using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioDuo
{
    sealed class LiveAcousticRoute
    {
        public string Id, Name;
        public DelayVolume Processor;
        public int OriginalDelay;
        public float OriginalGain;
    }
    static partial class LiveAcousticCalibration
    {
        public static AcousticPlan Run(string microphone, DeviceItem bridge, LiveAcousticRoute[] routes, Action<string> progress, CancellationToken token)
        {
            using (var devices = new MMDeviceEnumerator())
            {
                using (var mic = devices.GetDevice(microphone))
                    if (mic.AudioEndpointVolume.Mute) throw new InvalidOperationException("El micrófono está silenciado en Windows.");
                var results = new List<AcousticSpeaker>();
                var log = new List<string> { "Calibración en vivo · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "Solo números; no se guarda audio del micrófono.", "" };
                var failures = new List<string>();
                try
                {
                    foreach (var route in routes) route.Processor.Gain = 0;
                    foreach (var route in routes)
                    {
                        // Every speaker is measured even if a previous one failed, with one automatic retry.
                        string failure = null;
                        for (int attempt = 1; attempt <= 2; attempt++)
                        {
                            token.ThrowIfCancellationRequested();
                            progress("Midiendo " + route.Name + (attempt > 1 ? " · segundo intento" : "") + " (" + (Array.IndexOf(routes, route) + 1) + " de " + routes.Length + ")…");
                            try { results.Add(MeasureRoute(devices, microphone, bridge, route, routes, log, attempt, token)); failure = null; break; }
                            catch (InvalidOperationException ex) { failure = ex.Message; log.Add("  intento " + attempt + " falló: " + ex.Message); }
                            finally { route.Processor.Gain = 0; }
                        }
                        if (failure != null) failures.Add(route.Name + ": " + failure);
                    }
                    foreach (var route in routes)
                        if (route.Processor.RequestedDelay != route.OriginalDelay) throw new InvalidOperationException("El retraso cambió durante la medición. Repite sin mover los controles.");
                    if (failures.Count > 0)
                        throw new InvalidOperationException("Se midieron " + results.Count + " de " + routes.Length + " parlantes. No se aplicó ningún ajuste.\r\n" + string.Join("\r\n", failures));
                    var plan = AcousticCalibrator.BuildBridgePlan(results, bridge);
                    log.Add(""); foreach (var s in plan.Speakers) log.Add("Plan · " + s.Name + ": " + s.DelayMs + " ms (antes " + routes.First(r => r.Id == s.Id).OriginalDelay + " ms)");
                    log.Add("Demora total frente al video: " + plan.TotalMs.ToString("0") + " ms");
                    return plan;
                }
                finally
                {
                    foreach (var route in routes) route.Processor.Gain = route.OriginalGain;
                    try { File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "calibracion-ultima.txt"), log); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
        }
        static string Tones(float[] reference, float[] measured, double sentAt, CancellationToken token)
        {
            var parts = new List<string>();
            for (int code = 0; code < 3; code++)
            {
                var sent = ProbeMatcher.Find(reference, code, AcousticProbe.Times[code] * 1000 + sentAt - 250, AcousticProbe.Times[code] * 1000 + sentAt + 600, token);
                var heard = ProbeMatcher.Find(measured, code, sent.Ms - 100, sent.Ms + 1800, token);
                parts.Add((heard.Ms - sent.Ms).ToString("0.0") + " ms (puntaje " + heard.Score.ToString("0.00") + ")");
            }
            return string.Join(" · ", parts);
        }
        static AcousticSpeaker MeasureRoute(MMDeviceEnumerator devices, string microphone, DeviceItem bridge, LiveAcousticRoute route, LiveAcousticRoute[] routes, List<string> log, int attempt, CancellationToken token)
        {
            foreach (var other in routes) other.Processor.Gain = other == route ? route.OriginalGain : 0;
            if (route.OriginalGain < .05f) throw new InvalidOperationException("Sube el volumen de " + route.Name + " antes de medir.");
            if (token.WaitHandle.WaitOne(150)) token.ThrowIfCancellationRequested();
            using (var target = devices.GetDevice(route.Id))
                if (target.AudioEndpointVolume.Mute) throw new InvalidOperationException(route.Name + " está silenciado en Windows.");
            double origin = WaveHistory.Now;
            using (var input = new AcousticCapture(devices, microphone, false, origin))
            using (var source = new AcousticCapture(devices, bridge.Id, true, origin))
            using (var output = new AcousticCapture(devices, route.Id, true, origin))
            using (var sendDevice = devices.GetDevice(bridge.Id))
            using (var player = new WasapiOut(sendDevice, AudioClientShareMode.Shared, false, 50))
            {
                player.Init(new SampleToWaveProvider(new AcousticProbe()));
                double sentAt = WaveHistory.Now - origin; player.Play();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 6800)
                {
                    token.ThrowIfCancellationRequested(); input.Pump(); source.Pump(); output.Pump();
                    if (token.WaitHandle.WaitOne(3)) token.ThrowIfCancellationRequested();
                }
                player.Stop(); input.Pump(); source.Pump(); output.Pump();
                double confidence;
                var sent = source.Samples(); var rendered = output.Samples(); var microphoneSamples = input.Samples();
                // The plan uses only bridge → microphone, the delay actually heard. Windows mixes Bluetooth
                // endpoints in irregular chunks, so their loopback jitters ±15 ms even when the sound does not;
                // the intermediate digital stage is kept as information and never rejects a measurement.
                float micPeak = microphoneSamples.Max(v => Math.Abs(v));
                log.Add(route.Name + " · intento " + attempt + " · retraso " + route.OriginalDelay + " ms · volumen " + Math.Round(route.OriginalGain * 100) + " % · pico del micrófono " + (micPeak > 0 ? (20 * Math.Log10(micPeak)).ToString("0.0") + " dBFS" : "silencio"));
                try { log.Add("  envío → micrófono: " + Tones(sent, microphoneSamples, sentAt, token)); log.Add("  envío → salida digital: " + Tones(sent, rendered, sentAt, token)); }
                catch (ArgumentException) { }
                double[] heard = ProbeMatcher.Relative(sent, microphoneSamples, token, out confidence, sentAt);
                double heardMs = ProbeMatcher.Median(heard), digitalMs = route.OriginalDelay;
                try { double ignored; digitalMs = ProbeMatcher.Median(ProbeMatcher.Relative(sent, rendered, token, out ignored, sentAt, double.MaxValue)); }
                catch (InvalidOperationException) { }
                var referencePeak = ProbeMatcher.Find(sent, 0, sentAt + 550, sentAt + 1400, token);
                log.Add("  OK · llegada " + heardMs.ToString("0.0") + " ms · variación " + (heard.Max() - heard.Min()).ToString("0.0") + " ms");
                return new AcousticSpeaker { Id = route.Id, Name = route.Name, PhysicalMs = heardMs - digitalMs, PipelineMs = digitalMs - route.OriginalDelay, SpreadMs = heard.Max() - heard.Min(), Score = confidence, HeardMs = heardMs, AcousticEnvelope = AcousticWaveView.Envelope(microphoneSamples, referencePeak.Ms) };
            }
        }
    }
    sealed partial class MainForm
    {
        LiveAcousticRoute[] GetLiveCalibrationRoutes(DeviceItem[] selected)
        {
            if (!running || !usingBridge || processor == null || activeExtras.Length != extraSpeakers.Count(p => p.Enabled)) return null;
            var copy = target.SelectedItem as DeviceItem;
            if (copy == null) return null;
            var routes = new List<LiveAcousticRoute> { new LiveAcousticRoute { Id = copy.Id, Name = copy.Name, Processor = processor, OriginalDelay = processor.RequestedDelay, OriginalGain = processor.Gain } };
            foreach (var profile in extraSpeakers.Where(p => p.Enabled && p.Branch != null))
                routes.Add(new LiveAcousticRoute { Id = profile.Id, Name = profile.Name, Processor = profile.Branch.Delay, OriginalDelay = profile.Branch.Delay.RequestedDelay, OriginalGain = profile.Branch.Delay.Gain });
            if (routes.Count != selected.Length || routes.Any(r => !selected.Any(s => s.Id == r.Id))) return null;
            return routes.ToArray();
        }
        void ApplyLiveAcousticPlan(AcousticPlan plan)
        {
            SaveSettings();
            if (File.Exists(settings)) File.Copy(settings, settings + ".antes-calibrar", true);
            if (File.Exists(outputsFile)) File.Copy(outputsFile, outputsFile + ".antes-calibrar", true);
            var copy = target.SelectedItem as DeviceItem;
            foreach (var item in plan.Speakers)
            {
                if (copy != null && item.Id == copy.Id)
                {
                    delay.Value = item.DelayMs; referenceDelay = item.DelayMs; savedDelay.Text = "Volver a " + referenceDelay + " ms";
                }
                else
                {
                    var profile = extraSpeakers.First(p => p.Id == item.Id);
                    profile.Delay = item.DelayMs; SpeakerChanged(profile);
                }
            }
            overviewSignature = null; SaveSettings(); RefreshSpeakerOverview();
            status.Text = "Calibración aplicada en vivo · conexiones conservadas.";
        }
    }
}
