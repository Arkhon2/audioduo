using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

class CheckMicrophone
{
    [STAThread]
    static int Main(string[] args)
    {
        string folder = AppDomain.CurrentDomain.BaseDirectory;
        try
        {
            using (var devices = new MMDeviceEnumerator())
            {
                var lines = new List<string>();
                foreach (var input in devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                {
                    lines.Add(input.ID + " | " + input.FriendlyName + " | Mute=" + input.AudioEndpointVolume.Mute + " | Volumen=" + (int)(input.AudioEndpointVolume.MasterVolumeLevelScalar * 100)); input.Dispose();
                }
                File.WriteAllLines(Path.Combine(folder, "entradas.txt"), lines);
                if (args.Length == 0) return 0;
                using (var mic = devices.GetDevice(args[0]))
                using (var capture = new WasapiCapture(mic))
                {
                    double peak = 0, sum = 0; long samples = 0; Exception failure = null;
                    capture.DataAvailable += delegate(object sender, WaveInEventArgs e)
                    {
                        var format = capture.WaveFormat;
                        int bytes = format.BitsPerSample / 8;
                        for (int i = 0; i + bytes <= e.BytesRecorded; i += bytes)
                        {
                            double v;
                            if (format.Encoding == WaveFormatEncoding.IeeeFloat && bytes == 4) v = BitConverter.ToSingle(e.Buffer, i);
                            else if (format.Encoding == WaveFormatEncoding.Pcm && bytes == 2) v = BitConverter.ToInt16(e.Buffer, i) / 32768.0;
                            else if (format.Encoding == WaveFormatEncoding.Pcm && bytes == 4) v = BitConverter.ToInt32(e.Buffer, i) / 2147483648.0;
                            else if (format.Encoding == WaveFormatEncoding.Pcm && bytes == 3)
                            {
                                int n = e.Buffer[i] | e.Buffer[i + 1] << 8 | e.Buffer[i + 2] << 16; if ((n & 0x800000) != 0) n |= unchecked((int)0xff000000); v = n / 8388608.0;
                            }
                            else throw new NotSupportedException("Formato de entrada no compatible: " + format);
                            if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                            peak = Math.Max(peak, Math.Abs(v)); sum += v * v; samples++;
                        }
                    };
                    capture.RecordingStopped += delegate(object sender, StoppedEventArgs e) { failure = e.Exception; };
                    bool muted = mic.AudioEndpointVolume.Mute;
                    capture.StartRecording(); Thread.Sleep(6000); capture.StopRecording(); capture.Dispose();
                    if (failure != null) throw failure;
                    File.WriteAllText(Path.Combine(folder, "prueba-microfono.txt"), "Microfono: " + mic.FriendlyName + "\r\nSilenciado en Windows: " + muted + "\r\nVolumen de entrada: " + (int)(mic.AudioEndpointVolume.MasterVolumeLevelScalar * 100) + "%\r\nMuestras: " + samples + "\r\nPico: " + peak.ToString("0.000000") + " (" + (peak > 0 ? (20 * Math.Log10(peak)).ToString("0.0") : "-infinito") + " dBFS)\r\nRMS: " + (samples > 0 ? Math.Sqrt(sum / samples) : 0).ToString("0.000000") + "\r\nAudio usado solo en memoria, sin grabar archivos.");
                }
            }
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(folder, "error-microfono.txt"), ex.ToString()); return 1; }
    }
}
