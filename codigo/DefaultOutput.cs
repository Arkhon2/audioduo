using System;
using System.Runtime.InteropServices;

namespace AudioDuo
{
    // Sets the Windows default playback device, as the Sound panel's «Predeterminar» button does.
    // Windows often moves the default to the wired output when a Bluetooth speaker reconnects;
    // «Conectar audio» uses this so the user does not have to fix it by hand every day.
    static class DefaultOutput
    {
        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigClient { }

        // Undocumented but stable since Windows 7; the vtable order must be kept exactly.
        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat(string id, IntPtr format);
            [PreserveSig] int GetDeviceFormat(string id, bool isDefault, IntPtr format);
            [PreserveSig] int ResetDeviceFormat(string id);
            [PreserveSig] int SetDeviceFormat(string id, IntPtr endpointFormat, IntPtr mixFormat);
            [PreserveSig] int GetProcessingPeriod(string id, bool isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
            [PreserveSig] int SetProcessingPeriod(string id, IntPtr period);
            [PreserveSig] int GetShareMode(string id, IntPtr mode);
            [PreserveSig] int SetShareMode(string id, IntPtr mode);
            [PreserveSig] int GetPropertyValue(string id, IntPtr key, IntPtr value);
            [PreserveSig] int SetPropertyValue(string id, IntPtr key, IntPtr value);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
            [PreserveSig] int SetEndpointVisibility(string id, bool visible);
        }

        // Console, Multimedia and Communications, so videos, games and calls all use the same speaker.
        public static bool Set(string id)
        {
            object client = null;
            try
            {
                client = new PolicyConfigClient();
                var policy = (IPolicyConfig)client;
                bool ok = true;
                for (int role = 0; role < 3; role++) ok &= policy.SetDefaultEndpoint(id, role) == 0;
                return ok;
            }
            catch (Exception) { return false; }
            finally { if (client != null) Marshal.ReleaseComObject(client); }
        }
    }
}
