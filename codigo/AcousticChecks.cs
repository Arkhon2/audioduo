using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace AudioDuo
{
    static class AcousticChecks
    {
        static void Require(bool pass, string message) { if (!pass) throw new Exception(message); }
        static void Add(float[] data, int code, double ms, float scale, bool quadrature)
        {
            int start = (int)Math.Round(ms * AcousticCapture.Rate / 1000);
            for (int j = 0; j < AcousticProbe.PulseSeconds * AcousticCapture.Rate; j++) data[start + j] += scale * AcousticProbe.Sample(j / (double)AcousticCapture.Rate, code, quadrature);
        }
        public static void Run()
        {
            var reference = new float[AcousticCapture.Rate * 8]; var microphone = new float[reference.Length]; var inconsistent = new float[reference.Length];
            var random = new Random(2026);
            for (int i = 0; i < microphone.Length; i++) microphone[i] = (float)((random.NextDouble() - .5) * .0005);
            for (int code = 0; code < 3; code++)
            {
                double time = AcousticProbe.Times[code] * 1000 + 70;
                Add(reference, code, time, .2f, false);
                Add(microphone, code, time + 387, .02f, false); Add(microphone, code, time + 387, .01f, true);
                Add(microphone, code, time + 421, .006f, false);
                Add(inconsistent, code, time + 100 + code * 35, .02f, false);
            }
            double confidence;
            var measured = ProbeMatcher.Relative(reference, microphone, CancellationToken.None, out confidence, 70);
            Require(measured.All(value => Math.Abs(value - 387) <= 1), "La correlación acústica no recuperó el retraso conocido con ruido/fase/eco.");
            bool rejected = false;
            try { ProbeMatcher.Relative(reference, new float[reference.Length], CancellationToken.None, out confidence, 70); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "No se rechazó un micrófono sin señal."); rejected = false;
            try { ProbeMatcher.Relative(reference, inconsistent, CancellationToken.None, out confidence, 70); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "No se rechazó una medición inestable.");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel(); rejected = false;
                try { ProbeMatcher.Find(reference, 0, 0, 2000, cancellation.Token); } catch (OperationCanceledException) { rejected = true; }
                Require(rejected, "No se pudo cancelar el análisis.");
            }
            var plan = AcousticCalibrator.BuildPlan(new List<AcousticSpeaker> { new AcousticSpeaker { Id = "fast", PhysicalMs = 100, PipelineMs = 80 }, new AcousticSpeaker { Id = "slow", PhysicalMs = 600 } });
            var capturedImpulse = new float[16000]; capturedImpulse[7000] = .7f;
            var envelope = AcousticWaveView.Envelope(capturedImpulse, 250);
            Require(envelope.Length == 360 && envelope[125] == .7f && envelope.Count(v => v != 0) == 1, "La onda acústica no conserva el tiempo del sonido respecto al envío del tono.");
            Require(AcousticWaveView.Envelope(new float[16000], 250).All(v => v == 0), "La vista acústica inventó una señal en silencio.");
            Require(plan.CanApply && plan.Speakers.First(p => p.Reference).Id == "slow" && plan.Speakers.First(p => !p.Reference).DelayMs == 420, "El plan no compensó el coste de la ruta o no eligió el más lento.");
            plan = AcousticCalibrator.BuildPlan(new List<AcousticSpeaker> { new AcousticSpeaker { Id = "a", PhysicalMs = 550, PipelineMs = 80 }, new AcousticSpeaker { Id = "b", PhysicalMs = 600 } });
            Require(!plan.CanApply, "Se aprobó un ajuste que requiere adelantar audio.");
            plan = AcousticCalibrator.BuildBridgePlan(new List<AcousticSpeaker> { new AcousticSpeaker { Id = "MS", PhysicalMs = 100, PipelineMs = 113 }, new AcousticSpeaker { Id = "JHW", PhysicalMs = 122, PipelineMs = 110 } }, new DeviceItem { Id = "analog", Name = "Puente" });
            Require(plan.CanApply && plan.UsesBridge && plan.BridgeId == "analog" && plan.Speakers[0].DelayMs == 19 && plan.Speakers[1].DelayMs == 0, "El puente no compensó las dos rutas de forma independiente.");
            var wired = new DeviceItem { Id = "wired", Name = "High Definition Audio" };
            var virtualCable = new DeviceItem { Id = "virtual", Name = "CABLE Input (VB-Audio Virtual Cable)" };
            var bluetooth = new DeviceItem { Id = "bt", Name = "Bluetooth" };
            var chosen = MainForm.ChooseCalibrationBridge(new[] { wired, bluetooth, virtualCable }, new[] { wired, bluetooth }, wired, true);
            Require(chosen == virtualCable, "La calibración reutilizó como puente un parlante que debe sonar.");
            chosen = MainForm.ChooseCalibrationBridge(new[] { wired, bluetooth, virtualCable }, new[] { bluetooth }, virtualCable, true);
            Require(chosen == virtualCable, "La calibración cambió el puente virtual existente.");
            var multiCable = new DeviceItem { Id = "virtual16", Name = "CABLE In 16ch (VB-Audio Virtual Cable)" };
            chosen = MainForm.ChooseCalibrationBridge(new[] { multiCable, virtualCable }, new[] { wired, bluetooth }, null, false);
            Require(chosen == virtualCable, "Se eligió el origen de 16 canales en vez del estéreo.");
            using (var view = new WaveView(new WaveHistory(), new WaveHistory()))
            {
                view.SetTraces(new List<WaveTrace> { new WaveTrace { Label = "1", History = new WaveHistory() }, new WaveTrace { Label = "2", History = new WaveHistory() }, new WaveTrace { Label = "3", History = new WaveHistory() }, new WaveTrace { Label = "Detectado", History = null } });
                view.Freeze(); Require(view.TraceCount == 4 && view.Frozen && Math.Abs(view.CursorDelta - 600) < .1, "El monitor no congeló todas las salidas juntas."); view.Resume();
            }
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "validacion.txt"), "\r\nOK: correlación acústica con ruido/fase/eco, rechazo de silencio e inestabilidad, cancelación, elección del parlante lento, coste real de la ruta, bloqueo de adelantos imposibles y monitor de cuatro filas.");
        }
    }
}
