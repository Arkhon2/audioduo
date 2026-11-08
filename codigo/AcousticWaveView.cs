using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace AudioDuo
{
    sealed class AcousticWaveView : Control
    {
        internal AcousticPlan Measurement;
        internal bool Stale;
        internal static float[] Envelope(float[] samples, double referenceMs)
        {
            var peaks = new float[360];
            for (int bin = 0; bin < peaks.Length; bin++)
                for (int j = 0; j < 40; j++)
                {
                    int index = (int)Math.Round(referenceMs * 8) + bin * 40 + j;
                    if (index >= 0 && index < samples.Length) peaks[bin] = Math.Max(peaks[bin], Math.Abs(samples[index]));
                }
            return peaks;
        }
        public AcousticWaveView() { DoubleBuffered = true; BackColor = Palette.Card; SetStyle(ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics;
            var rows = Measurement == null ? new AcousticSpeaker[0] : Measurement.Speakers.Where(s => s.AcousticEnvelope != null).ToArray();
            if (rows.Length == 0)
            {
                TextRenderer.DrawText(g, "Conecta los tres parlantes y usa Calibrar con micrófono.\nAquí aparecerán las ondas reales de la última prueba.", Font, new Rectangle(20, 30, Width - 40, Height - 40), Palette.Muted, TextFormatFlags.WordBreak); return;
            }
            int left = 210, width = Width - left - 20, top = 32, bottom = Height - 38;
            if (width < 40 || bottom <= top) return;
            TextRenderer.DrawText(g, Stale ? "AJUSTES CAMBIADOS · repite la medición para comprobarlos" : "ÚLTIMA MEDICIÓN · tres pruebas separadas, alineadas al envío del tono", Font, new Point(4, 3), Stale ? Color.FromArgb(247,195,103) : Palette.Muted);
            double fastest = rows.Min(s => s.HeardMs);
            using (var grid = new Pen(Palette.Border))
            using (var font = new Font("Segoe UI", 8))
            {
                for (int ms = 0; ms <= 1800; ms += 300)
                {
                    int x = left + width * ms / 1800; g.DrawLine(grid, x, top, x, bottom);
                    TextRenderer.DrawText(g, ms + " ms", font, new Point(x - 10, bottom + 6), Palette.Muted);
                }
                for (int i = 0; i < rows.Length; i++)
                {
                    var row = rows[i]; int lane = (bottom - top) / rows.Length, y = top + lane * i + lane / 2;
                    Color color = i == 0 ? Palette.Blue : i == 1 ? Palette.Teal : Color.FromArgb(247,195,103);
                    string name = row.Name.Replace("Auriculares (", "Bluetooth · ").TrimEnd(')');
                    TextRenderer.DrawText(g, name, font, new Rectangle(3, y - 24, left - 12, 25), color, TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(g, "Llegó a " + row.HeardMs.ToString("0") + " ms · Δ " + (row.HeardMs - fastest).ToString("0") + " ms", font, new Point(3, y + 2), Palette.Text);
                    // Scale each row by the detected tone, not by noise or an echo elsewhere in the window.
                    int arrivalBin = (int)(row.HeardMs / 5);
                    float max = row.AcousticEnvelope.Where((v, j) => Math.Abs(j - arrivalBin) <= 16).DefaultIfEmpty(0).Max();
                    if (max <= 0) max = row.AcousticEnvelope.Max();
                    g.DrawLine(grid, left, y, left + width, y);
                    using (var pen = new Pen(color, 1.5f))
                    {
                        for (int j = 0; j < row.AcousticEnvelope.Length; j++)
                        {
                            int x = left + width * j / row.AcousticEnvelope.Length;
                            float amplitude = max > 0 ? Math.Min(1, row.AcousticEnvelope[j] / max) * Math.Max(2, lane / 2 - 12) : 0;
                            if (amplitude > .2) g.DrawLine(pen, x, y - amplitude, x, y + amplitude);
                        }
                        int arrival = left + (int)(width * row.HeardMs / 1800); g.DrawLine(pen, arrival, y - lane / 2 + 4, arrival, y + lane / 2 - 4);
                    }
                }
                TextRenderer.DrawText(g, "Onda del primer tono · llegada: mediana de 3 tonos · Δ frente al primero · amplitud normalizada", font, new Point(4, Height - 16), Palette.Muted);
            }
        }
    }
    sealed partial class MainForm
    {
        AcousticWaveView acousticView;
        CheckBox acousticToggle;
        void MarkAcousticStale() { if (acousticView != null) { acousticView.Stale = true; acousticView.Invalidate(); } }
        void ShowAcousticMeasurement(AcousticPlan plan)
        {
            if (acousticView == null || !plan.Speakers.Any(s => s.AcousticEnvelope != null)) return;
            acousticView.Measurement = plan; acousticView.Stale = false; acousticToggle.Checked = true; acousticView.Invalidate();
        }
    }
}
