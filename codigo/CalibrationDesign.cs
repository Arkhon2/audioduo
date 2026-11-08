using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.CoreAudioApi;

namespace AudioDuo
{
    sealed partial class MainForm
    {
        Form calibrationWindow;
        CancellationTokenSource calibrationCancellation;
        bool calibrating, exitAfterCalibration;
        internal static DeviceItem ChooseCalibrationBridge(DeviceItem[] available, DeviceItem[] speakers, DeviceItem current, bool bridgeMode)
        {
            var free = available.Where(d => !speakers.Any(s => s.Id == d.Id)).ToArray();
            if (bridgeMode && current != null && free.Any(d => d.Id == current.Id)) return free.First(d => d.Id == current.Id);
            return free.FirstOrDefault(d => d.Name.StartsWith("CABLE Input (", StringComparison.OrdinalIgnoreCase))
                ?? free.FirstOrDefault(d => d.Name.IndexOf("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? free.FirstOrDefault(d => d.Name.IndexOf("High Definition Audio", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        void ShowCalibration()
        {
            ResetListening();
            if (calibrationWindow != null) { calibrationWindow.Show(); calibrationWindow.Activate(); return; }
            var window = new Form { Text = "AudioDúo · Calibración con micrófono", Icon = Icon, ClientSize = new Size(940, 720), MinimumSize = new Size(956, 760), BackColor = Palette.Background, ForeColor = Palette.Text, Font = Font, StartPosition = FormStartPosition.CenterParent, AutoScaleMode = AutoScaleMode.Dpi };
            calibrationWindow = window;
            TextAt(window, "Medir el sonido en tu asiento", 24, 18, 885, 39, 22, Palette.Text, true);
            TextAt(window, "Pausa música/videos. Coloca el micrófono donde escuchas y evita moverlo.\nUn tono de preparación y tres de medida por parlante. El micrófono solo se usa en memoria.", 24, 68, 885, 48, 10, Palette.Muted, false);
            TextAt(window, "Micrófono de medición", 24, 127, 885, 23, 10, Palette.Text, true);
            var input = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Palette.Card, ForeColor = Palette.Text, FlatStyle = FlatStyle.Flat, AccessibleName = "Micrófono de medición" };
            input.SetBounds(24, 156, 885, 30); window.Controls.Add(input);
            foreach (var device in devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            { input.Items.Add(new DeviceItem { Id = device.ID, Name = device.FriendlyName }); device.Dispose(); }
            if (input.Items.Count > 0) input.SelectedIndex = 0;
            for (int i = 0; i < input.Items.Count; i++) if (((DeviceItem)input.Items[i]).Name.IndexOf("High Definition", StringComparison.OrdinalIgnoreCase) >= 0) input.SelectedIndex = i;
            TextAt(window, "Marca solo los parlantes que realmente están conectados", 24, 202, 885, 24, 10, Palette.Text, true);
            var outputs = new CheckedListBox { BackColor = Palette.Card, ForeColor = Palette.Text, BorderStyle = BorderStyle.FixedSingle, CheckOnClick = true, AccessibleName = "Parlantes que se medirán" };
            outputs.SetBounds(24, 234, 885, 90); window.Controls.Add(outputs);
            var selectedSource = source.SelectedItem as DeviceItem;
            var selectedCopy = target.SelectedItem as DeviceItem;
            foreach (var device in AvailableOutputs())
            {
                bool included = (!usingBridge && selectedSource != null && device.Id == selectedSource.Id) || (usingBridge && selectedCopy != null && device.Id == selectedCopy.Id) || extraSpeakers.Any(p => p.Enabled && p.Id == device.Id);
                // The analog output may now be headphones for the microphone, not the disconnected speakers.
                outputs.Items.Add(device, included);
            }
            var begin = ActionAt(window, "Iniciar medición", 24, 341, 190, 39, true);
            var cancel = ActionAt(window, "Cancelar", 228, 341, 130, 39, false); cancel.Enabled = false;
            var progress = TextAt(window, "Listo. Esta prueba interrumpe temporalmente la distribución normal.", 24, 392, 885, 41, 10, Palette.Muted, false);
            var report = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Palette.Card, ForeColor = Palette.Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10) };
            report.SetBounds(24, 444, 885, 170); report.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom; window.Controls.Add(report);
            var apply = ActionAt(window, "Usar esta calibración", 24, 632, 220, 39, true); apply.Enabled = false; apply.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            var sound = ActionAt(window, "Sonido de Windows", 259, 632, 195, 39, false); sound.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; sound.Click += delegate { System.Diagnostics.Process.Start("ms-settings:sound"); };
            TextAt(window, "No se cambian los retrasos hasta pulsar Usar esta calibración. La precisión depende del ruido y del micrófono.", 24, 683, 885, 25, 8, Palette.Muted, false).Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            AcousticPlan plan = null; bool pendingClose = false; int liveSession = -1;
            begin.Click += async delegate
            {
                var microphone = input.SelectedItem as DeviceItem;
                var speakers = outputs.CheckedItems.Cast<DeviceItem>().ToArray();
                if (microphone == null || speakers.Length < 2) { progress.Text = "Selecciona un micrófono y al menos dos parlantes."; return; }
                var liveRoutes = GetLiveCalibrationRoutes(speakers);
                if (liveRoutes == null) StopAudio();
                liveSession = liveRoutes == null ? -1 : generation;
                calibrating = true; calibrationCancellation = new CancellationTokenSource();
                begin.Enabled = input.Enabled = outputs.Enabled = apply.Enabled = false; cancel.Enabled = true; report.Text = "";
                try
                {
                    // Direct mode stays direct: one speaker is the Windows output (the browser then delays the video
                    // for it) and the others receive copies. Only bridge mode uses the virtual cable.
                    var bridge = usingBridge ? ChooseCalibrationBridge(AvailableOutputs(), speakers, source.SelectedItem as DeviceItem, true) : null;
                    var token = calibrationCancellation.Token;
                    Action<string> update = message => { if (!window.IsDisposed && window.IsHandleCreated) window.BeginInvoke((Action)delegate { if (!window.IsDisposed) progress.Text = message; }); };
                    plan = liveRoutes == null
                        ? await Task.Run(() => AcousticCalibrator.Run(microphone.Id, speakers, update, token, bridge))
                        : await Task.Run(() => LiveAcousticCalibration.Run(microphone.Id, bridge, liveRoutes, update, token));
                    if (liveSession >= 0 && (!running || generation != liveSession)) throw new InvalidOperationException("La conexión cambió durante la medición. Conecta y vuelve a medir.");
                    if (liveSession >= 0) ShowAcousticMeasurement(plan);
                    var text = new StringBuilder(); double fastest = plan.Speakers.Min(p => p.PhysicalMs);
                    foreach (var item in plan.Speakers)
                    {
                        text.AppendLine(item.Name);
                        text.AppendLine("  Demora propia del parlante vs. el más rápido: " + (item.PhysicalMs - fastest).ToString("0.0") + " ms · variación " + item.SpreadMs.ToString("0.0") + " ms");
                        if (item.HeardMs > 0) text.AppendLine("  Sonido medido con los ajustes actuales: " + item.HeardMs.ToString("0") + " ms después del envío");
                        text.AppendLine(item.Reference && !plan.UsesBridge ? "  PRINCIPAL RECOMENDADO (el más lento)" : "  Retraso propuesto: " + item.DelayMs + " ms · ruta digital " + item.PipelineMs.ToString("0.0") + " ms");
                        text.AppendLine();
                    }
                    text.AppendLine(plan.Explanation);
                    if (plan.UsesBridge && plan.TotalMs > 0)
                        text.AppendLine("Con esta calibración, los tres parlantes sonarán unos " + plan.TotalMs.ToString("0") + " ms después de que el video envía el audio. AudioDúo no puede adelantar la imagen; para videos, usa un reproductor que permita retrasar la imagen esa cantidad.");
                    report.Text = text.ToString(); apply.Enabled = plan.CanApply;
                    progress.Text = plan.CanApply ? "Medición terminada. Revisa los resultados antes de usarlos." : "Medición terminada; esta ruta no permite aplicar una solución segura.";
                }
                catch (OperationCanceledException) { progress.Text = "Medición cancelada. Los ajustes anteriores se conservan."; }
                catch (Exception ex) { progress.Text = "No se aplicaron cambios: " + ex.Message; report.Text = ex.Message + "\r\n\r\nComprueba el micrófono por cable, los volúmenes y el silencio de la habitación. Puedes repetir la medición."; }
                finally
                {
                    calibrating = false; calibrationCancellation.Dispose(); calibrationCancellation = null;
                    begin.Enabled = input.Enabled = outputs.Enabled = true; cancel.Enabled = false;
                    if (pendingClose) window.Close();
                    if (exitAfterCalibration) RequestExit();
                }
            };
            cancel.Click += delegate { if (calibrationCancellation != null) calibrationCancellation.Cancel(); };
            apply.Click += delegate
            {
                if (plan == null || !plan.CanApply) return;
                if (liveSession >= 0)
                {
                    if (!running || generation != liveSession) { progress.Text = "La conexión cambió. Repite la medición antes de aplicar."; apply.Enabled = false; return; }
                    ApplyLiveAcousticPlan(plan); apply.Enabled = false;
                    progress.Text = "Ajustes aplicados sin reconectar. Ya puedes reanudar la música."; return;
                }
                ApplyAcousticPlan(plan); apply.Enabled = false;
                string originName = plan.UsesBridge ? plan.BridgeName : plan.Speakers.First(p => p.Reference).Name;
                progress.Text = "Ajustes guardados. Elige «" + originName + "» como salida en Sonido de Windows y pulsa Conectar audio.";
            };
            window.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (calibrating) { e.Cancel = true; pendingClose = true; calibrationCancellation.Cancel(); } };
            window.FormClosed += delegate { calibrationWindow = null; };
            window.Show(this);
        }
        void ApplyAcousticPlan(AcousticPlan plan)
        {
            SaveSettings();
            if (File.Exists(settings)) File.Copy(settings, settings + ".antes-calibrar", true);
            if (File.Exists(outputsFile)) File.Copy(outputsFile, outputsFile + ".antes-calibrar", true);
            var old = new Dictionary<string, SpeakerProfile>();
            var a = source.SelectedItem as DeviceItem; var b = target.SelectedItem as DeviceItem;
            if (a != null) old[a.Id] = new SpeakerProfile { Id = a.Id, Name = a.Name, Volume = sourceVolume.Value, Delay = 0 };
            if (b != null) old[b.Id] = new SpeakerProfile { Id = b.Id, Name = b.Name, Volume = targetVolume.Value, Delay = (int)delay.Value, Route = mainRoute.SelectedIndex };
            foreach (var profile in extraSpeakers) old[profile.Id] = profile;
            StopAudio();
            string originId = plan.UsesBridge ? plan.BridgeId : plan.Speakers.First(p => p.Reference).Id;
            string originName = plan.UsesBridge ? plan.BridgeName : plan.Speakers.First(p => p.Reference).Name;
            var copy = plan.Speakers.FirstOrDefault(p => (plan.UsesBridge || !p.Reference) && b != null && p.Id == b.Id) ?? plan.Speakers.First(p => plan.UsesBridge || !p.Reference);
            extraSpeakers.Clear();
            foreach (var item in plan.Speakers.Where(p => (plan.UsesBridge || !p.Reference) && p.Id != copy.Id))
            {
                SpeakerProfile profile; if (!old.TryGetValue(item.Id, out profile)) profile = new SpeakerProfile { Id = item.Id, Name = item.Name };
                profile.Enabled = true; profile.Delay = item.DelayMs; profile.Route = 0; extraSpeakers.Add(profile);
            }
            foreach (var profile in old.Values.Where(p => p.Id != originId && !plan.Speakers.Any(s => s.Id == p.Id)))
            { profile.Enabled = false; profile.State = "Fuera de la calibración · ajuste conservado"; extraSpeakers.Add(profile); }
            LoadDevices(); SelectId(source, originId); SelectId(target, copy.Id); bridgeToggle.Checked = plan.UsesBridge;
            mainRoute.SelectedIndex = 0; delay.Value = copy.DelayMs; referenceDelay = copy.DelayMs; savedDelay.Text = "Volver a " + referenceDelay + " ms";
            SpeakerProfile oldCopy; if (old.TryGetValue(copy.Id, out oldCopy)) targetVolume.Value = oldCopy.Volume;
            SaveSettings(); RefreshSpeakerOverview();
            status.Text = "Selecciona " + originName + " como salida de Windows para usar la calibración.";
        }
    }
}
