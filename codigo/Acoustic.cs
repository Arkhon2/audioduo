using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioDuo
{
    // WASAPI packet QPC timestamps (100 ns) put microphone and loopback samples on one clock.
    // Audio never leaves these bounded in-memory buffers.
    sealed class AcousticCapture : IDisposable
    {
        public const int Rate = 8000, DurationMs = 15000;
        readonly MMDevice device;
        readonly AudioClient client;
        readonly AudioCaptureClient capture;
        readonly WaveFormat format;
        readonly double origin;
        readonly float[] sum = new float[Rate * DurationMs / 1000];
        readonly int[] counts = new int[Rate * DurationMs / 1000];
        byte[] bytes = new byte[0];
        bool started;
        public bool TimestampError;
        public AcousticCapture(MMDeviceEnumerator devices, string id, bool loopback, double origin)
        {
            this.origin = origin;
            try
            {
                device = devices.GetDevice(id); client = device.AudioClient;
                WaveFormat native = client.MixFormat;
                format = native is WaveFormatExtensible ? ((WaveFormatExtensible)native).ToStandardWaveFormat() : native;
                client.Initialize(AudioClientShareMode.Shared, loopback ? AudioClientStreamFlags.Loopback : (AudioClientStreamFlags)0, 1000000, 0, native, Guid.Empty);
                capture = client.AudioCaptureClient; client.Start(); started = true;
            }
            catch { Dispose(); throw; }
        }
        public void Pump()
        {
            while (capture.GetNextPacketSize() > 0)
            {
                int frames; AudioClientBufferFlags flags; long position, qpc;
                IntPtr pointer = capture.GetBuffer(out frames, out flags, out position, out qpc);
                try
                {
                    if (((int)flags & 4) != 0 || qpc <= 0) { TimestampError = true; continue; }
                    if ((flags & AudioClientBufferFlags.Silent) != 0) continue;
                    int size = frames * format.BlockAlign; if (bytes.Length < size) bytes = new byte[size];
                    Marshal.Copy(pointer, bytes, 0, size);
                    double start = qpc / 10000.0 - origin;
                    for (int f = 0; f < frames; f++)
                    {
                        int index = (int)Math.Floor((start + f * 1000.0 / format.SampleRate) * Rate / 1000.0);
                        if (index < 0 || index >= sum.Length) continue;
                        float mono = 0;
                        for (int c = 0; c < format.Channels; c++) mono += Decode(bytes, f * format.BlockAlign + c * format.BitsPerSample / 8, format);
                        mono /= format.Channels;
                        if (float.IsNaN(mono) || float.IsInfinity(mono)) continue;
                        sum[index] += mono; counts[index]++;
                    }
                }
                finally { capture.ReleaseBuffer(frames); }
            }
        }
        static float Decode(byte[] b, int i, WaveFormat f)
        {
            if (f.Encoding == WaveFormatEncoding.IeeeFloat && f.BitsPerSample == 32) return BitConverter.ToSingle(b, i);
            if (f.Encoding == WaveFormatEncoding.Pcm && f.BitsPerSample == 16) return BitConverter.ToInt16(b, i) / 32768f;
            if (f.Encoding == WaveFormatEncoding.Pcm && f.BitsPerSample == 32) return BitConverter.ToInt32(b, i) / 2147483648f;
            if (f.Encoding == WaveFormatEncoding.Pcm && f.BitsPerSample == 24)
            {
                int value = b[i] | b[i + 1] << 8 | b[i + 2] << 16; if ((value & 0x800000) != 0) value |= unchecked((int)0xff000000); return value / 8388608f;
            }
            throw new NotSupportedException("Formato del micrófono no compatible: " + f);
        }
        public float[] Samples()
        {
            if (TimestampError) throw new InvalidOperationException("Windows informó marcas de tiempo inválidas; repite la medición.");
            var data = new float[sum.Length];
            for (int i = 0; i < data.Length; i++) if (counts[i] > 0) data[i] = sum[i] / counts[i];
            return data;
        }
        public void Dispose()
        {
            if (client != null) { if (started) { client.Stop(); started = false; } client.Dispose(); }
            if (device != null) device.Dispose();
        }
    }
    sealed class AcousticProbe : ISampleProvider
    {
        public static readonly double[] Times = { .8, 2.8, 4.8 };
        public const double PulseSeconds = .055;
        static readonly double[] Low = { 1000, 1400, 800 }, High = { 2800, 3200, 2400 };
        long frame;
        public WaveFormat WaveFormat { get; private set; }
        public AcousticProbe() { WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2); }
        public static float Sample(double time, int code, bool quadrature)
        {
            if (time < 0 || time >= PulseSeconds) return 0;
            double low = Low[code], high = High[code];
            double phase = 2 * Math.PI * (low * time + (high - low) * time * time / (2 * PulseSeconds));
            return (float)(.5 * (1 - Math.Cos(2 * Math.PI * time / PulseSeconds)) * (quadrature ? Math.Cos(phase) : Math.Sin(phase)));
        }
        public int Read(float[] buffer, int offset, int count)
        {
            int n = (int)Math.Min(count / 2, 48000 * 7 - frame);
            for (int f = 0; f < n; f++, frame++)
            {
                double t = frame / 48000.0; float value = 0;
                if (t >= .1 && t < .1 + PulseSeconds) value = .12f * Sample(t - .1, 2, false);
                for (int code = 0; code < Times.Length; code++) if (t >= Times[code] && t < Times[code] + PulseSeconds) value = .2f * Sample(t - Times[code], code, false);
                buffer[offset + 2 * f] = buffer[offset + 2 * f + 1] = value;
            }
            return n * 2;
        }
    }
    sealed class ProbePeak
    {
        public double Ms, Score;
    }
    static class ProbeMatcher
    {
        public static ProbePeak Find(float[] data, int code, double fromMs, double toMs, CancellationToken token)
        {
            int rate = AcousticCapture.Rate, length = (int)(AcousticProbe.PulseSeconds * rate);
            var sine = new float[length]; var cosine = new float[length]; double es = 0, ec = 0;
            for (int j = 0; j < length; j++) { sine[j] = AcousticProbe.Sample(j / (double)rate, code, false); cosine[j] = AcousticProbe.Sample(j / (double)rate, code, true); es += sine[j] * sine[j]; ec += cosine[j] * cosine[j]; }
            int start = Math.Max(0, (int)(fromMs * rate / 1000)), end = Math.Min(data.Length - length, (int)(toMs * rate / 1000));
            var result = new ProbePeak { Score = 0 };
            double energy = 0; for (int j = start; j < start + length && j < data.Length; j++) energy += data[j] * data[j];
            for (int i = start; i <= end; i++)
            {
                if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                if (energy > 1e-9)
                {
                    double a = 0, b = 0;
                    for (int j = 0; j < length; j++) { a += data[i + j] * sine[j]; b += data[i + j] * cosine[j]; }
                    double score = Math.Sqrt((a * a / es + b * b / ec) / energy);
                    if (score > result.Score) { result.Score = Math.Min(1, score); result.Ms = i * 1000.0 / rate; }
                }
                if (i < end) energy = Math.Max(0, energy - data[i] * data[i] + data[i + length] * data[i + length]);
            }
            return result;
        }
        public static double[] Relative(float[] reference, float[] measured, CancellationToken token, out double confidence, double sentOffsetMs = 0, double maxSpreadMs = 25)
        {
            var delays = new double[3]; confidence = 1;
            for (int code = 0; code < 3; code++)
            {
                double expected = AcousticProbe.Times[code] * 1000 + sentOffsetMs;
                var sent = Find(reference, code, expected - 250, expected + 600, token);
                if (sent.Score < .5) throw new InvalidOperationException("No se identificó el tono digital de referencia. Pausa música/videos y repite.");
                var heard = Find(measured, code, sent.Ms - 100, sent.Ms + 1800, token);
                if (heard.Score < .16) throw new InvalidOperationException("El micrófono no distinguió el tono. Acércalo al punto de escucha y revisa volumen y silencio.");
                delays[code] = heard.Ms - sent.Ms; confidence = Math.Min(confidence, heard.Score);
            }
            if (delays.Max() - delays.Min() > maxSpreadMs) throw new InvalidOperationException("Medición inestable: " + string.Join(", ", delays.Select(value => value.ToString("0.0"))) + " ms. No se aplicó ningún ajuste.");
            return delays;
        }
        public static double Median(double[] values) { var sorted = values.OrderBy(v => v).ToArray(); return sorted[sorted.Length / 2]; }
    }
    sealed class AcousticSpeaker
    {
        public float[] AcousticEnvelope;
        public double HeardMs;
        public string Id, Name;
        public double PhysicalMs, SpreadMs, Score, PipelineMs;
        public int DelayMs;
        public bool Reference;
    }
    sealed class AcousticPlan
    {
        public List<AcousticSpeaker> Speakers;
        public bool CanApply;
        public string Explanation;
        public bool UsesBridge;
        public double TotalMs;
        public string BridgeId, BridgeName;
    }
    static class AcousticCalibrator
    {
        static void Pump(List<AcousticCapture> captures, int milliseconds, CancellationToken token)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < milliseconds)
            {
                token.ThrowIfCancellationRequested(); foreach (var capture in captures) capture.Pump();
                if (token.WaitHandle.WaitOne(3)) token.ThrowIfCancellationRequested();
            }
            foreach (var capture in captures) capture.Pump();
        }
        public static AcousticPlan Run(string microphone, DeviceItem[] speakers, Action<string> progress, CancellationToken token, DeviceItem bridge = null)
        {
            if (speakers.Length < 2) throw new InvalidOperationException("Selecciona al menos dos parlantes.");
            using (var devices = new MMDeviceEnumerator())
            {
                using (var mic = devices.GetDevice(microphone))
                    if (mic.AudioEndpointVolume.Mute) throw new InvalidOperationException("El micrófono está silenciado en Windows.");
                var results = new List<AcousticSpeaker>();
                foreach (var speaker in speakers)
                {
                    token.ThrowIfCancellationRequested(); progress("Escuchando " + speaker.Name + " · tres tonos breves…");
                    using (var device = devices.GetDevice(speaker.Id))
                    {
                        if (device.AudioEndpointVolume.Mute || device.AudioEndpointVolume.MasterVolumeLevelScalar < .01f) throw new InvalidOperationException(speaker.Name + " está silenciado o sin volumen.");
                        double origin = WaveHistory.Now;
                        using (var micCapture = new AcousticCapture(devices, microphone, false, origin))
                        using (var digital = new AcousticCapture(devices, speaker.Id, true, origin))
                        using (var player = new WasapiOut(device, AudioClientShareMode.Shared, false, 50))
                        {
                            player.Init(new SampleToWaveProvider(new AcousticProbe())); double sentAt = WaveHistory.Now - origin; player.Play();
                            Pump(new List<AcousticCapture> { micCapture, digital }, 6800, token); player.Stop();
                            progress("Analizando " + speaker.Name + "…"); double confidence;
                            // A Bluetooth endpoint's own loopback jitters about ±15 ms; 40 ms still rejects a speaker that re-buffers.
                            var delays = ProbeMatcher.Relative(digital.Samples(), micCapture.Samples(), token, out confidence, sentAt, 40);
                            results.Add(new AcousticSpeaker { Id = speaker.Id, Name = speaker.Name, PhysicalMs = ProbeMatcher.Median(delays), SpreadMs = delays.Max() - delays.Min(), Score = confidence });
                        }
                    }
                }
                var reference = results.OrderByDescending(r => r.PhysicalMs).First(); reference.Reference = true;
                string pipelineId = bridge == null ? reference.Id : bridge.Id;
                var routed = results.Where(r => bridge != null || !r.Reference).ToArray();
                progress(bridge == null ? "Comprobando la ruta real desde " + reference.Name + "…" : "Comprobando el puente silencioso y las rutas de los parlantes…");
                // Measure actual loopback->render overhead at zero added delay, on the same routes used by the app.
                double pipelineOrigin = WaveHistory.Now;
                var recorders = new List<AcousticCapture>(); var branches = new List<OutputBranch>();
                try
                {
                    using (var refDevice = devices.GetDevice(pipelineId))
                    using (var sendDevice = devices.GetDevice(pipelineId))
                    using (var forward = new LowLatencyLoopback(refDevice))
                    using (var player = new WasapiOut(sendDevice, AudioClientShareMode.Shared, false, 50))
                    {
                        bool oldMute = refDevice.AudioEndpointVolume.Mute;
                        try
                        {
                        if (bridge != null) refDevice.AudioEndpointVolume.Mute = true;
                        recorders.Add(new AcousticCapture(devices, pipelineId, true, pipelineOrigin));
                        foreach (var speaker in routed)
                        {
                            recorders.Add(new AcousticCapture(devices, speaker.Id, true, pipelineOrigin));
                            branches.Add(new OutputBranch(devices, new SpeakerProfile { Id = speaker.Id, Name = speaker.Name, Delay = 0, Volume = 100, Route = 0 }, forward.WaveFormat));
                        }
                        Exception forwardFailure = null;
                        forward.DataAvailable += delegate(object sender, WaveInEventArgs e) { foreach (var branch in branches) branch.Feed(e.Buffer, e.BytesRecorded); };
                        forward.RecordingStopped += delegate(object sender, StoppedEventArgs e) { if (e.Exception != null) forwardFailure = e.Exception; };
                        player.Init(new SampleToWaveProvider(new AcousticProbe())); forward.StartRecording(); double sentAt = WaveHistory.Now - pipelineOrigin; player.Play();
                        try { Pump(recorders, 6800, token); }
                        finally { player.Stop(); forward.StopRecording(); forward.Dispose(); }
                        if (forwardFailure != null) throw forwardFailure;
                        var source = recorders[0].Samples(); int index = 1;
                        foreach (var target in routed)
                        {
                            // Bluetooth loopback jitters in chunks; the median is used, the spread is not a rejection criterion here.
                            double confidence; var lags = ProbeMatcher.Relative(source, recorders[index++].Samples(), token, out confidence, sentAt, double.MaxValue);
                            target.PipelineMs = ProbeMatcher.Median(lags); target.SpreadMs = Math.Max(target.SpreadMs, lags.Max() - lags.Min());
                        }
                        }
                        finally { if (bridge != null) refDevice.AudioEndpointVolume.Mute = oldMute; }
                    }
                }
                finally { foreach (var branch in branches) branch.Dispose(); foreach (var recorder in recorders) recorder.Dispose(); }
                return bridge == null ? BuildPlan(results) : BuildBridgePlan(results, bridge);
            }
        }
        internal static AcousticPlan BuildBridgePlan(List<AcousticSpeaker> results, DeviceItem bridge)
        {
            double latest = results.Max(r => r.PhysicalMs + r.PipelineMs);
            var plan = new AcousticPlan { Speakers = results, CanApply = true, UsesBridge = true, BridgeId = bridge.Id, BridgeName = bridge.Name, TotalMs = latest,
                Explanation = "Puente silencioso: " + bridge.Name + ". Cada parlante recibe una copia con su propio retraso; el origen queda silenciado. Selecciona el puente como salida de Windows para usar estos ajustes." };
            foreach (var speaker in results)
            {
                speaker.Reference = false; double delay = latest - speaker.PhysicalMs - speaker.PipelineMs;
                if (delay > 1500) plan.CanApply = false;
                speaker.DelayMs = (int)Math.Round(Math.Max(0, Math.Min(1500, delay)));
            }
            return plan;
        }
        internal static AcousticPlan BuildPlan(List<AcousticSpeaker> results)
        {
                var reference = results.OrderByDescending(r => r.PhysicalMs).First();
                foreach (var speaker in results) speaker.Reference = speaker == reference;
                var plan = new AcousticPlan { Speakers = results, CanApply = true, Explanation = "Se eligió como principal el parlante que llega más tarde. Los demás reciben retraso para alcanzarlo." };
                foreach (var target in results.Where(r => !r.Reference))
                {
                    double needed = reference.PhysicalMs - target.PhysicalMs - target.PipelineMs;
                    if (needed < -8 || needed > 1500)
                    {
                        plan.CanApply = false;
                        plan.Explanation = "Esta ruta necesitaría adelantar una copia o superar 1500 ms. Para sincronizar este equipo se necesita una salida virtual que permita retrasar también el principal. No se cambiaron tus ajustes.";
                    }
                    target.DelayMs = (int)Math.Round(Math.Max(0, Math.Min(1500, needed)));
                }
                return plan;
        }
    }
}
