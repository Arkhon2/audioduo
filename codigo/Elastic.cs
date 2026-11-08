using System;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioDuo
{
    // Loopback that is polled every ~15 ms. NAudio's WasapiLoopbackCapture polls every ~50 ms,
    // which delivers the audio in large bursts and forces every queue to hold more backlog.
    sealed class LowLatencyLoopback : WasapiCapture
    {
        public const int BufferMs = 30;
        public LowLatencyLoopback(MMDevice device) : base(device, false, BufferMs) { }
        protected override AudioClientStreamFlags GetAudioClientStreamFlags() { return base.GetAudioClientStreamFlags() | AudioClientStreamFlags.Loopback; }
    }

    // Fixed-latency bridge between the capture clock (bridge endpoint) and one speaker's clock.
    // BufferedWaveProvider let each queue grow or drain freely (0–600 ms, then ClearBuffer), so every
    // speaker had a different, wandering delay and no calibration could hold. This feed:
    //  - waits until TargetMs of audio is queued before playing, and after any gap/underrun;
    //  - keeps the backlog at TargetMs by nudging the playback rate (at most ±0.4 %), which also
    //    absorbs clock drift between devices;
    //  - converts to the device's own sample rate (e.g. 48 kHz → 44.1 kHz Bluetooth), so WasapiOut
    //    does not insert its DMO resampler with its own buffering.
    sealed class ElasticFeed : ISampleProvider
    {
        public const int TargetMs = 30, MaxExcessMs = 120, RenderMs = 50;
        const double Smoothing = .05, CorrectionPerMs = .0003, MaxCorrection = .004;
        readonly object gate = new object();
        readonly WaveFormat source;
        readonly bool sourceFloat;
        readonly int bytesPerSample, channels, sourceRate, capacity;
        readonly float[] ring;
        readonly double baseStep, targetFrames, maxExcessFrames;
        long written, readPos;
        double frac, smoothed;
        bool waiting = true;
        volatile int levelMs, correctionPpm, refills, resyncs;
        float appliedInputGain = 1;
        // Multiplies the captured signal; ramped per packet so volume changes do not click.
        public volatile float InputGain = 1;
        public WaveFormat WaveFormat { get; private set; }
        public int LevelMs { get { return levelMs; } }
        public int CorrectionPpm { get { return correctionPpm; } }
        public int Refills { get { return refills; } }
        public int Resyncs { get { return resyncs; } }
        public bool Waiting { get { lock (gate) return waiting; } }

        public ElasticFeed(WaveFormat sourceFormat, int outputRate)
        {
            source = sourceFormat is WaveFormatExtensible ? ((WaveFormatExtensible)sourceFormat).ToStandardWaveFormat() : sourceFormat;
            sourceFloat = source.Encoding == WaveFormatEncoding.IeeeFloat;
            if (!(sourceFloat && source.BitsPerSample == 32) && !(source.Encoding == WaveFormatEncoding.Pcm && (source.BitsPerSample == 16 || source.BitsPerSample == 24 || source.BitsPerSample == 32)))
                throw new NotSupportedException("Formato de origen no compatible: " + sourceFormat);
            bytesPerSample = source.BitsPerSample / 8;
            channels = Math.Min(2, source.Channels);
            sourceRate = source.SampleRate;
            if (outputRate <= 0) outputRate = sourceRate;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(outputRate, channels);
            capacity = sourceRate * 2;
            ring = new float[capacity * channels];
            baseStep = (double)sourceRate / outputRate;
            targetFrames = sourceRate * TargetMs / 1000.0;
            maxExcessFrames = sourceRate * MaxExcessMs / 1000.0;
        }

        public static int DeviceRate(MMDevice device, int fallback)
        {
            try { using (var client = device.AudioClient) return client.MixFormat.SampleRate; }
            catch (Exception) { return fallback; }
        }

        float Decode(byte[] data, int index)
        {
            if (sourceFloat) return BitConverter.ToSingle(data, index);
            if (bytesPerSample == 2) return BitConverter.ToInt16(data, index) / 32768f;
            if (bytesPerSample == 3)
            {
                int v = data[index] | data[index + 1] << 8 | data[index + 2] << 16;
                if ((v & 0x800000) != 0) v |= unchecked((int)0xff000000);
                return v / 8388608f;
            }
            return BitConverter.ToInt32(data, index) / 2147483648f;
        }

        // Capture thread.
        public void Write(byte[] data, int count)
        {
            int frames = count / source.BlockAlign;
            lock (gate)
            {
                float startGain = appliedInputGain, endGain = InputGain;
                for (int f = 0; f < frames; f++)
                {
                    int slot = (int)(written % capacity) * channels;
                    float gain = startGain + (endGain - startGain) * (f + 1) / frames;
                    for (int c = 0; c < channels; c++)
                    {
                        float value = Decode(data, f * source.BlockAlign + c * bytesPerSample) * gain;
                        ring[slot + c] = float.IsNaN(value) || float.IsInfinity(value) ? 0 : value;
                    }
                    written++;
                }
                if (frames > 0) appliedInputGain = endGain;
                // A stalled speaker must not block the others: keep only the newest audio.
                if (written - readPos > capacity - 8) { readPos = written - (capacity - 8); frac = 0; }
            }
        }

        float At(long frame, int c) { return ring[(int)(((frame % capacity) + capacity) % capacity) * channels + c]; }

        static float Hermite(float x0, float x1, float x2, float x3, float t)
        {
            float c1 = .5f * (x2 - x0), c2 = x0 - 2.5f * x1 + 2f * x2 - .5f * x3, c3 = .5f * (x3 - x0) + 1.5f * (x1 - x2);
            return ((c3 * t + c2) * t + c1) * t + x1;
        }

        void Skip(double frames)
        {
            long whole = (long)Math.Floor(frames);
            if (whole > 0) readPos += whole;
        }

        // Render thread. Always returns the full count (silence while refilling), like ReadFully.
        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / channels;
            lock (gate)
            {
                double needed = frames * baseStep;
                double after = written - readPos - frac - needed;
                if (waiting)
                {
                    if (after < targetFrames) { Array.Clear(buffer, offset, count); levelMs = 0; return count; }
                    Skip(after - targetFrames); smoothed = targetFrames; waiting = false;
                }
                else if (after > targetFrames + maxExcessFrames)
                {
                    Skip(after - targetFrames); smoothed = targetFrames; resyncs++;
                }
                else smoothed += (after - smoothed) * Smoothing;
                double errorMs = (smoothed - targetFrames) * 1000.0 / sourceRate;
                double correction = Math.Max(-MaxCorrection, Math.Min(MaxCorrection, errorMs * CorrectionPerMs));
                double step = baseStep * (1 + correction);
                int f = 0;
                for (; f < frames; f++)
                {
                    if (readPos + 2 >= written) break;
                    float t = (float)frac;
                    for (int c = 0; c < channels; c++)
                        buffer[offset + f * channels + c] = Hermite(At(readPos - 1, c), At(readPos, c), At(readPos + 1, c), At(readPos + 2, c), t);
                    frac += step; long advance = (long)frac; readPos += advance; frac -= advance;
                }
                if (f < frames)
                {
                    // Source paused or stalled: play silence and refill to the same target before resuming.
                    Array.Clear(buffer, offset + f * channels, (frames - f) * channels);
                    waiting = true; refills++;
                }
                levelMs = (int)Math.Round(smoothed * 1000.0 / sourceRate);
                correctionPpm = (int)Math.Round(correction * 1e6);
            }
            return count;
        }
    }

    // Windows 11 applies timer resolution per process. Without this, the 15 ms polling of the
    // capture and render threads rounds up to 15.6/31.2 ms and causes bursts and glitches.
    static class TimerResolution
    {
        [DllImport("winmm.dll")] static extern int timeBeginPeriod(int period);
        [DllImport("winmm.dll")] static extern int timeEndPeriod(int period);
        static bool active;
        public static void Begin() { if (!active && timeBeginPeriod(1) == 0) active = true; }
        public static void End() { if (active) { timeEndPeriod(1); active = false; } }
    }
}
