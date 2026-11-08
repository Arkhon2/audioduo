using System;
using System.Windows.Forms;

namespace AudioDuo
{
    sealed partial class MainForm
    {
        CheckBox bridgeToggle;
        bool usingBridge, restoreSourceMute, savedSourceMute;
        string BridgeStateText()
        {
            if (!running || sourceDevice == null) return "Origen · puente al conectar";
            try { return sourceDevice.AudioEndpointVolume.Mute ? "Puente digital · silenciado" : "Puente audible · sin retraso"; }
            catch (Exception) { return "Estado del puente no disponible"; }
        }
        void SilenceBridge()
        {
            if (!usingBridge) return;
            savedSourceMute = sourceDevice.AudioEndpointVolume.Mute;
            sourceDevice.AudioEndpointVolume.Mute = true;
            restoreSourceMute = true;
        }
        void RestoreBridge()
        {
            if (!restoreSourceMute) return;
            restoreSourceMute = false;
            try { if (sourceDevice != null) sourceDevice.AudioEndpointVolume.Mute = savedSourceMute; } catch (Exception) { }
        }
    }
}
