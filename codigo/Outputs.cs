using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Serialization;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioDuo
{
    // Stereo matrix. The principal endpoint remains the original, unmodified Windows mix.
    sealed class ChannelRouter : ISampleProvider
    {
        public static readonly string[] Names = { "Estéreo L/R", "Solo izquierda", "Solo derecha", "Mono L+R", "Ambiente L−R (simulado)" };
        readonly ISampleProvider input;
        float[] scratch = new float[0];
        float ll = 1, lr, rl, rr = 1;
        bool initial = true;
        public volatile int Route;
        public WaveFormat WaveFormat { get; private set; }
        public ChannelRouter(ISampleProvider input) { this.input = input; WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(input.WaveFormat.SampleRate, 2); }
        static float Toward(float value, float target) { return value + Math.Max(-.002f, Math.Min(.002f, target - value)); }
        public int Read(float[] buffer, int offset, int count)
        {
            int channels = input.WaveFormat.Channels, frames = count / 2;
            int needed = frames * channels; if (scratch.Length < needed) scratch = new float[needed];
            int read = input.Read(scratch, 0, needed) / channels;
            for (int f = 0; f < read; f++)
            {
                float a = 1, b = 0, c = 0, d = 1;
                switch (Route)
                {
                    case 1: a = c = 1; b = d = 0; break;
                    case 2: a = c = 0; b = d = 1; break;
                    case 3: a = b = c = d = .5f; break;
                    case 4: a = c = .5f; b = d = -.5f; break;
                }
                if (initial) { ll = a; lr = b; rl = c; rr = d; initial = false; }
                else { ll = Toward(ll, a); lr = Toward(lr, b); rl = Toward(rl, c); rr = Toward(rr, d); }
                float l = scratch[f * channels], r = channels > 1 ? scratch[f * channels + 1] : l;
                buffer[offset + f * 2] = l * ll + r * lr; buffer[offset + f * 2 + 1] = l * rl + r * rr;
            }
            return read * 2;
        }
    }
    public sealed class SpeakerProfile
    {
        public string Id = "", Name = "";
        public int Delay, Volume = 65, Route;
        public bool Enabled = true;
        [XmlIgnore] internal readonly WaveHistory History = new WaveHistory();
        [XmlIgnore] internal OutputBranch Branch;
        [XmlIgnore] internal string State = "Se activará al conectar";
    }
    sealed class OutputBranch : IDisposable
    {
        public readonly ElasticFeed Input;
        public readonly DelayVolume Delay;
        public readonly ChannelRouter Router;
        readonly MMDevice device, monitorDevice;
        readonly WasapiOut player;
        readonly WasapiCapture observer;
        public volatile string Error;
        volatile bool disposed;
        public OutputBranch(MMDeviceEnumerator devices, SpeakerProfile profile, WaveFormat sourceFormat) : this(devices, profile, sourceFormat, profile.Id) { }
        // monitorId: the endpoint whose loopback shows this speaker's sound (differs for virtual enhancers).
        public OutputBranch(MMDeviceEnumerator devices, SpeakerProfile profile, WaveFormat sourceFormat, string monitorId)
        {
            try
            {
                device = devices.GetDevice(profile.Id);
                Input = new ElasticFeed(sourceFormat, ElasticFeed.DeviceRate(device, sourceFormat.SampleRate));
                Router = new ChannelRouter(Input) { Route = profile.Route };
                Delay = new DelayVolume(Router) { Gain = profile.Volume / 100f }; Delay.SetDelay(profile.Delay);
                player = new WasapiOut(device, AudioClientShareMode.Shared, false, ElasticFeed.RenderMs); player.Init(new SampleToWaveProvider(Delay));
                player.PlaybackStopped += delegate(object s, StoppedEventArgs e) { if (!disposed && e.Exception != null) Error = e.Exception.Message; };
                profile.History.Clear();
                try
                {
                    monitorDevice = devices.GetDevice(monitorId); observer = new LowLatencyLoopback(monitorDevice);
                    observer.DataAvailable += delegate(object s, WaveInEventArgs e) { profile.History.Add(e.Buffer, e.BytesRecorded, observer.WaveFormat); };
                    observer.RecordingStopped += delegate(object s, StoppedEventArgs e) { if (!disposed && e.Exception != null) profile.State = "Audio activo · vista no disponible"; };
                    observer.StartRecording();
                }
                catch (Exception) { profile.State = "Audio activo · vista no disponible"; }
                player.Play();
            }
            catch { Dispose(); throw; }
        }
        public void Feed(byte[] data, int count) { if (!disposed) Input.Write(data, count); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (observer != null) observer.Dispose(); if (player != null) player.Dispose();
            if (monitorDevice != null) monitorDevice.Dispose(); if (device != null) device.Dispose();
        }
    }
    sealed partial class MainForm
    {
        readonly List<SpeakerProfile> extraSpeakers = new List<SpeakerProfile>();
        volatile OutputBranch[] activeExtras = new OutputBranch[0];
        ChannelRouter mainRouter;
        ComboBox mainRoute;
        Form outputsWindow;
        readonly string outputsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "parlantes.xml");
        internal DeviceItem[] AvailableOutputs()
        {
            var result = new List<DeviceItem>();
            foreach (var d in devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            { result.Add(new DeviceItem { Id = d.ID, Name = d.FriendlyName }); d.Dispose(); }
            return result.ToArray();
        }
        internal bool IsReserved(string id)
        {
            var a = source.SelectedItem as DeviceItem; var b = target.SelectedItem as DeviceItem;
            return (a != null && a.Id == id) || (b != null && b.Id == id) || extraSpeakers.Any(p => p.Id == id);
        }
        internal void AddSpeaker(DeviceItem device)
        {
            if (IsReserved(device.Id)) throw new InvalidOperationException("Esta salida ya está en uso. Elige otro parlante.");
            var profile = new SpeakerProfile { Id = device.Id, Name = device.Name };
            // Adding it by hand undoes an earlier «Ahora no» / «Deshacer».
            if (ignoredSpeakers.Remove(device.Id) | ignoredSpeakers.Remove(device.Name)) SaveIgnoredSpeakers();
            extraSpeakers.Add(profile); if (running) StartExtra(profile); PublishExtras(); SaveExtraSpeakers();
        }
        void StartExtra(SpeakerProfile profile)
        {
            if (!profile.Enabled) { profile.State = "Desactivado · ajuste conservado"; return; }
            try
            {
                if (sourceDevice.ID == profile.Id || targetDevice.ID == profile.Id) { profile.State = "Ya usado en la ventana principal"; return; }
                if (!IsActive(profile.Id))
                {
                    // Re-paired Bluetooth speakers come back with a new ID; keep their settings by matching the name.
                    var renamed = AvailableOutputs().FirstOrDefault(d => string.Equals(d.Name, profile.Name, StringComparison.OrdinalIgnoreCase) && !IsReserved(d.Id));
                    if (renamed == null) { profile.State = "Apagado · se activará al encenderlo"; return; }
                    profile.Id = renamed.Id; SaveExtraSpeakers();
                }
                profile.State = "Audio activo"; profile.Branch = new OutputBranch(devices, profile, capture.WaveFormat, MonitorIdFor(profile.Id, profile.Name));
            }
            catch (Exception ex) { profile.State = "No disponible: " + ex.Message; profile.Branch = null; }
        }
        void PublishExtras() { activeExtras = extraSpeakers.Where(p => p.Branch != null).Select(p => p.Branch).ToArray(); }
        void StartExtras() { foreach (var profile in extraSpeakers) StartExtra(profile); PublishExtras(); }
        void StopExtras()
        {
            activeExtras = new OutputBranch[0];
            foreach (var profile in extraSpeakers) { if (profile.Branch != null) profile.Branch.Dispose(); profile.Branch = null; profile.State = "Audio detenido"; }
        }
        void HealthExtras()
        {
            bool changed = false;
            foreach (var profile in extraSpeakers)
            {
                if (profile.Branch == null) continue;
                if (profile.Branch.Error != null)
                {
                    profile.State = "Desconectado: " + profile.Branch.Error; profile.Branch.Dispose(); profile.Branch = null; changed = true;
                }
            }
            if (changed) PublishExtras();
        }
        internal void RemoveSpeaker(SpeakerProfile profile)
        {
            extraSpeakers.Remove(profile); PublishExtras(); if (profile.Branch != null) profile.Branch.Dispose(); profile.Branch = null; SaveExtraSpeakers();
        }
        internal void RetrySpeaker(SpeakerProfile profile)
        {
            if (!profile.Enabled) { profile.State = "Marca Usar para activar esta salida."; return; }
            if (!running) { status.Text = "Conecta primero las salidas principales."; return; }
            var old = profile.Branch; profile.Branch = null; PublishExtras(); if (old != null) old.Dispose(); StartExtra(profile); PublishExtras();
        }
        internal void SpeakerChanged(SpeakerProfile profile)
        {
            MarkAcousticStale();
            if (profile.Branch != null) { profile.Branch.Delay.SetDelay(profile.Delay); profile.Branch.Delay.Gain = ListeningGain(profile.Id, profile.Volume); profile.Branch.Router.Route = profile.Route; }
            ScheduleSave();
        }
        void SaveExtraSpeakers()
        {
            try
            {
                string pending = outputsFile + ".tmp";
                using (var stream = File.Create(pending)) new XmlSerializer(typeof(List<SpeakerProfile>)).Serialize(stream, extraSpeakers);
                if (File.Exists(outputsFile)) File.Replace(pending, outputsFile, null); else File.Move(pending, outputsFile);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        void LoadExtraSpeakers()
        {
            try
            {
                if (!File.Exists(outputsFile)) return;
                using (var stream = File.OpenRead(outputsFile))
                {
                    var list = (List<SpeakerProfile>)new XmlSerializer(typeof(List<SpeakerProfile>)).Deserialize(stream);
                    foreach (var p in list)
                    {
                        if (string.IsNullOrWhiteSpace(p.Id) || extraSpeakers.Any(s => s.Id == p.Id)) continue;
                        p.Delay = Math.Max(0, Math.Min(1500, p.Delay)); p.Volume = Math.Max(0, Math.Min(100, p.Volume)); p.Route = Math.Max(0, Math.Min(4, p.Route)); extraSpeakers.Add(p);
                    }
                }
            }
            catch (IOException) { }
            catch (InvalidOperationException) { }
            catch (UnauthorizedAccessException) { }
        }
        void ShowOutputs()
        {
            if (outputsWindow != null && !outputsWindow.IsDisposed) { outputsWindow.Show(); outputsWindow.Activate(); return; }
            outputsWindow = new Form { Text = "AudioDúo · Más parlantes", Icon = Icon, Size = new Size(940, 760), MinimumSize = new Size(940, 680), StartPosition = FormStartPosition.CenterParent, BackColor = Palette.Background, ForeColor = Palette.Text, Font = Font, AutoScaleMode = AutoScaleMode.Dpi };
            var top = new Panel { Dock = DockStyle.Top, Height = 185 }; outputsWindow.Controls.Add(top);
            TextAt(top, "Amplía tu espacio", 24, 18, 600, 34, 21, Palette.Text, true);
            TextAt(top, "Cada copia tiene canal, volumen y retraso propios. El principal conserva la mezcla original.", 24, 60, 870, 24, 10, Palette.Muted, false);
            var choose = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Palette.Card, ForeColor = Palette.Text, FlatStyle = FlatStyle.Flat };
            choose.SetBounds(24, 96, 445, 30); top.Controls.Add(choose);
            var add = ActionAt(top, "Añadir", 481, 94, 105, 34, true);
            var scan = ActionAt(top, "Actualizar", 598, 94, 115, 34, false);
            var bluetooth = ActionAt(top, "Bluetooth de Windows", 725, 94, 170, 34, false);
            var note = TextAt(top, "Empareja cada parlante en Windows. Solo aparecen salidas activas que tu PC puede usar.", 24, 141, 875, 34, 9, Palette.Muted, false);
            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(18, 0, 18, 18), BackColor = Palette.Background };
            outputsWindow.Controls.Add(list); list.BringToFront();
            var clock = new System.Windows.Forms.Timer { Interval = 100 };
            var views = new List<WaveView>();
            Action scanDevices = delegate
            {
                choose.Items.Clear(); foreach (var d in AvailableOutputs()) if (!IsReserved(d.Id)) choose.Items.Add(d);
                if (choose.Items.Count > 0) choose.SelectedIndex = 0;
                else note.Text = "No hay salidas nuevas. Conecta otro parlante en Bluetooth de Windows y pulsa Actualizar.";
            };
            Action redraw = null;
            redraw = delegate
            {
                foreach (Control control in list.Controls.Cast<Control>().ToArray()) control.Dispose(); list.Controls.Clear(); views.Clear();
                foreach (var profile in extraSpeakers.ToArray())
                {
                    var p = profile;
                    var card = new Surface { Size = new Size(865, 246), Margin = new Padding(0, 0, 0, 14) }; list.Controls.Add(card);
                    TextAt(card, p.Name, 18, 12, 620, 25, 12, Palette.Text, true);
                    var remove = ActionAt(card, "Quitar", 765, 12, 80, 27, false); remove.Click += delegate { RemoveSpeaker(p); redraw(); scanDevices(); };
                    var route = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Palette.Background, ForeColor = Palette.Text, FlatStyle = FlatStyle.Flat };
                    route.Items.AddRange(ChannelRouter.Names); route.SelectedIndex = p.Route; route.SetBounds(18, 50, 225, 29); card.Controls.Add(route);
                    TextAt(card, "Retraso", 259, 53, 65, 24, 9, Palette.Muted, false);
                    var timing = new NumericUpDown { Minimum = 0, Maximum = 1500, Increment = 1, Value = p.Delay, BackColor = Palette.Background, ForeColor = Palette.Teal, Font = new Font("Segoe UI", 12) }; timing.SetBounds(326, 49, 90, 30); card.Controls.Add(timing);
                    TextAt(card, "ms", 424, 53, 30, 23, 9, Palette.Muted, false);
                    var volume = new Slider { Value = p.Volume, Accent = Palette.Teal }; volume.SetBounds(475, 48, 195, 28); card.Controls.Add(volume);
                    var volumeText = TextAt(card, p.Volume + "%", 677, 53, 55, 24, 9, Palette.Muted, false);
                    var retry = ActionAt(card, "Reintentar", 750, 50, 95, 29, false); retry.Click += delegate { RetrySpeaker(p); };
                    var view = new WaveView(sourceHistory, p.History); view.SetBounds(18, 87, 827, 122); card.Controls.Add(view); views.Add(view);
                    var pause = ActionAt(card, "Congelar", 649, 12, 103, 27, false);
                    pause.Click += delegate { if (view.Frozen) view.Resume(); else view.Freeze(); pause.Text = view.Frozen ? "Reanudar" : "Congelar"; };
                    var state = TextAt(card, p.State, 20, 217, 700, 23, 9, Palette.Muted, false); state.Tag = p;
                    var enabled = new CheckBox { Text = "Usar", Checked = p.Enabled, BackColor = Palette.Card, ForeColor = Palette.Text, Font = new Font("Segoe UI", 9) };
                    enabled.SetBounds(765, 213, 80, 27); card.Controls.Add(enabled);
                    enabled.CheckedChanged += delegate
                    {
                        p.Enabled = enabled.Checked;
                        if (!p.Enabled) { var previous = p.Branch; p.Branch = null; PublishExtras(); if (previous != null) previous.Dispose(); p.State = "Desactivado · ajuste conservado"; }
                        else if (running) { StartExtra(p); PublishExtras(); }
                        SaveExtraSpeakers();
                    };
                    route.SelectedIndexChanged += delegate { p.Route = route.SelectedIndex; SpeakerChanged(p); };
                    timing.ValueChanged += delegate { p.Delay = (int)timing.Value; SpeakerChanged(p); };
                    volume.Scroll += delegate { p.Volume = volume.Value; volumeText.Text = p.Volume + "%"; SpeakerChanged(p); };
                }
                if (extraSpeakers.Count == 0) TextAt(list, "Añade otro parlante para elegir qué señal recibe.\nL/R separa el estéreo; Ambiente usa L−R y es un efecto simulado, no un canal trasero 5.1.", 0, 0, 850, 90, 11, Palette.Muted, false);
            };
            add.Click += delegate { try { var selected = choose.SelectedItem as DeviceItem; if (selected == null) return; AddSpeaker(selected); redraw(); scanDevices(); } catch (Exception ex) { note.Text = ex.Message; } };
            scan.Click += delegate { scanDevices(); };
            bluetooth.Click += delegate { System.Diagnostics.Process.Start("ms-settings:bluetooth"); };
            clock.Tick += delegate
            {
                if (!outputsWindow.Visible) return;
                foreach (var view in views) view.Invalidate();
                foreach (Control card in list.Controls) foreach (Control label in card.Controls) if (label.Tag is SpeakerProfile) label.Text = ((SpeakerProfile)label.Tag).State;
            };
            outputsWindow.FormClosed += delegate { clock.Stop(); clock.Dispose(); outputsWindow = null; };
            scanDevices(); redraw(); clock.Start(); outputsWindow.Show(this);
        }
    }
}
