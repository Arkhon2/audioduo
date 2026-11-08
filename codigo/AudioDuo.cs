using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioDuo
{
    sealed class Clicks : ISampleProvider
    {
        long frame;
        public WaveFormat WaveFormat { get; private set; }
        public Clicks() { WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2); }
        public int Read(float[] buffer, int offset, int count)
        {
            int available = (int)Math.Min(count / 2, 48000 * 8 - frame);
            for (int i = 0; i < available; i++, frame++)
            {
                int phase = (int)(frame % 48000);
                float value = phase < 1800 ? (float)(0.18 * Math.Sin(2 * Math.PI * 1000 * phase / 48000) * Math.Exp(-phase / 300.0)) : 0;
                buffer[offset + 2 * i] = buffer[offset + 2 * i + 1] = value;
            }
            return available * 2;
        }
    }

    sealed class DeviceItem
    {
        public string Id, Name;
        public bool Offline;
        public override string ToString() { return Offline ? Name + " · apagado" : Name; }
    }

    sealed partial class MainForm : Form
    {
        readonly MMDeviceEnumerator devices = new MMDeviceEnumerator();
        readonly ComboBox source = new ComboBox(), target = new ComboBox();
        Button refresh, start, stop, test;
        readonly Slider sourceVolume = new Slider(), targetVolume = new Slider();
        readonly NumericUpDown delay = new NumericUpDown();
        readonly Label status = new Label(), sourceLevel = new Label(), targetLevel = new Label(), meter = new Label();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip trayMenu = new ContextMenuStrip();
        readonly string settings = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preferencias.txt");
        WasapiCapture capture;
        WasapiCapture targetCapture;
        MMDevice monitorDevice;
        volatile bool targetViewAvailable;
        WasapiOut output, testOutput;
        MMDevice sourceDevice, targetDevice, testDevice;
        ElasticFeed queue;
        DelayVolume processor;
        bool running, closing, exitRequested, trayHintShown;
        int generation;
        long received;
        internal EventWaitHandle ShowSignal, ExitSignal;

        public MainForm() { BuildDesign(); }
        void HideToTray()
        {
            if (calibrating && calibrationCancellation != null) calibrationCancellation.Cancel();
            if (outputsWindow != null) outputsWindow.Hide();
            SaveSettings(); Hide();
            RefreshChrome();
            if (!trayHintShown)
            {
                tray.ShowBalloonTip(4000, "AudioDúo sigue abierto", running ? "Tus parlantes siguen sonando. Doble clic aquí para volver. Para cerrar, usa Salir y detener audio." : "Doble clic aquí para volver. Para cerrar por completo, usa Salir y detener audio.", ToolTipIcon.Info);
                trayHintShown = true;
            }
        }
        void RestoreWindow()
        {
            Show(); WindowState = FormWindowState.Normal; Activate();
        }
        internal void RequestExit()
        {
            if (calibrating) { exitAfterCalibration = true; calibrationCancellation.Cancel(); return; }
            exitRequested = true; Close();
        }
        internal void ConnectForLaunch() { StartAudio(); }
        internal void VerifyTrayBehavior()
        {
            bool wasMute = false; string sourceId = ((DeviceItem)source.SelectedItem).Id;
            using (var original = devices.GetDevice(sourceId)) wasMute = original.AudioEndpointVolume.Mute;
            StartAudio();
            if (!running) throw new Exception("No se pudo iniciar audio: " + status.Text);
            var activeProcessor = processor;
            if (usingBridge && !sourceDevice.AudioEndpointVolume.Mute) throw new Exception("El puente no quedó silenciado.");
            Close();
            if (Visible || closing || !running || !tray.Visible || processor != activeProcessor)
                throw new Exception("La X no conservó el audio en segundo plano.");
            RestoreWindow();
            if (!Visible || !running || processor != activeProcessor) throw new Exception("No se pudo restaurar sin interrumpir el audio.");
            WindowState = FormWindowState.Minimized;
            if (Visible || !running || !tray.Visible) throw new Exception("Minimizar no conservó el audio.");
            RequestExit();
            if (!closing || running || capture != null || output != null) throw new Exception("Salir no liberó el audio.");
            using (var checkDevices = new MMDeviceEnumerator())
            using (var restored = checkDevices.GetDevice(sourceId))
                if (usingBridge && restored.AudioEndpointVolume.Mute != wasMute) throw new Exception("Salir no restauró el silencio original del puente.");
        }
        internal void VerifyLiveStudio(Action<Exception> done)
        {
            int originalDelay = (int)delay.Value, originalRoute = mainRoute.SelectedIndex;
            StartAudio(); if (!running) { done(new Exception(status.Text)); return; }
            var originalCapture = capture; var originalOutput = output; var originalProcessor = processor;
            var originalBranches = extraSpeakers.Select(p => p.Branch).ToArray();
            var originalExtraDelays = extraSpeakers.Select(p => p.Delay).ToArray();
            StartTest(); int stage = 0;
            var checkTimer = new System.Windows.Forms.Timer { Interval = 700 };
            checkTimer.Tick += delegate
            {
                try
                {
                    if (!running || originalCapture != capture || originalOutput != output || originalProcessor != processor) throw new Exception("El ajuste reinició la conexión.");
                    for (int i = 0; i < extraSpeakers.Count; i++) if (extraSpeakers[i].Branch != originalBranches[i]) throw new Exception("El ajuste reinició una salida adicional.");
                    if (stage == 0)
                    {
                        var mainId = ((DeviceItem)target.SelectedItem).Id;
                        manualPlusButtons[mainId].PerformClick();
                        if (processor.RequestedDelay != Math.Min(1500, originalDelay + 10)) throw new Exception("El botón de la tarjeta principal no ajustó el audio.");
                        foreach (var p in extraSpeakers.Where(p => p.Enabled && manualPlusButtons.ContainsKey(p.Id)))
                        {
                            int previousDelay = p.Delay; manualPlusButtons[p.Id].PerformClick();
                            if (p.Branch.Delay.RequestedDelay != Math.Min(1500, previousDelay + 10)) throw new Exception("El botón de la tarjeta adicional no ajustó el audio.");
                        }
                        mutedSpeakers.Add(mainId); ApplyListeningGains();
                        if (processor.Gain != 0) throw new Exception("Silenciar no aisló la salida principal.");
                        soloSpeaker = mainId; mutedSpeakers.Clear(); ApplyListeningGains();
                        if (activeExtras.Any(branch => branch.Delay.Gain != 0)) throw new Exception("Solo no aisló las otras salidas.");
                        ResetListening();
                        if (usingBridge && waves.TraceCount != 1 + extraSpeakers.Count(p => !IsUtilityOutput(p.Name))) throw new Exception("El monitor incluye salidas virtuales como parlantes.");
                        delay.Value = Math.Min(1500, originalDelay + 37); mainRoute.SelectedIndex = 1;
                        foreach (var p in extraSpeakers) { p.Delay = Math.Min(1500, p.Delay + 47); SpeakerChanged(p); }
                    }
                    else if (stage == 1)
                    {
                        delay.Value = originalDelay; mainRoute.SelectedIndex = originalRoute;
                        for (int i = 0; i < extraSpeakers.Count; i++) { extraSpeakers[i].Delay = originalExtraDelays[i]; SpeakerChanged(extraSpeakers[i]); }
                    }
                    else
                    {
                        if (!sourceHistory.Snapshot(WaveHistory.Now, 2000).Any(v => v > .00001) || !targetHistory.Snapshot(WaveHistory.Now, 2000).Any(v => v > .00001)) throw new Exception("No hay señal real en ambos monitores.");
                        foreach (var p in extraSpeakers.Where(p => p.Enabled))
                            if (p.Branch == null || !p.History.Snapshot(WaveHistory.Now, 2000).Any(v => v > .00001)) throw new Exception("No hay señal real en salida adicional: " + p.Name + " · " + p.State);
                        waves.Freeze(); if (!waves.Frozen || Math.Abs(waves.CursorDelta - 600) > .1) throw new Exception("No funciona la vista congelada o sus cursores."); waves.Resume();
                        checkTimer.Stop(); checkTimer.Dispose(); StopTest(); SaveSettings(); done(null); return;
                    }
                    stage++;
                }
                catch (Exception ex)
                {
                    checkTimer.Stop(); checkTimer.Dispose(); delay.Value = originalDelay; mainRoute.SelectedIndex = originalRoute;
                    for (int i = 0; i < extraSpeakers.Count; i++) { extraSpeakers[i].Delay = originalExtraDelays[i]; SpeakerChanged(extraSpeakers[i]); }
                    SaveSettings(); done(ex);
                }
            };
            checkTimer.Start();
        }
        void LoadDevices()
        {
            try
            {
                var oldSource = source.SelectedItem as DeviceItem; var oldTarget = target.SelectedItem as DeviceItem;
                reloadingDevices = true;
                source.Items.Clear(); target.Items.Clear();
                foreach (MMDevice device in devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    source.Items.Add(new DeviceItem { Id = device.ID, Name = device.FriendlyName });
                    target.Items.Add(new DeviceItem { Id = device.ID, Name = device.FriendlyName }); device.Dispose();
                }
                // Saved speakers stay selected while switched off, so their settings survive until they return.
                if (oldSource != null || oldTarget != null)
                {
                    if (oldSource != null) SelectOrKeep(source, oldSource.Id, oldSource.Name);
                    if (oldTarget != null) SelectOrKeep(target, oldTarget.Id, oldTarget.Name, source.SelectedItem is DeviceItem ? ((DeviceItem)source.SelectedItem).Id : null);
                }
                else if (source.Items.Count > 0)
                {
                    source.SelectedIndex = 0;
                    for (int i = 0; i < source.Items.Count; i++)
                        if (((DeviceItem)source.Items[i]).Name.IndexOf("Bluetooth", StringComparison.OrdinalIgnoreCase) >= 0) source.SelectedIndex = i;
                    target.SelectedIndex = source.Items.Count > 1 ? (source.SelectedIndex == 0 ? 1 : 0) : 0;
                    using (var current = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) SelectId(source, current.ID);
                    for (int i = 0; i < target.Items.Count; i++)
                    {
                        var candidate = (DeviceItem)target.Items[i];
                        if (candidate.Id != ((DeviceItem)source.SelectedItem).Id && candidate.Name.IndexOf("High Definition Audio", StringComparison.OrdinalIgnoreCase) >= 0) target.SelectedIndex = i;
                    }
                }
            }
            catch (Exception ex) { Fail(ex); }
            finally { reloadingDevices = false; }
        }
        static void SelectId(ComboBox box, string id)
        {
            for (int i = 0; i < box.Items.Count; i++) if (((DeviceItem)box.Items[i]).Id == id) box.SelectedIndex = i;
        }
        void StartAudio()
        {
            if (calibrating) return;
            try
            {
                var a = source.SelectedItem as DeviceItem; var b = target.SelectedItem as DeviceItem;
                if (a == null || b == null || a.Id == b.Id) throw new InvalidOperationException("En Ajustes, elige un parlante principal y una copia diferentes.");
                // A saved speaker that is switched off is waited for, not reported as an error.
                if (!IsActive(a.Id) || !IsActive(b.Id)) { StopAudio(); waitingForDevices = true; ExplainWaiting(); RefreshChrome(); RefreshSpeakerOverview(); return; }
                bool isDefault;
                using (var current = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) isDefault = current.ID == a.Id;
                if (!isDefault && DefaultOutput.Set(a.Id))
                    using (var current = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) isDefault = current.ID == a.Id;
                if (!isDefault) throw new InvalidOperationException("Windows no aceptó «" + SpeakerName(a.Name) + "» como salida. Elígelo en Sonido de Windows y pulsa Conectar.");
                StopAudio(); int session = ++generation;
                sourceDevice = devices.GetDevice(a.Id); targetDevice = devices.GetDevice(b.Id);
                sourceOwnLevel = -1; sourceSetDb = double.NaN;
                SilenceBridge();
                sourceVolume.Value = (int)Math.Round(sourceDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
                sourceLevel.Text = "Volumen principal: " + sourceVolume.Value + "%";
                if (usingBridge) sourceLevel.Text = "Puente silenciado · señal digital activa";
                TimerResolution.Begin();
                capture = new LowLatencyLoopback(sourceDevice);
                sourceHistory.Clear(); targetHistory.Clear(); waves.Resume(); freeze.Text = "Congelar";
                queue = new ElasticFeed(capture.WaveFormat, ElasticFeed.DeviceRate(targetDevice, capture.WaveFormat.SampleRate));
                mainRouter = new ChannelRouter(queue) { Route = mainRoute.SelectedIndex };
                processor = new DelayVolume(mainRouter); processor.Gain = targetVolume.Value / 100f; processor.SetDelay((int)delay.Value);
                output = new WasapiOut(targetDevice, AudioClientShareMode.Shared, false, ElasticFeed.RenderMs);
                output.Init(new SampleToWaveProvider(processor));
                capture.DataAvailable += delegate(object sender, WaveInEventArgs e)
                {
                    // Stopped capture is joined before queue is released.
                    queue.Write(e.Buffer, e.BytesRecorded); Interlocked.Add(ref received, e.BytesRecorded);
                    sourceHistory.Add(e.Buffer, e.BytesRecorded, capture.WaveFormat);
                    foreach (var branch in activeExtras) branch.Feed(e.Buffer, e.BytesRecorded);
                };
                capture.RecordingStopped += delegate(object sender, StoppedEventArgs e) { if (e.Exception != null && session == generation) Fail(e.Exception); };
                output.PlaybackStopped += delegate(object sender, StoppedEventArgs e) { if (e.Exception != null && session == generation) Fail(e.Exception); };
                received = 0; running = true;
                output.Play(); capture.StartRecording();
                StartTargetView(MonitorIdFor(b.Id, b.Name), session);
                StartExtras(); ApplyListeningGains(); SyncSourceOwnLevel();
                source.Enabled = target.Enabled = refresh.Enabled = start.Enabled = false; stop.Enabled = test.Enabled = true;
                sourceVolume.Enabled = !usingBridge; bridgeToggle.Enabled = false;
                waitingForDevices = false; lastFailure = false;
                status.Text = "Sonando en todos tus parlantes. Si notas desfase, ajusta el retraso de la copia o usa Calibrar.";
                SaveSettings(); RefreshChrome(); RefreshSpeakerOverview();
            }
            catch (Exception ex) { Fail(ex); }
        }
        void StartTargetView(string id, int session)
        {
            try
            {
                monitorDevice = devices.GetDevice(id); targetCapture = new LowLatencyLoopback(monitorDevice);
                var observer = targetCapture;
                observer.DataAvailable += delegate(object sender, WaveInEventArgs e) { targetHistory.Add(e.Buffer, e.BytesRecorded, observer.WaveFormat); };
                observer.RecordingStopped += delegate(object sender, StoppedEventArgs e) { if (session == generation && e.Exception != null) targetViewAvailable = false; };
                targetViewAvailable = true; observer.StartRecording();
            }
            catch (Exception) { targetViewAvailable = false; }
        }
        void StartTest()
        {
            try
            {
                StopTest(); testDevice = devices.GetDevice(((DeviceItem)source.SelectedItem).Id);
                testOutput = new WasapiOut(testDevice, AudioClientShareMode.Shared, false, 50);
                testOutput.Init(new SampleToWaveProvider(new Clicks())); testOutput.Play();
                status.Text = "Ocho golpes suaves. Ajusta cada copia hasta oír un solo golpe.";
            }
            catch (Exception ex) { Fail(ex); }
        }
        void UpdateStatus()
        {
            if (ExitSignal != null && ExitSignal.WaitOne(0)) { RequestExit(); return; }
            if (ShowSignal != null && ShowSignal.WaitOne(0)) RestoreWindow();
            if (!calibrating && ++overviewTick % 2 == 0) RefreshSpeakerOverview();
            RefreshChrome();
            // Fallback for missed device notifications: retry the saved setup every few seconds.
            if (!running)
            {
                // A passing audio error (driver reset, format change) gets up to three automatic retries.
                if (lastFailure && failureRetries < 3 && autoConnect && !userStopped && overviewTick % 5 == 0) { failureRetries++; autoBlocked = false; TryAutoConnect(); }
                else if (overviewTick % 4 == 0) TryAutoConnect();
                return;
            }
            try
            {
                if (sourceDevice.State != DeviceState.Active || targetDevice.State != DeviceState.Active) { WaitForSpeakers(); return; }
                meter.Text = Interlocked.Read(ref received) == 0 ? "Esperando señal" : "● Señal activa · cola " + queue.LevelMs + " ms";
                if (!targetViewAvailable) meter.Text = "Vista del cable no disponible";
                if ((!usingBridge && sourceDevice.AudioEndpointVolume.Mute) || targetDevice.AudioEndpointVolume.Mute) meter.Text = "Silenciado en Windows";
                // Windows sometimes moves the default output (plugging a device, a game, a call). Offer one click back.
                using (var current = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                {
                    bool moved = current.ID != sourceDevice.ID;
                    if (moved && !defaultMovedAsked && !noticePrompt.Visible)
                    {
                        defaultMovedAsked = true; string mainId = sourceDevice.ID, mainName = SpeakerName(sourceDevice.FriendlyName);
                        AskInNotice("Windows cambió la salida a «" + SpeakerName(current.FriendlyName) + "». Los videos ya no suenan por «" + mainName + "».", "Volver a " + mainName,
                            delegate { if (DefaultOutput.Set(mainId)) status.Text = "Salida restablecida en «" + mainName + "»."; else status.Text = "Windows no aceptó el cambio; elige «" + mainName + "» en Sonido de Windows."; },
                            null, "Dejarla así");
                    }
                    if (!moved) defaultMovedAsked = false;
                }
                if (usingBridge)
                {
                    sourceLevel.Text = BridgeStateText();
                    if (!sourceDevice.AudioEndpointVolume.Mute) status.Text = "El puente está audible: esa salida suena sin el retraso de las copias.";
                }
                HealthExtras();
            }
            catch (Exception ex) { Fail(ex); }
        }
        void Fail(Exception ex)
        {
            if (closing || IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((Action)(() => Fail(ex))); return; }
            // A speaker switched off mid-playback also ends here: that is a wait, not a failure.
            var a = source.SelectedItem as DeviceItem; var b = target.SelectedItem as DeviceItem;
            if (running && a != null && b != null && (!IsActive(a.Id) || !IsActive(b.Id))) { WaitForSpeakers(); return; }
            StopAudio(); lastFailure = true; autoBlocked = true;
            status.Text = ex.Message; meter.Text = "No se pudo mantener la conexión. Puedes volver a intentarlo.";
            RefreshChrome(); RefreshSpeakerOverview();
            if (!Visible) tray.ShowBalloonTip(4000, "AudioDúo · audio detenido", ex.Message, ToolTipIcon.Warning);
        }
        void WaitForSpeakers()
        {
            StopAudio(); waitingForDevices = true; ExplainWaiting(); RefreshChrome(); RefreshSpeakerOverview();
            if (!Visible) tray.ShowBalloonTip(3000, "AudioDúo · esperando parlante", status.Text, ToolTipIcon.Info);
        }
        void StopTest()
        {
            if (testOutput != null) { testOutput.Dispose(); testOutput = null; }
            if (testDevice != null) { testDevice.Dispose(); testDevice = null; }
        }
        void StopAudio()
        {
            ++generation; running = false;
            MarkAcousticStale();
            ResetListening();
            StopTest();
            if (capture != null) { capture.Dispose(); capture = null; }
            StopExtras();
            if (targetCapture != null) { targetCapture.Dispose(); targetCapture = null; }
            if (monitorDevice != null) { monitorDevice.Dispose(); monitorDevice = null; }
            targetViewAvailable = false;
            if (output != null) { output.Dispose(); output = null; }
            RestoreBridge();
            TimerResolution.End();
            if (sourceDevice != null) { sourceDevice.Dispose(); sourceDevice = null; }
            if (targetDevice != null) { targetDevice.Dispose(); targetDevice = null; }
            processor = null; mainRouter = null; queue = null;
            source.Enabled = target.Enabled = refresh.Enabled = start.Enabled = true; stop.Enabled = test.Enabled = false; meter.Text = "";
            sourceVolume.Enabled = false;
            if (bridgeToggle != null) bridgeToggle.Enabled = true;
            RefreshChrome();
        }
        void SaveSettings()
        {
            if (PreviewOnly) return;
            try
            {
                var a = source.SelectedItem as DeviceItem; var b = target.SelectedItem as DeviceItem;
                if (a != null && b != null)
                {
                    string pending = settings + ".tmp";
                    File.WriteAllLines(pending, new[] { a.Id, b.Id, delay.Value.ToString(), targetVolume.Value.ToString(), referenceDelay.ToString(), mainRoute.SelectedIndex.ToString(), usingBridge.ToString(), masterLevel.ToString(), autoConnect.ToString(), a.Name, b.Name, autoAddSpeakers.ToString() }, Encoding.UTF8);
                    if (File.Exists(settings)) File.Replace(pending, settings, null); else File.Move(pending, settings);
                }
                SaveExtraSpeakers();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        void LoadSettings()
        {
            loadingSettings = true;
            try
            {
                if (!File.Exists(settings)) return;
                string[] lines = File.ReadAllLines(settings); if (lines.Length < 4) return;
                SelectOrKeep(source, lines[0], lines.Length > 9 ? lines[9] : null); SelectOrKeep(target, lines[1], lines.Length > 10 ? lines[10] : null, source.SelectedItem is DeviceItem ? ((DeviceItem)source.SelectedItem).Id : null); int v;
                bool auto; if (lines.Length > 8 && bool.TryParse(lines[8], out auto)) autoConnect = auto;
                if (lines.Length > 11 && bool.TryParse(lines[11], out auto)) autoAddSpeakers = auto;
                if (int.TryParse(lines[2], out v)) delay.Value = Math.Max(0, Math.Min(1500, v));
                referenceDelay = (int)delay.Value;
                if (lines.Length > 4 && int.TryParse(lines[4], out v)) referenceDelay = Math.Max(0, Math.Min(1500, v));
                savedDelay.Text = "Volver a " + referenceDelay + " ms";
                if (lines.Length > 5 && int.TryParse(lines[5], out v)) mainRoute.SelectedIndex = Math.Max(0, Math.Min(4, v));
                bool bridge; if (lines.Length > 6 && bool.TryParse(lines[6], out bridge)) bridgeToggle.Checked = bridge;
                if (lines.Length > 7 && int.TryParse(lines[7], out v)) masterVolume.Value = Math.Max(0, Math.Min(100, v));
                if (int.TryParse(lines[3], out v)) targetVolume.Value = Math.Max(0, Math.Min(100, v));
                targetLevel.Text = "Volumen de la copia · " + targetVolume.Value + "%";
                if (autoConnectToggle != null) autoConnectToggle.Checked = autoConnect;
                if (autoAddToggle != null) autoAddToggle.Checked = autoAddSpeakers;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally { loadingSettings = false; }
        }
    }

    static class Program
    {
        sealed class Impulse : ISampleProvider
        {
            int index;
            public WaveFormat WaveFormat { get { return WaveFormat.CreateIeeeFloatWaveFormat(1000, 2); } }
            public int Read(float[] b, int o, int n)
            {
                for (int i = 0; i < n; i++, index++) b[o + i] = index < 2 ? 1f : 0f;
                return n;
            }
        }
        static void SelfTest()
        {
            var p = new DelayVolume(new Impulse()); p.Gain = 0.5f; p.SetDelay(10);
            var b = new float[60]; p.Read(b, 0, 12); p.Read(b, 12, 48);
            for (int i = 0; i < b.Length; i++)
                if (b[i] != (i == 20 || i == 21 ? 0.5f : 0f)) throw new Exception("Fallo: retraso/volumen/estéreo.");
            p.SetDelay(0); p.Gain = 0; p.Read(b, 0, 60);
            if (b.Any(v => v != 0)) throw new Exception("Fallo: silencio.");
            var clicks = new Clicks(); var block = new float[960]; int total = 0, n;
            while ((n = clicks.Read(block, 0, block.Length)) > 0) total += n;
            if (total != 48000 * 2 * 8 || clicks.Read(block, 0, block.Length) != 0) throw new Exception("Fallo: duración de prueba.");
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "validacion.txt"), "OK: retraso exacto, canales estéreo, volumen, silencio y prueba de ocho segundos.");
        }
        static void AudioSmoke(string destinationId)
        {
            using (var e = new MMDeviceEnumerator())
            using (var a = e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            using (var b = e.GetDevice(destinationId))
            using (var c = new WasapiLoopbackCapture(a))
            using (var observe = new WasapiLoopbackCapture(b))
            using (var render = new WasapiOut(b, AudioClientShareMode.Shared, false, 50))
            using (var pulseDevice = e.GetDevice(a.ID))
            using (var pulse = new WasapiOut(pulseDevice, AudioClientShareMode.Shared, false, 50))
            {
                if (a.ID == b.ID) throw new Exception("Las salidas deben ser diferentes.");
                long captured = 0, audible = 0; Exception failure = null;
                var q = new BufferedWaveProvider(c.WaveFormat) { ReadFully = true, DiscardOnBufferOverflow = true };
                var dv = new DelayVolume(q.ToSampleProvider()); dv.SetDelay(150); dv.Gain = 0.5f;
                c.DataAvailable += delegate(object s, WaveInEventArgs ev) { q.AddSamples(ev.Buffer, 0, ev.BytesRecorded); Interlocked.Add(ref captured, ev.BytesRecorded); };
                observe.DataAvailable += delegate(object s, WaveInEventArgs ev) { for (int i = 0; i < ev.BytesRecorded; i++) if (ev.Buffer[i] != 0) { Interlocked.Increment(ref audible); break; } };
                c.RecordingStopped += delegate(object s, StoppedEventArgs ev) { if (ev.Exception != null) failure = ev.Exception; };
                render.PlaybackStopped += delegate(object s, StoppedEventArgs ev) { if (ev.Exception != null) failure = ev.Exception; };
                render.Init(new SampleToWaveProvider(dv)); pulse.Init(new SampleToWaveProvider(new Clicks()));
                observe.StartRecording(); c.StartRecording(); render.Play(); pulse.Play();
                Thread.Sleep(2600);
                pulse.Stop(); c.StopRecording(); render.Stop(); observe.StopRecording();
                if (failure != null) throw failure;
                if (captured == 0 || audible == 0) throw new Exception("No se detectó audio en las dos salidas.");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prueba-audio.txt"), "OK\r\nPrincipal: " + a.FriendlyName + "\r\nCopia: " + b.FriendlyName + "\r\nBytes capturados: " + captured + "\r\nBloques con señal en salida cableada: " + audible + "\r\nRetraso añadido: 150 ms. La coincidencia acústica se ajusta de oído.");
            }
        }
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--self-test") { SelfTest(); SignalChecks.Run(); AcousticChecks.Run(); return; }
                if (args.Length > 0 && args[0] == "--studio-smoke")
                {
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    Exception failure = null;
                    using (var f = new MainForm())
                    {
                        f.Shown += delegate { f.BeginInvoke((Action)delegate { f.VerifyLiveStudio(delegate(Exception ex) { failure = ex; f.RequestExit(); }); }); };
                        Application.Run(f);
                    }
                    if (failure != null) throw failure;
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prueba-estudio.txt"), "OK: retraso y canales cambiados con la misma captura/salida activas, señal real en ambos monitores y todas las salidas adicionales configuradas, congelación y cursores. Preferencias restauradas."); return;
                }
                if (args.Length > 0 && args[0] == "--tray-smoke")
                {
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    using (var f = new MainForm())
                    {
                        Exception failure = null;
                        f.Shown += delegate { f.BeginInvoke((Action)delegate { try { f.VerifyTrayBehavior(); } catch (Exception ex) { failure = ex; f.RequestExit(); } }); };
                        Application.Run(f);
                        if (failure != null) throw failure;
                    }
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prueba-bandeja.txt"), "OK: X mantiene el motor de audio, restaurar conserva la sesión, minimizar mantiene el audio y Salir libera captura y salida.");
                    return;
                }
                if (args.Length > 1 && args[0] == "--audio-smoke") { AudioSmoke(args[1]); return; }
                if (args.Length > 0 && args[0] == "--preview")
                {
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    // --preview [ancho alto [archivo]]: window image only; no audio, no settings written.
                    using (var f = new MainForm { PreviewOnly = true })
                    {
                        int w, h;
                        if (args.Length > 2 && int.TryParse(args[1], out w) && int.TryParse(args[2], out h)) f.Size = new Size(w, h);
                        f.Show(); for (int i = 0; i < 5; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); } f.Refresh();
                        Form shot = args.Contains("--ajustes") ? f.SettingsForPreview() : f;
                        for (int i = 0; i < 5; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(60); } shot.Refresh();
                        using (var bmp = new Bitmap(shot.Width, shot.Height))
                        {
                            shot.DrawToBitmap(bmp, new Rectangle(0, 0, shot.Width, shot.Height));
                            bmp.Save(args.Length > 3 && !args[3].StartsWith("--") ? args[3] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vista.png"));
                        }
                        f.RequestExit();
                    }
                    return;
                }
            if (args.Length > 0 && args[0] == "--devices")
            {
                using (var e = new MMDeviceEnumerator())
                {
                    var lines = new List<string>();
                    using (var d = e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) lines.Add("Principal de Windows: " + d.FriendlyName);
                    foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                    { lines.Add(d.FriendlyName + " | " + d.ID); d.Dispose(); }
                    File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dispositivos.txt"), lines, Encoding.UTF8);
                }
                return;
            }
            if (args.Contains("--exit-running"))
            {
                try { using (var signal = EventWaitHandle.OpenExisting("Local\\AudioDuo.Exit")) signal.Set(); } catch (WaitHandleCannotBeOpenedException) { }
                return;
            }
            bool firstInstance;
            using (var single = new Mutex(true, "Local\\AudioDuo.Desktop", out firstInstance))
            using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\AudioDuo.Show"))
            using (var exit = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\AudioDuo.Exit"))
            {
                if (!firstInstance) { show.Set(); return; }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (var form = new MainForm())
                {
                    form.ShowSignal = show; form.ExitSignal = exit;
                    if (args.Contains("--connect")) form.Shown += delegate { form.BeginInvoke((Action)form.ConnectForLaunch); };
                    if (args.Contains("--tray")) form.StartInTray();
                    Application.Run(form);
                }
                single.ReleaseMutex();
            }
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.txt"), ex.ToString());
                Environment.ExitCode = 1;
                if (args.Length == 0) MessageBox.Show(ex.Message, "AudioDúo", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
