using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace AudioDuo
{
    sealed partial class MainForm
    {
        const int CardHeight = 252;
        FlowLayoutPanel speakerOverview;
        Label waveHint;
        string overviewSignature;
        int overviewTick;
        readonly List<Action> updateInlineStates = new List<Action>();
        readonly List<KeyValuePair<LevelMeter, WaveHistory>> cardMeters = new List<KeyValuePair<LevelMeter, WaveHistory>>();
        readonly HashSet<string> mutedSpeakers = new HashSet<string>();
        string soloSpeaker;
        readonly Dictionary<string, Button> manualPlusButtons = new Dictionary<string, Button>();
        static readonly Color[] TraceColors = { Palette.Blue, Palette.Teal, Color.FromArgb(247, 195, 103), Color.FromArgb(183, 153, 255) };
        static bool IsUtilityOutput(string name) { return name.IndexOf("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("7.1 Surround Sound", StringComparison.OrdinalIgnoreCase) >= 0; }
        static bool IsEnhancer(string name) { return name.IndexOf("7.1 Surround Sound", StringComparison.OrdinalIgnoreCase) >= 0; }
        static string SpeakerName(string name)
        {
            if (name.IndexOf("High Definition Audio", StringComparison.OrdinalIgnoreCase) >= 0 || name == "Parlantes por cable") return "Parlante por cable";
            // The user's enhancement program; its output goes to the wired front speakers.
            if (IsEnhancer(name)) return "Parlantes de cable · mejora 7.1";
            return name.Replace("Auriculares (", "Bluetooth · ").TrimEnd(')');
        }
        float ListeningGain(string id, int volume) { return mutedSpeakers.Contains(id) || (soloSpeaker != null && soloSpeaker != id) || volume <= 0 ? 0 : (float)Math.Pow(10, LevelDb(volume) / 20) * MasterGain; }
        bool Silenced(string id) { return mutedSpeakers.Contains(id) || (soloSpeaker != null && soloSpeaker != id) || MasterGain == 0; }
        bool sourceListeningMute, sourceMuteBefore;
        void ApplyListeningGains()
        {
            var b = target.SelectedItem as DeviceItem;
            if (b != null && processor != null) processor.Gain = ListeningGain(b.Id, targetVolume.Value);
            foreach (var p in extraSpeakers) if (p.Branch != null) p.Branch.Delay.Gain = ListeningGain(p.Id, p.Volume);
            // Direct mode: the Windows output has no gain stage of ours, so Silenciar/Solo use its endpoint mute.
            // Its loopback keeps the signal while muted, so the copies keep playing.
            var a = source.SelectedItem as DeviceItem;
            bool silence = running && !usingBridge && sourceDevice != null && a != null && ListeningGain(a.Id, 100) == 0;
            try
            {
                if (silence && !sourceListeningMute) { sourceMuteBefore = sourceDevice.AudioEndpointVolume.Mute; sourceDevice.AudioEndpointVolume.Mute = true; sourceListeningMute = true; }
                else if (!silence && sourceListeningMute) { if (sourceDevice != null) sourceDevice.AudioEndpointVolume.Mute = sourceMuteBefore; sourceListeningMute = false; }
            }
            catch (Exception) { sourceListeningMute = false; }
        }
        void ResetListening() { mutedSpeakers.Clear(); soloSpeaker = null; ApplyListeningGains(); }
        void UpdateCardMeters()
        {
            foreach (var pair in cardMeters)
            {
                float peak = running && pair.Value != null ? pair.Value.Peak : 0;
                pair.Key.Peak = Math.Max(peak, pair.Key.Peak * .82f); pair.Key.Invalidate();
            }
        }

        // Common card frame: colour dot, name, status chip, role and a live level meter.
        Surface NewCard(string name, Color color, string role, WaveHistory history, out Pill chip, out Label roleLabel)
        {
            var card = new Surface { Size = new Size(460, CardHeight), Margin = new Padding(8) }; speakerOverview.Controls.Add(card);
            TextAt(card, "●", 18, 14, 22, 26, 12, color, false);
            TextAt(card, name, 40, 14, 300, 28, 12.5f, Palette.Text, true).Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            chip = new Pill { Anchor = AnchorStyles.Top | AnchorStyles.Right }; chip.SetBounds(card.Width - 168, 16, 150, 26); card.Controls.Add(chip);
            roleLabel = TextAt(card, role, 20, 46, card.Width - 40, 20, 9, Palette.Muted, false); roleLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var level = new LevelMeter { Accent = color, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, AccessibleName = "Nivel de " + name };
            level.SetBounds(20, 74, card.Width - 40, 6); card.Controls.Add(level);
            cardMeters.Add(new KeyValuePair<LevelMeter, WaveHistory>(level, history));
            return card;
        }
        void SetChip(Pill chip, string id, bool available, bool enabled)
        {
            if (!enabled) { chip.Text = "Desactivado"; chip.Accent = Palette.Muted; }
            else if (!available) { chip.Text = "Apagado"; chip.Accent = Styles.Red; }
            else if (running && Silenced(id)) { chip.Text = "Silenciado"; chip.Accent = Styles.Amber; }
            else if (running) { chip.Text = "Sonando"; chip.Accent = Palette.Teal; }
            else { chip.Text = waitingForDevices ? "Esperando" : "Listo"; chip.Accent = waitingForDevices ? Styles.Amber : Palette.Muted; }
        }
        void AddListeningChips(Surface card, string id, string name, Color color, Func<bool> updating)
        {
            var mute = Styles.Chip("Silenciar", Styles.Amber); mute.SetBounds(20, 198, 108, 34); mute.AccessibleName = "Silenciar " + name; card.Controls.Add(mute);
            var solo = Styles.Chip("Solo este", color); solo.SetBounds(136, 198, 108, 34); solo.AccessibleName = "Escuchar solo " + name; card.Controls.Add(solo);
            tips.SetToolTip(mute, "Silencia este parlante sin desconectarlo.");
            tips.SetToolTip(solo, "Silencia los demás para escuchar solo este y compararlo.");
            mute.CheckedChanged += delegate { if (updating()) return; if (mute.Checked) mutedSpeakers.Add(id); else mutedSpeakers.Remove(id); ApplyListeningGains(); RefreshSpeakerOverview(); };
            solo.CheckedChanged += delegate { if (updating()) return; soloSpeaker = solo.Checked ? id : null; ApplyListeningGains(); RefreshSpeakerOverview(); };
            updateInlineStates.Add(delegate { mute.Checked = mutedSpeakers.Contains(id); solo.Checked = soloSpeaker == id; });
        }
        Slider AddVolumeRow(Surface card, string name, Color color, int value, out Label percent)
        {
            TextAt(card, "Volumen", 20, 150, 76, 22, 9.5f, Palette.Muted, false);
            var volume = new Slider { Maximum = 100, Value = value, Accent = color, AccessibleName = "Volumen de " + name, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            volume.SetBounds(96, 146, card.Width - 96 - 78, 30); card.Controls.Add(volume);
            percent = TextAt(card, value + "%", card.Width - 70, 150, 52, 22, 10, Palette.Text, true); percent.Anchor = AnchorStyles.Top | AnchorStyles.Right; percent.TextAlign = ContentAlignment.MiddleRight;
            return volume;
        }

        Surface AddSourceCard(string id, string name, Color color)
        {
            Pill chip; Label role;
            var card = NewCard(name, color, "Salida principal de Windows · suena directo y YouTube ajusta la imagen a él", sourceHistory, out chip, out role);
            TextAt(card, "Retraso", 20, 100, 76, 22, 9.5f, Palette.Muted, false);
            TextAt(card, "0 ms", 96, 92, 90, 34, 16, color, true);
            TextAt(card, "Es la referencia: los demás se ajustan a este.", 190, 101, card.Width - 210, 22, 9, Palette.Muted, false).Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            SyncSourceOwnLevel();
            Label percent; var volume = AddVolumeRow(card, name, color, Math.Max(0, sourceOwnLevel), out percent);
            tips.SetToolTip(volume, "Volumen propio de este parlante. También lo cambian las teclas de volumen de Windows.");
            bool updating = false;
            volume.Scroll += delegate { if (updating) return; SetSourceOwnLevel(volume.Value); percent.Text = volume.Value + "%"; };
            AddListeningChips(card, id, name, color, () => updating);
            updateInlineStates.Add(delegate
            {
                updating = true;
                if (!volume.Capture) { SyncSourceOwnLevel(); volume.Value = Math.Max(0, sourceOwnLevel); }
                percent.Text = volume.Value + "%";
                var a = source.SelectedItem as DeviceItem; SetChip(chip, id, a != null && !a.Offline, true);
                updating = false;
            });
            return card;
        }

        Surface AddManualCard(string id, string name, Color color, Func<int> getDelay, Action<int> setDelay, Func<int> getVolume, Action<int> setVolume, Func<string> getState, WaveHistory history = null, string role = null, Func<bool> available = null, Func<bool> enabled = null)
        {
            Pill chip; Label roleLabel;
            var card = NewCard(name, color, role ?? "Copia sincronizada · espera al parlante principal", history, out chip, out roleLabel);
            TextAt(card, "Retraso", 20, 100, 76, 22, 9.5f, Palette.Muted, false);
            var timing = new NumericUpDown { Minimum = 0, Maximum = 1500, Increment = 1, Value = getDelay(), BackColor = Palette.Background, ForeColor = color, Font = new Font("Segoe UI", 14, FontStyle.Bold), BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right, AccessibleName = "Milisegundos de retraso de " + name };
            timing.SetBounds(96, 92, 96, 34); card.Controls.Add(timing);
            TextAt(card, "ms", 196, 100, 30, 22, 9.5f, Palette.Muted, false);
            tips.SetToolTip(timing, "Más milisegundos = este parlante suena más tarde. Se aplica al instante, sin cortar el sonido.");
            int x = 232;
            foreach (int amount in new[] { -10, -1, 1, 10 })
            {
                int step = amount;
                var button = new Button { Text = amount.ToString("+0;−0"), AccessibleName = name + ": " + amount.ToString("+0;-0") + " ms" };
                Styles.Secondary(button); button.BackColor = Palette.Background; button.SetBounds(x, 93, 46, 32); card.Controls.Add(button); x += 50;
                button.Click += delegate { timing.Value = Math.Max(0, Math.Min(1500, getDelay() + step)); };
                if (amount == 10) manualPlusButtons[id] = button;
            }
            Label percent; var volume = AddVolumeRow(card, name, color, getVolume(), out percent);
            bool updating = false;
            timing.ValueChanged += delegate { if (!updating) setDelay((int)timing.Value); };
            volume.Scroll += delegate { if (!updating) { setVolume(volume.Value); percent.Text = volume.Value + "%"; ApplyListeningGains(); } };
            AddListeningChips(card, id, name, color, () => updating);
            updateInlineStates.Add(delegate
            {
                updating = true;
                if (!timing.ContainsFocus) timing.Value = getDelay();
                if (!volume.Capture) volume.Value = getVolume();
                percent.Text = getVolume() + "%";
                SetChip(chip, id, available == null || available(), enabled == null || enabled());
                if (running && getState != null && enabled != null && enabled() && getState().StartsWith("No disponible")) { chip.Text = "Sin conexión"; chip.Accent = Styles.Red; }
                updating = false;
            });
            return card;
        }

        void RefreshSpeakerOverview()
        {
            if (speakerOverview == null || waves == null || closing) return;
            var a = source.SelectedItem as DeviceItem; var b = target.SelectedItem as DeviceItem;
            var profiles = extraSpeakers.Where(p => (a == null || p.Id != a.Id) && (b == null || p.Id != b.Id) && !(IsUtilityOutput(p.Name) && !IsEnhancer(p.Name))).ToArray();
            var traces = new List<WaveTrace>();
            bool direct = a != null && !usingBridge;
            Color copyColor = direct ? TraceColors[1] : TraceColors[0];
            if (direct) traces.Add(new WaveTrace { Label = SpeakerName(a.Name), State = "Principal · 0 ms", History = sourceHistory, Color = TraceColors[0] });
            if (b != null) traces.Add(new WaveTrace { Label = SpeakerName(b.Name), State = "Copia · " + delay.Value + " ms", History = targetHistory, Color = copyColor });
            foreach (var p in profiles) traces.Add(new WaveTrace { Label = SpeakerName(p.Name), State = p.Enabled ? "Copia · " + p.Delay + " ms" : "Desactivado", History = p.Enabled ? p.History : null, Color = TraceColors[traces.Count % TraceColors.Length] });
            waves.SetTraces(traces);
            string signature = direct + "|" + (a == null ? "" : a.Id + a.Offline) + "|" + (b == null ? "" : b.Id + b.Offline) + "|" + string.Join("|", profiles.Select(p => p.Id + p.Enabled));
            if (signature != overviewSignature)
            {
                overviewSignature = signature;
                foreach (Control control in speakerOverview.Controls.Cast<Control>().ToArray()) control.Dispose();
                speakerOverview.Controls.Clear(); updateInlineStates.Clear(); manualPlusButtons.Clear(); cardMeters.Clear();
                if (direct) AddSourceCard(a.Id, SpeakerName(a.Name), TraceColors[0]);
                if (b != null)
                {
                    var copy = b;
                    string role = IsEnhancer(copy.Name) ? "Copia con la mejora 7.1 · sale por los parlantes de cable" : "Copia sincronizada · espera al parlante principal";
                    AddManualCard(copy.Id, SpeakerName(copy.Name), copyColor, () => (int)delay.Value, v => delay.Value = v, () => targetVolume.Value, v => targetVolume.Value = v, null, targetHistory, role, () => !copy.Offline);
                }
                int index = direct ? 2 : 1;
                foreach (var p in profiles)
                {
                    var profile = p;
                    var card = AddManualCard(p.Id, SpeakerName(p.Name), TraceColors[index++ % TraceColors.Length], () => profile.Delay, v => { profile.Delay = v; SpeakerChanged(profile); }, () => profile.Volume, v => { profile.Volume = v; SpeakerChanged(profile); }, () => profile.State, profile.History, null, () => IsActive(profile.Id), () => profile.Enabled);
                    var remove = ActionAt(card, "Quitar", card.Width - 120, 198, 100, 34, false); remove.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    tips.SetToolTip(remove, "Quita este parlante de AudioDúo. Puedes volver a agregarlo desde Más parlantes.");
                    remove.Click += delegate { RemoveSpeaker(profile); status.Text = "Se quitó «" + SpeakerName(profile.Name) + "»."; RefreshSpeakerOverview(); };
                    if (!profile.Enabled)
                    {
                        var activate = ActionAt(card, "Activar", card.Width - 230, 198, 100, 34, true); activate.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                        activate.Click += delegate { profile.Enabled = true; if (running) { StartExtra(profile); PublishExtras(); } SaveExtraSpeakers(); RefreshSpeakerOverview(); };
                    }
                }
                if (speakerOverview.Controls.Count == 0)
                {
                    var empty = new Surface { Size = new Size(460, CardHeight), Margin = new Padding(8) }; speakerOverview.Controls.Add(empty);
                    TextAt(empty, "Aún no hay parlantes configurados.\nAbre Ajustes para elegir el parlante principal y su copia.", 24, 24, 400, 60, 10, Palette.Muted, false);
                }
                LayoutPage();
            }
            int offline = OfflineExtras().Length;
            bool showRemove = offline > 0;
            if (removeOffline.Visible != showRemove || (showRemove && !removeOffline.Text.EndsWith("(" + offline + ")")))
            {
                removeOffline.Text = "Quitar apagados (" + offline + ")"; removeOffline.Visible = showRemove; LayoutPage();
            }
            foreach (var update in updateInlineStates) update();
        }
    }
}
