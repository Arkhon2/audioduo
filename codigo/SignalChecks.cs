using System;
using System.Linq;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioDuo
{
    static class SignalChecks
    {
        sealed class Constant : ISampleProvider
        {
            int sample;
            public WaveFormat WaveFormat { get { return WaveFormat.CreateIeeeFloatWaveFormat(1000, 2); } }
            public int Read(float[] b, int o, int n) { for (int i = 0; i < n; i++, sample++) b[o + i] = sample % 2 == 0 ? 1 : -.25f; return n; }
        }
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        public static void Run()
        {
            var delayed = new DelayVolume(new Constant()) { Gain = 1 }; var block = new float[2000];
            delayed.Read(block, 0, 1000); delayed.SetDelay(310); delayed.Read(block, 0, 1000);
            for (int i = 0; i < 1000; i++) Check(Math.Abs(block[i] - (i % 2 == 0 ? 1 : -.25f)) < .00001, "El cambio de retraso borró el historial o cruzó canales.");
            delayed.SetDelay(0); delayed.Read(block, 0, 1000);
            for (int i = 0; i < 1000; i++) Check(Math.Abs(block[i] - (i % 2 == 0 ? 1 : -.25f)) < .00001, "El cambio a cero interrumpió la señal.");
            delayed.Gain = 0; delayed.Read(block, 0, 2000); Check(block.Skip(1600).All(v => Math.Abs(v) < .00001), "El volumen no llegó al silencio.");
            float[][] expected = { new[] { 1f, -.25f }, new[] { 1f, 1f }, new[] { -.25f, -.25f }, new[] { .375f, .375f }, new[] { .625f, .625f } };
            for (int r = 0; r < expected.Length; r++)
            {
                var router = new ChannelRouter(new Constant()) { Route = r }; router.Read(block, 0, 1000);
                for (int i = 0; i < 1000; i++) Check(Math.Abs(block[i] - expected[r][i % 2]) < .00001, "Distribución incorrecta del canal " + r);
            }
            var changing = new ChannelRouter(new Constant()); changing.Read(block, 0, 1000); changing.Route = 4; changing.Read(block, 0, 2000);
            Check(block.All(v => !float.IsNaN(v) && Math.Abs(v) <= 1.001), "Cambio de canal fuera de rango.");
            // Fan-out: same incoming PCM, three independent delay/route/gain paths.
            var format = WaveFormat.CreateIeeeFloatWaveFormat(1000, 2); var raw = new byte[1600];
            for (int i = 0; i < raw.Length / 4; i++) Array.Copy(BitConverter.GetBytes(i % 2 == 0 ? 1f : -.25f), 0, raw, i * 4, 4);
            var first = new BufferedWaveProvider(format) { ReadFully = true }; var second = new BufferedWaveProvider(format) { ReadFully = true }; var third = new BufferedWaveProvider(format) { ReadFully = true };
            first.AddSamples(raw, 0, raw.Length); second.AddSamples(raw, 0, raw.Length); third.AddSamples(raw, 0, raw.Length);
            var a = new DelayVolume(new ChannelRouter(first.ToSampleProvider()) { Route = 1 }) { Gain = 1 }; a.SetDelay(10);
            var b = new DelayVolume(new ChannelRouter(second.ToSampleProvider()) { Route = 2 }) { Gain = .5f }; b.SetDelay(30);
            var c = new DelayVolume(new ChannelRouter(third.ToSampleProvider()) { Route = 3 }) { Gain = 1 }; c.SetDelay(60);
            a.Read(block, 0, 400); Check(block[20] == 1 && block[0] == 0, "Salida 1 incorrecta.");
            b.Read(block, 0, 400); Check(block[60] == -.125f && block[20] == 0, "Salida 2 perdió independencia.");
            c.Read(block, 0, 400); Check(block[120] == .375f && block[60] == 0, "Salida 3 perdió independencia.");
            var history = new WaveHistory(); history.Add(raw, raw.Length, format);
            Check(history.Snapshot(WaveHistory.Now, 2000).Any(v => v > .9), "El monitor no leyó PCM real.");
            history.Clear(); Check(history.Snapshot(WaveHistory.Now, 2000).All(v => v == 0), "Hay ondas inventadas tras limpiar.");
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "validacion.txt"), "\r\nOK: cambios de retraso sin vaciar historial, estéreo intacto, silencio gradual, cinco matrices, cambio de canal acotado, tres salidas independientes y ondas de PCM real.");
            ElasticFeedChecks();
            RepairChecks();
        }
        // A speaker removed and paired again gets a new Windows ID; it must keep its role by name,
        // never take the device already used by the other role, and stay listed while switched off.
        static void RepairChecks()
        {
            using (var box = new System.Windows.Forms.ComboBox())
            {
                box.Items.Add(new DeviceItem { Id = "nuevo", Name = "Auriculares (MS-2054BT)" });
                box.Items.Add(new DeviceItem { Id = "gemelo", Name = "Auriculares (MS-2054BT)" });
                MainForm.SelectOrKeep(box, "viejo", "Auriculares (MS-2054BT)");
                Check(((DeviceItem)box.SelectedItem).Id == "nuevo", "No se reconoció por nombre un parlante emparejado de nuevo.");
                MainForm.SelectOrKeep(box, "viejo", "Auriculares (MS-2054BT)", "nuevo");
                Check(((DeviceItem)box.SelectedItem).Id == "gemelo", "Se asignó el mismo parlante a dos funciones.");
                MainForm.SelectOrKeep(box, "apagado", "Parlante X");
                var kept = (DeviceItem)box.SelectedItem;
                Check(kept.Id == "apagado" && kept.Offline, "Se perdió un parlante guardado que está apagado.");
            }
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "validacion.txt"), "\r\nOK: parlante emparejado de nuevo reconocido por nombre, sin duplicar funciones, y parlante apagado conservado.");
        }
        // Simulated clocks: capture delivers 10 ms packets at 48 kHz; a Bluetooth-like output pulls
        // 25 ms blocks at 44.1 kHz and runs 0.1 % fast. The queue must stay at its fixed target.
        static void ElasticFeedChecks()
        {
            var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
            var feed = new ElasticFeed(format, 44100);
            Check(feed.WaveFormat.SampleRate == 44100 && feed.WaveFormat.Channels == 2, "La cola no adoptó la frecuencia del parlante.");
            var bytes = new byte[48000 * 8]; var output = new float[44100 * 2];
            long sourceFrame = 0; double writeDebt = 0, readDebt = 0;
            int crossings = 0, minLevel = int.MaxValue, maxLevel = 0, refillsBeforeGap = -1; float previous = 0, peak = 0;
            for (int ms = 0; ms < 90000; ms++)
            {
                bool gap = ms >= 60000 && ms < 60300;
                if (ms % 10 == 0 && !gap)
                {
                    writeDebt += 480; int n = (int)writeDebt; writeDebt -= n;
                    for (int f = 0; f < n; f++, sourceFrame++)
                    {
                        var value = BitConverter.GetBytes((float)(.5 * Math.Sin(2 * Math.PI * 1000 * sourceFrame / 48000.0)));
                        Array.Copy(value, 0, bytes, f * 8, 4); Array.Copy(value, 0, bytes, f * 8 + 4, 4);
                    }
                    feed.Write(bytes, n * 8);
                }
                if (ms % 25 == 0)
                {
                    readDebt += 25 * 44.1 * 1.001; int n = (int)readDebt; readDebt -= n;
                    feed.Read(output, 0, n * 2);
                    if (ms >= 30000 && ms < 60000) { minLevel = Math.Min(minLevel, feed.LevelMs); maxLevel = Math.Max(maxLevel, feed.LevelMs); }
                    if (ms >= 40000 && ms < 41000)
                        for (int f = 0; f < n; f++)
                        {
                            float v = output[f * 2]; peak = Math.Max(peak, Math.Abs(v));
                            if (previous < 0 && v >= 0) crossings++; previous = v;
                        }
                    if (ms == 59975) refillsBeforeGap = feed.Refills;
                }
                if (ms == 5000) Check(feed.Refills <= 1, "La cola se vació durante el arranque.");
            }
            Check(minLevel >= ElasticFeed.TargetMs - 4 && maxLevel <= ElasticFeed.TargetMs + 4, "La demora de la cola no se mantuvo fija con relojes distintos: " + minLevel + "–" + maxLevel + " ms.");
            Check(refillsBeforeGap <= 1 && feed.Resyncs == 0, "La cola tuvo cortes con un flujo continuo.");
            Check(Math.Abs(crossings - 1000) <= 6, "La conversión de frecuencia alteró el tono: " + crossings + " ciclos.");
            Check(peak > .48f && peak < .52f, "La conversión de frecuencia alteró el volumen.");
            Check(feed.Refills > refillsBeforeGap, "Una pausa del origen no reinició el llenado.");
            Check(Math.Abs(feed.LevelMs - ElasticFeed.TargetMs) <= 4, "Tras una pausa la cola no volvió a la misma demora.");
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "validacion.txt"), "\r\nOK: cola de demora fija (" + minLevel + "–" + maxLevel + " ms con 0,1 % de deriva), 48→44,1 kHz sin cambiar tono ni volumen, y la misma demora tras una pausa.");
        }
    }
}
