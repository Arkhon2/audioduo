using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioDuo
{
    // Windows calls this on its own thread whenever an audio device appears, disappears or changes.
    sealed class DeviceWatcher : IMMNotificationClient
    {
        public event Action Changed;
        void Raise() { var handler = Changed; if (handler != null) handler(); }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { Raise(); }
        public void OnDeviceAdded(string pwstrDeviceId) { Raise(); }
        public void OnDeviceRemoved(string deviceId) { Raise(); }
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }

    sealed partial class MainForm
    {
        int failureRetries;
        bool defaultMovedAsked;
        bool autoConnect = true, autoAddSpeakers = true, waitingForDevices, lastFailure, userStopped, autoBlocked, reloadingDevices;
        DeviceWatcher watcher;
        readonly System.Windows.Forms.Timer deviceDebounce = new System.Windows.Forms.Timer { Interval = 1500 };
        HashSet<string> activeIds = new HashSet<string>();
        readonly HashSet<string> ignoredSpeakers = new HashSet<string>();
        readonly string ignoredFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "parlantes-ignorados.txt");

        internal void StartInTray() { trayHintShown = true; Shown += delegate { BeginInvoke((Action)HideToTray); }; }

        void StartWatchingDevices()
        {
            activeIds = ActiveRenderIds();
            deviceDebounce.Tick += delegate { deviceDebounce.Stop(); OnDevicesChanged(); };
            try
            {
                watcher = new DeviceWatcher();
                watcher.Changed += delegate
                {
                    if (closing || IsDisposed || !IsHandleCreated) return;
                    try { BeginInvoke((Action)delegate { deviceDebounce.Stop(); deviceDebounce.Start(); }); } catch (InvalidOperationException) { }
                };
                devices.RegisterEndpointNotificationCallback(watcher);
            }
            catch (Exception) { watcher = null; }
        }
        void StopWatchingDevices()
        {
            deviceDebounce.Stop();
            if (watcher != null) { try { devices.UnregisterEndpointNotificationCallback(watcher); } catch (Exception) { } watcher = null; }
            deviceDebounce.Dispose();
        }
        HashSet<string> ActiveRenderIds()
        {
            var ids = new HashSet<string>();
            try { foreach (var d in devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) { ids.Add(d.ID); d.Dispose(); } }
            catch (Exception) { }
            return ids;
        }
        bool IsActive(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            try { using (var d = devices.GetDevice(id)) return d.State == DeviceState.Active; }
            catch (Exception) { return false; }
        }

        // A speaker was switched on/off or connected. Keep the saved setup and resume on its own.
        void OnDevicesChanged()
        {
            if (closing) return;
            autoBlocked = false; failureRetries = 0;
            var before = activeIds; activeIds = ActiveRenderIds();
            LoadDevices();
            if (running)
            {
                // StartExtra also re-adopts a speaker that came back with a new ID.
                foreach (var p in extraSpeakers.Where(p => p.Enabled && p.Branch == null).ToArray()) StartExtra(p);
                PublishExtras(); ApplyListeningGains();
            }
            else TryAutoConnect();
            foreach (var id in activeIds.Where(id => !before.Contains(id)).ToArray()) OfferNewSpeaker(id);
            RefreshSpeakerOverview(); RefreshChrome();
        }
        // --preview renders the window only: it must never connect audio or overwrite the user's settings.
        internal bool PreviewOnly;
        void TryAutoConnect()
        {
            if (PreviewOnly || running || calibrating || closing || !autoConnect || userStopped || autoBlocked) return;
            var a = source.SelectedItem as DeviceItem; var b = target.SelectedItem as DeviceItem;
            if (a == null || b == null) return;
            if (IsActive(a.Id) && IsActive(b.Id)) StartAudio();
            else { waitingForDevices = true; ExplainWaiting(); RefreshChrome(); }
        }
        void ExplainWaiting()
        {
            var a = source.SelectedItem as DeviceItem; var b = target.SelectedItem as DeviceItem;
            var missing = new[] { a, b }.Where(d => d != null && !IsActive(d.Id)).Select(d => "«" + SpeakerName(d.Name) + "»").ToArray();
            if (missing.Length > 0) status.Text = "Esperando a " + string.Join(" y ", missing) + ". Enciéndelo o conéctalo y AudioDúo volverá a sonar solo (o elige otro en Ajustes).";
        }
        void ConnectByUser() { userStopped = false; autoBlocked = false; lastFailure = false; failureRetries = 0; CloseNoticeIfAny(); StartAudio(); RefreshChrome(); }
        void StopByUser()
        {
            userStopped = true; waitingForDevices = false; StopAudio();
            status.Text = "Detenido. El parlante principal sigue sonando por Windows; pulsa Conectar para volver.";
            RefreshChrome(); RefreshSpeakerOverview();
        }
        void CloseNoticeIfAny() { if (noticePrompt != null && noticePrompt.Visible) CloseNotice(); }

        // Ask once about a speaker AudioDúo has never seen; known speakers just resume with their settings.
        void OfferNewSpeaker(string id)
        {
            if (ignoredSpeakers.Contains(id) || IsReserved(id) || (noticePrompt != null && noticePrompt.Visible)) return;
            var item = AvailableOutputs().FirstOrDefault(d => d.Id == id);
            if (item == null || IsUtilityOutput(item.Name) || ignoredSpeakers.Contains(item.Name)) return;
            var a = source.SelectedItem as DeviceItem;
            if (a != null && a.Offline) return;
            // A saved speaker that was removed and paired again: same name, new ID. Keep its settings.
            var known = extraSpeakers.FirstOrDefault(p => string.Equals(p.Name, item.Name, StringComparison.OrdinalIgnoreCase) && !IsActive(p.Id));
            if (known != null)
            {
                known.Id = id; SaveExtraSpeakers();
                if (running && known.Enabled) { StartExtra(known); PublishExtras(); ApplyListeningGains(); }
                status.Text = "Volvió «" + SpeakerName(item.Name) + "» con su configuración guardada.";
                return;
            }
            string name = SpeakerName(item.Name);
            if (autoAddSpeakers)
            {
                try { AddSpeaker(item); } catch (Exception ex) { status.Text = ex.Message; return; }
                RefreshSpeakerOverview();
                AskInNotice("Se agregó «" + name + "» automáticamente. Ajusta su retraso en su tarjeta o usa Calibrar.", "Entendido", null, delegate
                {
                    var added = extraSpeakers.FirstOrDefault(p => p.Id == id);
                    if (added != null) RemoveSpeaker(added);
                    ignoredSpeakers.Add(id); ignoredSpeakers.Add(item.Name); SaveIgnoredSpeakers();
                    status.Text = "Se quitó «" + name + "». No se volverá a agregar solo."; RefreshSpeakerOverview();
                }, "Deshacer");
                return;
            }
            AskInNotice("Se conectó «" + name + "». ¿Quieres que suene junto con tus parlantes?", "Agregar", delegate
            {
                try { AddSpeaker(item); status.Text = "Agregado «" + name + "». Ajusta su retraso en su tarjeta o usa Calibrar."; }
                catch (Exception ex) { status.Text = ex.Message; }
                RefreshSpeakerOverview();
            }, delegate { ignoredSpeakers.Add(id); ignoredSpeakers.Add(item.Name); SaveIgnoredSpeakers(); });
        }
        // Speakers in «Más parlantes» that are switched off or no longer paired.
        SpeakerProfile[] OfflineExtras() { return extraSpeakers.Where(p => !IsActive(p.Id)).ToArray(); }
        void RemoveOfflineSpeakers()
        {
            var gone = OfflineExtras();
            foreach (var p in gone) RemoveSpeaker(p);
            status.Text = gone.Length == 1 ? "Se quitó «" + SpeakerName(gone[0].Name) + "»." : "Se quitaron " + gone.Length + " parlantes apagados.";
            RefreshSpeakerOverview();
        }
        void LoadIgnoredSpeakers()
        {
            try { if (File.Exists(ignoredFile)) foreach (var line in File.ReadAllLines(ignoredFile)) if (line.Trim().Length > 0) ignoredSpeakers.Add(line.Trim()); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        void SaveIgnoredSpeakers()
        {
            try { File.WriteAllLines(ignoredFile, ignoredSpeakers.ToArray(), Encoding.UTF8); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        // «Audiculares 7.1» is a virtual enhancer without loopback; its sound comes out of the wired output,
        // so that endpoint is the one to watch for waves and levels.
        string MonitorIdFor(string id, string name)
        {
            if (!IsEnhancer(name)) return id;
            var a = source.SelectedItem as DeviceItem;
            var wired = AvailableOutputs().FirstOrDefault(d => d.Name.IndexOf("High Definition Audio", StringComparison.OrdinalIgnoreCase) >= 0 && (a == null || d.Id != a.Id));
            return wired != null ? wired.Id : id;
        }

        // Keeps a saved speaker selected while it is switched off, so its settings are never lost.
        internal static void SelectOrKeep(ComboBox box, string id, string name, string excludeId = null)
        {
            if (string.IsNullOrEmpty(id)) return;
            for (int i = 0; i < box.Items.Count; i++) if (((DeviceItem)box.Items[i]).Id == id) { box.SelectedIndex = i; return; }
            // Removing and pairing a Bluetooth speaker again gives it a new Windows ID: recognise it by name.
            // excludeId: never adopt the device already used by the other role (two identical speakers).
            if (!string.IsNullOrEmpty(name))
                for (int i = 0; i < box.Items.Count; i++)
                {
                    var item = (DeviceItem)box.Items[i];
                    if (!item.Offline && item.Id != excludeId && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) { box.SelectedIndex = i; return; }
                }
            box.Items.Add(new DeviceItem { Id = id, Name = string.IsNullOrEmpty(name) ? "Parlante guardado" : name, Offline = true });
            box.SelectedIndex = box.Items.Count - 1;
        }
    }
}
