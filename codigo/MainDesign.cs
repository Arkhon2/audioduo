using System;
using System.Drawing;
using System.Windows.Forms;

namespace AudioDuo
{
    sealed partial class MainForm
    {
        readonly WaveHistory sourceHistory = new WaveHistory(), targetHistory = new WaveHistory();
        readonly Pill badge = new Pill { AlignLeft = true };
        readonly Slider masterVolume = new Slider();
        Label masterPercent;
        CheckBox masterMute;
        int masterLevel = 100;
        readonly System.Windows.Forms.Timer visualTimer = new System.Windows.Forms.Timer(), saveTimer = new System.Windows.Forms.Timer();
        readonly ToolTip tips = new ToolTip { InitialDelay = 400, ReshowDelay = 200, AutoPopDelay = 12000 };
        WaveView waves;
        Button freeze, savedDelay, connectButton, calibrateButton, settingsButton, noticeAction, noticeDismiss;
        ComboBox scale;
        int referenceDelay = 310;
        bool loadingSettings, monitorCollapsed;
        ScrollHost host;
        Panel page, header, footer;
        Surface noticeCard, masterCard, monitorCard;
        Label noticeDot, noticePrompt, speakersTitle, speakersHint, monitorTitle, monitorDescription;
        Button collapseMonitor, removeOffline;

        Label TextAt(Control parent, string text, int x, int y, int width, int height, float size, Color color, bool bold)
        {
            var label = new Label { Text = text, ForeColor = color, BackColor = Color.Transparent, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), UseMnemonic = false };
            label.SetBounds(x, y, width, height); parent.Controls.Add(label); return label;
        }
        Button ActionAt(Control parent, string text, int x, int y, int width, int height, bool primary)
        {
            var button = new Button { Text = text };
            if (primary) Styles.Primary(button); else Styles.Secondary(button);
            button.SetBounds(x, y, width, height); parent.Controls.Add(button); return button;
        }
        void BuildDesign()
        {
            Text = "AudioDúo"; ClientSize = new Size(1120, 860); MinimumSize = new Size(820, 640);
            StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10); BackColor = Palette.Background; ForeColor = Palette.Text;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            host = new ScrollHost { Dock = DockStyle.Fill }; Controls.Add(host);
            page = new Panel { BackColor = Palette.Background, Location = Point.Empty }; host.Controls.Add(page);

            // Header: identity, live state and the main actions.
            header = new Panel { BackColor = Palette.Background }; page.Controls.Add(header);
            var logo = new PictureBox { Image = Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Palette.Background };
            logo.SetBounds(0, 10, 48, 48); header.Controls.Add(logo);
            TextAt(header, "AudioDúo", 60, 2, 170, 40, 21, Palette.Text, true);
            TextAt(header, "Sonido sincronizado en todos tus parlantes", 62, 42, 330, 22, 9.5f, Palette.Muted, false);
            badge.SetBounds(236, 12, 230, 26); header.Controls.Add(badge);
            connectButton = ActionAt(header, "Conectar", 0, 12, 150, 42, true);
            connectButton.Click += delegate { if (running) StopByUser(); else ConnectByUser(); };
            test = ActionAt(header, "Probar sonido", 0, 12, 140, 42, false); test.Enabled = false; test.Click += delegate { StartTest(); };
            calibrateButton = ActionAt(header, "Calibrar", 0, 12, 112, 42, false); calibrateButton.Click += delegate { ShowCalibration(); };
            settingsButton = ActionAt(header, "Ajustes", 0, 12, 104, 42, false); settingsButton.Click += delegate { ShowSettings(); };
            tips.SetToolTip(test, "Reproduce 8 golpes suaves: si los parlantes están sincronizados, oirás un solo golpe cada vez.");
            tips.SetToolTip(calibrateButton, "Mide con un micrófono cuándo llega el sonido de cada parlante y propone los retrasos.");
            tips.SetToolTip(settingsButton, "Elegir parlantes, conexión automática e inicio con Windows.");

            // Notice bar: the current state in plain words, or a question with its buttons.
            noticeCard = new Surface(); page.Controls.Add(noticeCard);
            noticeDot = TextAt(noticeCard, "●", 16, 12, 20, 24, 11, Palette.Muted, false);
            status.ForeColor = Palette.Text; status.Font = new Font("Segoe UI", 10); status.BackColor = Color.Transparent; status.TextAlign = ContentAlignment.MiddleLeft; status.AutoEllipsis = true; status.UseMnemonic = false;
            noticeCard.Controls.Add(status);
            noticePrompt = TextAt(noticeCard, "", 40, 0, 600, 48, 10, Palette.Text, true); noticePrompt.TextAlign = ContentAlignment.MiddleLeft; noticePrompt.Visible = false;
            noticeAction = ActionAt(noticeCard, "", 0, 7, 120, 34, true); noticeAction.Visible = false;
            noticeDismiss = ActionAt(noticeCard, "Ahora no", 0, 7, 100, 34, false); noticeDismiss.Visible = false;
            noticeAction.Click += delegate { var yes = noticeYes; CloseNotice(); if (yes != null) yes(); };
            noticeDismiss.Click += delegate { var no = noticeNo; CloseNotice(); if (no != null) no(); };

            // Master volume.
            masterCard = new Surface(); page.Controls.Add(masterCard);
            TextAt(masterCard, "Volumen general", 22, 18, 260, 28, 13, Palette.Text, true);
            TextAt(masterCard, "Sube o baja todos los parlantes a la vez, sin cambiar el balance entre ellos.", 22, 48, 300, 40, 9, Palette.Muted, false);
            masterVolume.Value = masterLevel; masterVolume.Accent = Palette.Teal; masterVolume.AccessibleName = "Volumen general de todos los parlantes"; masterVolume.Height = 30;
            masterCard.Controls.Add(masterVolume);
            masterPercent = TextAt(masterCard, masterLevel + "%", 0, 26, 80, 36, 18, Palette.Text, true); masterPercent.TextAlign = ContentAlignment.MiddleRight;
            masterMute = Styles.Chip("Silenciar todo", Styles.Amber); masterMute.Size = new Size(124, 36); masterCard.Controls.Add(masterMute);
            masterVolume.Scroll += delegate { SetMasterVolume(masterVolume.Value); };
            masterMute.CheckedChanged += delegate { ApplyListeningGains(); RefreshSpeakerOverview(); RefreshChrome(); };

            // Speakers.
            speakersTitle = TextAt(page, "Tus parlantes", 0, 0, 260, 30, 13, Palette.Text, true);
            speakersHint = TextAt(page, "Más milisegundos = ese parlante suena más tarde. Ajusta de a 10 y afina con 1.", 0, 0, 600, 22, 9, Palette.Muted, false);
            speakersHint.TextAlign = ContentAlignment.MiddleRight;
            removeOffline = ActionAt(page, "Quitar apagados", 0, 0, 190, 32, false); removeOffline.Visible = false;
            removeOffline.Click += delegate { RemoveOfflineSpeakers(); };
            tips.SetToolTip(removeOffline, "Quita de la lista los parlantes adicionales que están apagados o ya no están emparejados.");
            speakerOverview = new FlowLayoutPanel { BackColor = Palette.Background, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty, Padding = Padding.Empty };
            page.Controls.Add(speakerOverview);

            // Live waves / last microphone measurement.
            monitorCard = new Surface(); page.Controls.Add(monitorCard);
            monitorTitle = TextAt(monitorCard, "Ondas en vivo", 22, 16, 300, 28, 13, Palette.Text, true);
            monitorDescription = TextAt(monitorCard, "Lo que Windows entrega a cada parlante. El Bluetooth suma su propia demora después.", 22, 46, 560, 22, 9, Palette.Muted, false);
            acousticToggle = Styles.Chip("Medición con micrófono", Palette.Blue); acousticToggle.Size = new Size(190, 34); monitorCard.Controls.Add(acousticToggle);
            scale = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Palette.Background, ForeColor = Palette.Text, FlatStyle = FlatStyle.Flat, AccessibleName = "Ventana de tiempo de las ondas", Font = new Font("Segoe UI", 9.5f) };
            scale.Items.AddRange(new object[] { "1 segundo", "2 segundos", "4 segundos" }); scale.SelectedIndex = 1; scale.Width = 112; monitorCard.Controls.Add(scale);
            freeze = ActionAt(monitorCard, "Congelar", 0, 0, 100, 34, false);
            collapseMonitor = ActionAt(monitorCard, "Ocultar", 0, 0, 90, 34, false);
            waves = new WaveView(sourceHistory, targetHistory); monitorCard.Controls.Add(waves);
            acousticView = new AcousticWaveView(); acousticView.Visible = false; monitorCard.Controls.Add(acousticView);
            waveHint = TextAt(monitorCard, "Que las ondas coincidan no asegura que el sonido coincida: para eso usa Calibrar con micrófono.", 22, 0, 900, 22, 9, Palette.Muted, false);
            acousticToggle.CheckedChanged += delegate
            {
                acousticView.Visible = acousticToggle.Checked; waves.Visible = !acousticToggle.Checked; freeze.Enabled = scale.Enabled = !acousticToggle.Checked;
                monitorTitle.Text = acousticToggle.Checked ? "Sonido medido por micrófono" : "Ondas en vivo";
                monitorDescription.Text = acousticToggle.Checked ? "Última calibración: cada fila es un parlante medido por separado." : "Lo que Windows entrega a cada parlante. El Bluetooth suma su propia demora después.";
                waveHint.Text = acousticToggle.Checked ? "Si las filas muestran Δ cercano a 0 ms, los parlantes suenan juntos. Repite la medición después de ajustar." : "Que las ondas coincidan no asegura que el sonido coincida: para eso usa Calibrar con micrófono.";
            };
            freeze.Click += delegate { if (waves.Frozen) waves.Resume(); else waves.Freeze(); freeze.Text = waves.Frozen ? "Reanudar" : "Congelar"; };
            scale.SelectedIndexChanged += delegate { waves.WindowMs = new[] { 1000, 2000, 4000 }[scale.SelectedIndex]; freeze.Text = "Congelar"; };
            collapseMonitor.Click += delegate { monitorCollapsed = !monitorCollapsed; collapseMonitor.Text = monitorCollapsed ? "Mostrar" : "Ocultar"; LayoutPage(); };

            // Footer.
            footer = new Panel { BackColor = Palette.Background }; page.Controls.Add(footer);
            var more = ActionAt(footer, "Más parlantes", 0, 0, 140, 38, false); more.Click += delegate { ShowOutputs(); };
            var sound = ActionAt(footer, "Sonido de Windows", 150, 0, 170, 38, false); sound.Click += delegate { System.Diagnostics.Process.Start("ms-settings:sound"); };
            var exit = ActionAt(footer, "Salir", 0, 0, 96, 38, false); exit.Name = "exit"; exit.Click += delegate { RequestExit(); };
            tips.SetToolTip(exit, "Detiene el audio y cierra AudioDúo.");
            TextAt(footer, "La X deja el audio sonando: AudioDúo sigue junto al reloj. Doble clic en su icono para volver.", 0, 46, 760, 22, 9, Palette.Muted, false);

            // Legacy/engine controls that live in Ajustes or stay hidden.
            start = new Button(); stop = new Button(); savedDelay = new Button();
            refresh = new Button { Text = "Actualizar lista" }; refresh.Click += delegate { LoadDevices(); RefreshSpeakerOverview(); };
            delay.Minimum = 0; delay.Maximum = 1500; delay.Value = 310;
            delay.ValueChanged += delegate { ApplyDelay(); };
            mainRoute = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Canales de la copia principal" };
            mainRoute.Items.AddRange(ChannelRouter.Names); mainRoute.SelectedIndex = 0;
            mainRoute.SelectedIndexChanged += delegate { if (mainRouter != null) mainRouter.Route = mainRoute.SelectedIndex; ScheduleSave(); };
            bridgeToggle = new CheckBox { Text = "Usar cable virtual (VB-CABLE) como puente" };
            bridgeToggle.CheckedChanged += delegate { usingBridge = bridgeToggle.Checked; ScheduleSave(); };
            source.SelectedIndexChanged += delegate { if (!reloadingDevices && !loadingSettings && bridgeToggle != null) bridgeToggle.Checked = false; };
            sourceVolume.Enabled = false; sourceVolume.Maximum = 100;
            sourceVolume.Scroll += delegate { try { if (sourceDevice != null) sourceDevice.AudioEndpointVolume.MasterVolumeLevelScalar = sourceVolume.Value / 100f; } catch (Exception ex) { Fail(ex); } };
            targetVolume.Maximum = 100; targetVolume.Value = 65;
            targetVolume.Scroll += delegate { ApplyListeningGains(); ScheduleSave(); };
            BuildSettings();

            tray.Icon = Icon; tray.Text = "AudioDúo";
            trayMenu.Items.Add("Abrir AudioDúo", null, delegate { RestoreWindow(); });
            trayMenu.Items.Add("Conectar", null, delegate { ConnectByUser(); });
            trayMenu.Items.Add("Detener audio", null, delegate { StopByUser(); });
            trayMenu.Items.Add(new ToolStripSeparator()); trayMenu.Items.Add("Salir y detener audio", null, delegate { RequestExit(); });
            tray.ContextMenuStrip = trayMenu; tray.Visible = true; tray.DoubleClick += delegate { RestoreWindow(); };
            timer.Interval = 1000; timer.Tick += delegate { UpdateStatus(); }; timer.Start();
            visualTimer.Interval = 50;
            visualTimer.Tick += delegate
            {
                UpdateSourceCompensation();
                if (!Visible || WindowState == FormWindowState.Minimized) return;
                if (!monitorCollapsed) waves.Invalidate();
                UpdateCardMeters();
            };
            visualTimer.Start(); saveTimer.Interval = 600; saveTimer.Tick += delegate { saveTimer.Stop(); SaveSettings(); };
            host.Resize += delegate { LayoutPage(); };
            LoadDevices(); LoadSettings(); LoadExtraSpeakers(); LoadIgnoredSpeakers(); RefreshSpeakerOverview();
            status.Text = "Listo. Pulsa Conectar para que suenen tus parlantes.";
            RefreshChrome(); LayoutPage();
            StartWatchingDevices();
            Shown += delegate { BeginInvoke((Action)TryAutoConnect); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized && !closing) HideToTray(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing && !exitRequested) { e.Cancel = true; HideToTray(); return; }
                closing = true; SaveSettings(); timer.Stop(); visualTimer.Stop(); saveTimer.Stop(); StopWatchingDevices(); StopAudio(); devices.Dispose();
                tray.Visible = false; tray.Dispose(); trayMenu.Dispose(); timer.Dispose(); visualTimer.Dispose(); saveTimer.Dispose(); tips.Dispose(); logo.Image.Dispose();
                if (settingsWindow != null) settingsWindow.Dispose();
            };
        }

        // Manual, width-driven layout: everything stretches with the window and only scrolls vertically.
        void LayoutPage()
        {
            if (host == null || page == null) return;
            int width = Math.Max(760, host.ClientSize.Width), pad = 24, inner = width - 2 * pad, y = 16;
            page.SuspendLayout();
            page.Width = width;
            // Narrow windows move the action buttons under the title instead of covering it.
            int buttons = settingsButton.Width + calibrateButton.Width + test.Width + connectButton.Width + 30;
            bool twoRows = inner < buttons + 480;
            header.SetBounds(pad, y, inner, twoRows ? 120 : 64);
            int x = inner, buttonTop = twoRows ? 74 : 10;
            foreach (var b in new[] { settingsButton, calibrateButton, test, connectButton }) { x -= b.Width; b.Location = new Point(x, buttonTop); x -= 10; }
            badge.Left = 236; badge.Width = twoRows ? 260 : Math.Max(120, Math.Min(260, x - 246));
            y += twoRows ? 132 : 76;
            noticeCard.SetBounds(pad, y, inner, 48);
            int right = inner - 12;
            if (noticeDismiss.Visible) { noticeDismiss.Left = right - noticeDismiss.Width; right = noticeDismiss.Left - 8; }
            if (noticeAction.Visible) { noticeAction.Left = right - noticeAction.Width; right = noticeAction.Left - 8; }
            status.SetBounds(40, 0, right - 44, 48); noticePrompt.SetBounds(40, 0, right - 44, 48);
            y += 60;
            masterCard.SetBounds(pad, y, inner, 96);
            masterMute.Location = new Point(inner - masterMute.Width - 20, 30);
            masterPercent.Location = new Point(masterMute.Left - masterPercent.Width - 12, 30);
            masterVolume.SetBounds(330, 33, Math.Max(120, masterPercent.Left - 344), 30);
            y += 112;
            speakersTitle.SetBounds(pad, y, 220, 30);
            int hintRight = inner;
            if (removeOffline.Visible) { removeOffline.Location = new Point(pad + inner - removeOffline.Width, y - 2); hintRight = inner - removeOffline.Width - 12; }
            speakersHint.SetBounds(pad + 220, y + 4, Math.Max(0, hintRight - 220), 22);
            y += 38;
            int count = Math.Max(1, speakerOverview.Controls.Count);
            // A card needs ~470 px for the delay buttons; below that it takes the full width.
            int columns = Math.Min(count, inner >= 1470 ? 3 : inner >= 960 ? 2 : 1);
            int cardWidth = (inner + 16) / columns - 16;
            foreach (Control card in speakerOverview.Controls) { card.Width = cardWidth; card.Margin = new Padding(8); }
            int rows = (speakerOverview.Controls.Count + columns - 1) / columns;
            int gridHeight = rows * (CardHeight + 16);
            speakerOverview.SetBounds(pad - 8, y - 8, inner + 16, gridHeight);
            y += gridHeight - 8 + 12;
            int lanes = Math.Max(2, waves.TraceCount), waveHeight = lanes * 66 + 42;
            int tools = collapseMonitor.Width + freeze.Width + scale.Width + acousticToggle.Width + 40;
            bool toolsBelow = inner - tools - 40 < 420 && !monitorCollapsed;
            int wavesTop = toolsBelow ? 128 : 84;
            int monitorHeight = monitorCollapsed ? 82 : wavesTop + 8 + waveHeight + 40;
            monitorCard.SetBounds(pad, y, inner, monitorHeight);
            int cx = inner - 20, toolsTop = toolsBelow ? 78 : 18;
            foreach (Control c in new Control[] { collapseMonitor, freeze, scale, acousticToggle }) { cx -= c.Width; c.Location = new Point(cx, c == scale ? toolsTop + 4 : toolsTop); cx -= 10; }
            if (toolsBelow) { collapseMonitor.Location = new Point(inner - 20 - collapseMonitor.Width, 18); }
            monitorDescription.Width = toolsBelow ? inner - 44 - collapseMonitor.Width - 10 : Math.Max(200, cx - 30);
            foreach (Control c in new Control[] { freeze, scale, acousticToggle, waves, acousticView, waveHint }) c.Visible = !monitorCollapsed && (c == waves ? !acousticToggle.Checked : c == acousticView ? acousticToggle.Checked : true);
            waves.SetBounds(22, wavesTop, inner - 44, waveHeight); acousticView.Bounds = waves.Bounds;
            waveHint.SetBounds(22, wavesTop + waveHeight + 10, inner - 44, 22);
            y += monitorHeight + 20;
            footer.SetBounds(pad, y, inner, 72);
            footer.Controls["exit"].Left = inner - footer.Controls["exit"].Width;
            y += 84;
            page.Height = y;
            page.ResumeLayout();
        }

        // Header state, main button and notice colour.
        void RefreshChrome()
        {
            if (closing || IsDisposed || connectButton == null) return;
            if (running) { connectButton.Text = "Detener"; Styles.Secondary(connectButton); connectButton.Font = new Font("Segoe UI", 10, FontStyle.Bold); }
            else { connectButton.Text = "Conectar"; Styles.Primary(connectButton); }
            tips.SetToolTip(connectButton, running ? "Detiene las copias. El parlante principal sigue sonando por Windows." : "Hace sonar todos tus parlantes sincronizados.");
            test.Enabled = running && !calibrating;
            Color state;
            if (calibrating) { badge.Text = "CALIBRANDO"; state = Palette.Blue; }
            else if (running) { badge.Text = masterMute.Checked ? "EN VIVO · SILENCIADO" : "EN VIVO"; state = masterMute.Checked ? Styles.Amber : Palette.Teal; }
            else if (waitingForDevices) { badge.Text = "ESPERANDO PARLANTE"; state = Styles.Amber; }
            else if (lastFailure) { badge.Text = "SE DETUVO"; state = Styles.Red; }
            else { badge.Text = "DETENIDO"; state = Palette.Muted; }
            badge.Accent = state; noticeDot.ForeColor = state;
            tray.Text = running ? "AudioDúo · en vivo" : waitingForDevices ? "AudioDúo · esperando parlante" : "AudioDúo · detenido";
        }

        // A question in the notice bar (for example, a newly connected speaker).
        Action noticeYes, noticeNo;
        void AskInNotice(string text, string yes, Action onYes, Action onNo, string no = "Ahora no")
        {
            noticePrompt.Text = text; noticePrompt.Visible = true; status.Visible = false;
            noticeAction.Text = yes; noticeAction.Width = Math.Max(110, TextRenderer.MeasureText(yes, noticeAction.Font).Width + 30);
            noticeDismiss.Text = no; noticeDismiss.Width = Math.Max(100, TextRenderer.MeasureText(no, noticeDismiss.Font).Width + 30);
            noticeAction.Visible = noticeDismiss.Visible = true; noticeYes = onYes; noticeNo = onNo;
            LayoutPage();
        }
        void CloseNotice()
        {
            noticePrompt.Visible = noticeAction.Visible = noticeDismiss.Visible = false; status.Visible = true;
            noticeYes = noticeNo = null; LayoutPage();
        }

        static string Db(float peak) { return peak < .00001 ? "−∞ dBFS" : (20 * Math.Log10(peak)).ToString("0.0") + " dBFS"; }
        void ChangeDelay(int delta) { delay.Value = Math.Max(delay.Minimum, Math.Min(delay.Maximum, delay.Value + delta)); }
        void ApplyDelay()
        {
            MarkAcousticStale();
            if (processor != null) processor.SetDelay((int)delay.Value);
            ScheduleSave();
        }
        // Volume model. Every speaker has its own level (its card) and the master applies the same dB to all.
        // Levels use Windows' own slider taper (50 % ≈ −10 dB, 10 % ≈ −33 dB). Copies get the master as gain;
        // the direct Windows output has no gain stage of ours, so its endpoint is set to own + master dB.
        // The copies' compensation keeps them independent of that endpoint, so the balance is preserved.
        const double Taper = 1.66;
        static double LevelDb(int level) { return level <= 0 ? -96 : Math.Max(-96, 20 * Taper * Math.Log10(level / 100.0)); }
        static int LevelFromDb(double db) { return db <= -96 ? 0 : (int)Math.Round(Math.Max(0, Math.Min(100, 100 * Math.Pow(10, db / (20 * Taper))))); }
        float MasterGain { get { return masterLevel <= 0 || (masterMute != null && masterMute.Checked) ? 0 : (float)Math.Pow(10, LevelDb(masterLevel) / 20); } }
        int sourceOwnLevel = -1;
        double sourceSetDb = double.NaN;
        void WithSourceEndpoint(Action<NAudio.CoreAudioApi.AudioEndpointVolume> action)
        {
            var a = source.SelectedItem as DeviceItem;
            if (usingBridge || a == null || a.Offline) return;
            try
            {
                var device = sourceDevice ?? devices.GetDevice(a.Id);
                try { action(device.AudioEndpointVolume); }
                finally { if (device != sourceDevice) device.Dispose(); }
            }
            catch (Exception) { }
        }
        // A change made outside the app (volume keys, Windows mixer) becomes the speaker's own level.
        void SyncSourceOwnLevel()
        {
            WithSourceEndpoint(endpoint =>
            {
                double db = endpoint.MasterVolumeLevel;
                if (sourceOwnLevel < 0 || double.IsNaN(sourceSetDb) || Math.Abs(db - sourceSetDb) > .5) { sourceOwnLevel = LevelFromDb(db - LevelDb(masterLevel)); sourceSetDb = db; }
            });
        }
        void ApplySourceVolume()
        {
            if (sourceOwnLevel < 0) return;
            WithSourceEndpoint(endpoint =>
            {
                double db = Math.Max(endpoint.VolumeRange.MinDecibels, Math.Min(endpoint.VolumeRange.MaxDecibels, LevelDb(sourceOwnLevel) + LevelDb(masterLevel)));
                endpoint.MasterVolumeLevel = (float)db; sourceSetDb = endpoint.MasterVolumeLevel;
            });
        }
        void SetSourceOwnLevel(int level) { SyncSourceOwnLevel(); sourceOwnLevel = level; ApplySourceVolume(); }
        void SetMasterVolume(int level)
        {
            if (loadingSettings) { masterLevel = level; if (masterPercent != null) masterPercent.Text = level + "%"; return; }
            SyncSourceOwnLevel();
            masterLevel = level; if (masterPercent != null) masterPercent.Text = level + "%";
            ApplySourceVolume(); ApplyListeningGains(); ScheduleSave();
        }
        // Direct mode: Windows applies the source speaker's volume before its loopback, so the copies would
        // follow that speaker's volume. Undo it so each speaker keeps its own volume. The loopback is float, so even
        // the endpoint's −96 dB floor is recovered exactly; at true silence there is nothing to amplify.
        // Measured: while the endpoint is muted Windows skips its volume stage, and the loopback carries the
        // full-level signal, so no compensation is applied then (otherwise Solo on a copy made it ~9x louder).
        void UpdateSourceCompensation()
        {
            if (!running || queue == null) return;
            float gain = 1;
            try
            {
                var endpoint = !usingBridge && sourceDevice != null ? sourceDevice.AudioEndpointVolume : null;
                if (endpoint != null && !endpoint.Mute) gain = (float)Math.Min(1e5, Math.Pow(10, -endpoint.MasterVolumeLevel / 20));
            }
            catch (Exception) { gain = 1; }
            queue.InputGain = gain;
            foreach (var branch in activeExtras) branch.Input.InputGain = gain;
        }
        void ScheduleSave() { if (!loadingSettings && !closing) { saveTimer.Stop(); saveTimer.Start(); } }
    }
}
