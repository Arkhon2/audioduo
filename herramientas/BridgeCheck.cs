using System;
using System.IO;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

class BridgeCheck
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            using (var devices = new MMDeviceEnumerator())
            using (var device = devices.GetDevice(args[0]))
            using (var observed = devices.GetDevice(args[0]))
            using (var capture = new WasapiLoopbackCapture(observed))
            using (var player = new WasapiOut(device, AudioClientShareMode.Shared, false, 50))
            {
                bool wasMuted = device.AudioEndpointVolume.Mute; long nonzero = 0; Exception failure = null;
                try
                {
                    device.AudioEndpointVolume.Mute = true;
                    capture.DataAvailable += delegate(object s, WaveInEventArgs e)
                    {
                        for (int i = 0; i < e.BytesRecorded; i++) if (e.Buffer[i] != 0) { Interlocked.Increment(ref nonzero); break; }
                    };
                    capture.RecordingStopped += delegate(object s, StoppedEventArgs e) { failure = e.Exception; };
                    var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2); var queue = new BufferedWaveProvider(format) { ReadFully = true };
                    byte[] sound = new byte[48000 * 2 * 4];
                    for (int f = 0; f < 48000; f++)
                    {
                        var sample = BitConverter.GetBytes((float)(.1 * Math.Sin(2 * Math.PI * 1200 * f / 48000)));
                        Buffer.BlockCopy(sample, 0, sound, f * 8, 4); Buffer.BlockCopy(sample, 0, sound, f * 8 + 4, 4);
                    }
                    queue.AddSamples(sound, 0, sound.Length); player.Init(queue); capture.StartRecording(); player.Play(); Thread.Sleep(1700); player.Stop(); capture.StopRecording(); capture.Dispose();
                    if (failure != null) throw failure;
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prueba-puente.txt"), "Salida: " + device.FriendlyName + "\r\nPrueba con salida silenciada: " + (nonzero > 0 ? "OK, loopback conserva la señal" : "NO compatible") + "\r\nBloques digitales con señal: " + nonzero + "\r\nSilencio original restaurado: " + wasMuted);
                    return nonzero > 0 ? 0 : 2;
                }
                finally { device.AudioEndpointVolume.Mute = wasMuted; }
            }
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error-puente.txt"), ex.ToString()); return 1; }
    }
}
