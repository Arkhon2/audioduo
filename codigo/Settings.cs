using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AudioDuo
{
    sealed partial class MainForm
    {
        Form settingsWindow;
        CheckBox autoConnectToggle, startupToggle, autoAddToggle;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // Device choice and technical options, kept out of the main screen.
        void BuildSettings()
        {
            var w = new Form { Text = "AudioDúo · Ajustes", Icon = Icon, ClientSize = new Size(620, 710), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, BackColor = Palette.Background, ForeColor = Palette.Text, Font = Font, AutoScaleMode = AutoScaleMode.Dpi, ShowInTaskbar = false };
            settingsWindow = w;
            // Only the user's X hides it. When the main window closes, owned forms close first: cancelling then
            // also cancelled the main window, so «Salir» and --exit-running did nothing after opening Ajustes.
            w.FormClosing += delegate(object s, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing && !closing && !exitRequested) { e.Cancel = true; w.Hide(); } };
            TextAt(w, "Ajustes", 24, 16, 400, 36, 18, Palette.Text, true);

            var speakers = new Surface(); speakers.SetBounds(24, 64, 572, 252); w.Controls.Add(speakers);
            TextAt(speakers, "Parlantes", 20, 14, 300, 26, 12, Palette.Text, true);
            TextAt(speakers, "Parlante principal (salida de Windows)", 20, 46, 520, 20, 9, Palette.Muted, false);
            StyleCombo(source); source.SetBounds(20, 68, 532, 30); speakers.Controls.Add(source);
            TextAt(speakers, "Normalmente el Bluetooth: suena directo y YouTube ajusta la imagen a él.", 20, 100, 532, 20, 8.5f, Palette.Muted, false);
            TextAt(speakers, "Copia sincronizada", 20, 128, 300, 20, 9, Palette.Muted, false);
            StyleCombo(target); target.SetBounds(20, 150, 360, 30); speakers.Controls.Add(target);
            StyleCombo(mainRoute); mainRoute.SetBounds(392, 150, 160, 30); speakers.Controls.Add(mainRoute);
            Styles.Secondary(refresh); refresh.SetBounds(20, 198, 150, 34); speakers.Controls.Add(refresh);
            TextAt(speakers, "Para cambiar de parlante, detén el audio primero.", 182, 205, 370, 22, 8.5f, Palette.Muted, false);

            var behaviour = new Surface(); behaviour.SetBounds(24, 328, 572, 268); w.Controls.Add(behaviour);
            TextAt(behaviour, "Comportamiento", 20, 14, 300, 26, 12, Palette.Text, true);
            autoConnectToggle = new CheckBox { Text = "Conectar solo cuando mis parlantes estén encendidos", Checked = autoConnect, ForeColor = Palette.Text, BackColor = Palette.Card, Font = new Font("Segoe UI", 10) };
            autoConnectToggle.SetBounds(20, 48, 532, 26); behaviour.Controls.Add(autoConnectToggle);
            TextAt(behaviour, "Al encender o reconectar el Bluetooth, vuelve a sonar con tu configuración.", 40, 74, 512, 20, 8.5f, Palette.Muted, false);
            autoAddToggle = new CheckBox { Text = "Agregar automáticamente los parlantes nuevos", Checked = autoAddSpeakers, ForeColor = Palette.Text, BackColor = Palette.Card, Font = new Font("Segoe UI", 10) };
            autoAddToggle.SetBounds(20, 108, 532, 26); behaviour.Controls.Add(autoAddToggle);
            TextAt(behaviour, "Suenan como copia en cuanto los conectas; con «Deshacer» se quitan y no vuelven a agregarse.", 40, 134, 512, 20, 8.5f, Palette.Muted, false);
            autoAddToggle.CheckedChanged += delegate { autoAddSpeakers = autoAddToggle.Checked; ScheduleSave(); };
            startupToggle = new CheckBox { Text = "Iniciar AudioDúo con Windows (junto al reloj)", Checked = StartsWithWindows(), ForeColor = Palette.Text, BackColor = Palette.Card, Font = new Font("Segoe UI", 10) };
            startupToggle.SetBounds(20, 172, 532, 26); behaviour.Controls.Add(startupToggle);
            bridgeToggle.ForeColor = Palette.Text; bridgeToggle.BackColor = Palette.Card; bridgeToggle.Font = new Font("Segoe UI", 10);
            bridgeToggle.SetBounds(20, 208, 532, 26); behaviour.Controls.Add(bridgeToggle);
            TextAt(behaviour, "Solo para más de un Bluetooth. Con el puente, el video no se ajusta solo.", 40, 234, 512, 20, 8.5f, Palette.Muted, false);
            autoConnectToggle.CheckedChanged += delegate { autoConnect = autoConnectToggle.Checked; ScheduleSave(); };
            startupToggle.CheckedChanged += delegate { SetStartsWithWindows(startupToggle.Checked); };

            meter.ForeColor = Palette.Muted; meter.Font = new Font("Segoe UI", 8.5f); meter.BackColor = Palette.Background; meter.SetBounds(24, 608, 572, 20); w.Controls.Add(meter);
            var bluetooth = ActionAt(w, "Bluetooth de Windows", 24, 650, 180, 38, false); bluetooth.Click += delegate { System.Diagnostics.Process.Start("ms-settings:bluetooth"); };
            var sound = ActionAt(w, "Sonido de Windows", 214, 650, 170, 38, false); sound.Click += delegate { System.Diagnostics.Process.Start("ms-settings:sound"); };
            var close = ActionAt(w, "Listo", 496, 650, 100, 38, true); close.Click += delegate { w.Hide(); };
            w.VisibleChanged += delegate { if (w.Visible) { startupToggle.Checked = StartsWithWindows(); autoConnectToggle.Checked = autoConnect; autoAddToggle.Checked = autoAddSpeakers; } else RefreshSpeakerOverview(); };
        }
        static void StyleCombo(ComboBox box)
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList; box.FlatStyle = FlatStyle.Flat; box.BackColor = Palette.Background; box.ForeColor = Palette.Text; box.Font = new Font("Segoe UI", 10);
        }
        internal Form SettingsForPreview() { ShowSettings(); return settingsWindow; }
        void ShowSettings()
        {
            if (settingsWindow.Visible) { settingsWindow.Activate(); return; }
            settingsWindow.Show(this);
        }
        static bool StartsWithWindows()
        {
            try { using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) return key != null && key.GetValue("AudioDuo") != null; }
            catch (Exception) { return false; }
        }
        // Per-user Run entry, created only when the user ticks the option; --tray starts it next to the clock.
        void SetStartsWithWindows(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (enabled) key.SetValue("AudioDuo", "\"" + Application.ExecutablePath + "\" --tray");
                    else if (key.GetValue("AudioDuo") != null) key.DeleteValue("AudioDuo");
                }
            }
            catch (Exception ex) { status.Text = "No se pudo cambiar el inicio con Windows: " + ex.Message; }
        }
    }
}
