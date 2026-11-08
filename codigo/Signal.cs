using System;
using System.Diagnostics;
using NAudio.Wave;

namespace AudioDuo
{
    // Audio-thread-owned ring. UI writes only the requested delay; no allocation or reset on retune.
    sealed class DelayVolume : ISampleProvider
    {
        readonly ISampleProvider input;
        readonly float[] history;
        readonly int fadeFrames;
        int position, channel, applied, previous, fadePosition;
        bool initialized;
        volatile int requested;
        float actualGain;
        public volatile float Gain = 0.65f;
        public WaveFormat WaveFormat { get { return input.WaveFormat; } }
        public int RequestedDelay { get { return requested; } }
        public DelayVolume(ISampleProvider input)
        {
            this.input = input;
            history = new float[(WaveFormat.SampleRate * 2 + 1) * WaveFormat.Channels];
            fadeFrames = Math.Max(1, WaveFormat.SampleRate * 35 / 1000);
            fadePosition = fadeFrames;
        }
        public void SetDelay(int ms)
        {
            if (ms < 0 || ms > 1500) throw new ArgumentOutOfRangeException("ms");
            requested = ms;
        }
        public int Read(float[] buffer, int offset, int count)
        {
            int read = input.Read(buffer, offset, count);
            int channels = WaveFormat.Channels;
            if (!initialized) { applied = previous = WaveFormat.SampleRate * requested / 1000; actualGain = Gain; initialized = true; }
            for (int i = offset; i < offset + read; i++)
            {
                if (channel == 0 && fadePosition >= fadeFrames)
                {
                    int next = WaveFormat.SampleRate * requested / 1000;
                    if (next != applied) { previous = applied; applied = next; fadePosition = 0; }
                }
                history[position] = buffer[i];
                int tap = position - applied * channels;
                if (tap < 0) tap += history.Length;
                float value = history[tap];
                if (fadePosition < fadeFrames)
                {
                    int oldTap = position - previous * channels;
                    if (oldTap < 0) oldTap += history.Length;
                    float blend = (float)fadePosition / fadeFrames;
                    value = history[oldTap] * (1 - blend) + value * blend;
                }
                if (channel == 0)
                {
                    float difference = Gain - actualGain;
                    actualGain += Math.Max(-0.002f, Math.Min(0.002f, difference));
                }
                buffer[i] = value * actualGain;
                if (++position == history.Length) position = 0;
                if (++channel == channels) { channel = 0; if (fadePosition < fadeFrames) fadePosition++; }
            }
            return read;
        }
    }

    // Shared monotonic time, 5 ms peak bins. Callback arrival is approximate, not an acoustic clock.
    sealed class WaveHistory
    {
        public const int BinMs = 5, Capacity = 3200;
        readonly object gate = new object();
        readonly long[] stamps = new long[Capacity];
        readonly float[] peaks = new float[Capacity];
        public static double Now { get { return Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency; } }
        public void Clear() { lock (gate) { Array.Clear(stamps, 0, stamps.Length); Array.Clear(peaks, 0, peaks.Length); } }
        public void Add(byte[] data, int count, WaveFormat format)
        {
            int frames = count / format.BlockAlign;
            double first = Now - frames * 1000.0 / format.SampleRate;
            lock (gate)
            {
                for (int f = 0; f < frames; f++)
                {
                    float peak = 0;
                    for (int c = 0; c < format.Channels; c++)
                    {
                        int offset = f * format.BlockAlign + c * format.BitsPerSample / 8;
                        float value;
                        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32) value = BitConverter.ToSingle(data, offset);
                        else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16) value = BitConverter.ToInt16(data, offset) / 32768f;
                        else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 24)
                        {
                            int v = data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16;
                            if ((v & 0x800000) != 0) v |= unchecked((int)0xff000000);
                            value = v / 8388608f;
                        }
                        else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 32) value = BitConverter.ToInt32(data, offset) / 2147483648f;
                        else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 8) value = (data[offset] - 128) / 128f;
                        else throw new NotSupportedException("Formato no compatible con la visualización: " + format);
                        if (!float.IsNaN(value) && !float.IsInfinity(value)) peak = Math.Max(peak, Math.Abs(value));
                    }
                    long stamp = (long)((first + f * 1000.0 / format.SampleRate) / BinMs);
                    int slot = (int)(stamp % Capacity);
                    if (stamps[slot] != stamp) { stamps[slot] = stamp; peaks[slot] = 0; }
                    peaks[slot] = Math.Max(peaks[slot], peak);
                }
            }
        }
        public float[] Snapshot(double endMs, int windowMs)
        {
            int bins = windowMs / BinMs;
            var result = new float[bins];
            long end = (long)(endMs / BinMs);
            lock (gate)
                for (int i = 0; i < bins; i++)
                {
                    long stamp = end - bins + i;
                    if (stamp < 0) continue;
                    int slot = (int)(stamp % Capacity);
                    if (stamps[slot] == stamp) result[i] = peaks[slot];
                }
            return result;
        }
        public float Peak
        {
            get
            {
                float max = 0; var values = Snapshot(Now, 150);
                foreach (float value in values) max = Math.Max(max, value);
                return max;
            }
        }
    }
}
