using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace AudioDuo
{
    static class Palette
    {
        public static readonly Color Background = Color.FromArgb(12, 19, 31), Card = Color.FromArgb(20, 30, 46),
            Border = Color.FromArgb(40, 55, 75), Text = Color.FromArgb(235, 242, 251), Muted = Color.FromArgb(151, 171, 194),
            Blue = Color.FromArgb(94, 170, 255), Teal = Color.FromArgb(58, 221, 184);
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath(); int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
    }
    // Vertical-only scrolling page. WinForms scrolls to the focused control by default, which shifted
    // the whole window sideways when a card gained focus (cards appeared cut on the left).
    sealed class ScrollHost : Panel
    {
        public ScrollHost() { AutoScroll = true; DoubleBuffered = true; BackColor = Palette.Background; }
        protected override Point ScrollToControl(Control activeControl) { return DisplayRectangle.Location; }
    }
    // Rounded status chip («Conectado», «Esperando», …).
    sealed class Pill : Control
    {
        Color accent = Palette.Muted;
        public Color Accent { get { return accent; } set { accent = value; Invalidate(); } }
        public bool AlignLeft;
        public Pill() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; Font = new Font("Segoe UI", 8.5f, FontStyle.Bold); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var size = TextRenderer.MeasureText(Text, Font);
            int width = Math.Min(Width - 1, size.Width + 30), left = AlignLeft ? 0 : Width - 1 - width;
            var r = new Rectangle(left, 0, width, Height - 1);
            using (var path = Palette.Round(r, Math.Max(1, r.Height / 2)))
            using (var fill = new SolidBrush(Color.FromArgb(38, accent)))
            using (var border = new Pen(Color.FromArgb(110, accent))) { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
            using (var dot = new SolidBrush(accent)) e.Graphics.FillEllipse(dot, left + 10, Height / 2 - 4, 8, 8);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(left + 22, 0, width - 24, Height - 1), accent, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }
    }
    static class Styles
    {
        public static readonly Color Amber = Color.FromArgb(247, 195, 103), Red = Color.FromArgb(255, 138, 118), Hover = Color.FromArgb(38, 53, 73);
        public static void Primary(Button b)
        {
            b.FlatStyle = FlatStyle.Flat; b.UseVisualStyleBackColor = false; b.Cursor = Cursors.Hand;
            b.BackColor = Palette.Teal; b.ForeColor = Palette.Background; b.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            b.FlatAppearance.BorderColor = Palette.Teal; b.FlatAppearance.MouseOverBackColor = Color.FromArgb(94, 238, 204); b.FlatAppearance.MouseDownBackColor = Color.FromArgb(40, 190, 158);
        }
        public static void Secondary(Button b)
        {
            b.FlatStyle = FlatStyle.Flat; b.UseVisualStyleBackColor = false; b.Cursor = Cursors.Hand;
            b.BackColor = Palette.Card; b.ForeColor = Palette.Text; b.Font = new Font("Segoe UI", 9.5f);
            b.FlatAppearance.BorderColor = Palette.Border; b.FlatAppearance.MouseOverBackColor = Hover; b.FlatAppearance.MouseDownBackColor = Palette.Border;
        }
        // Checkbox drawn as a toggle chip; the accent fills it while checked.
        public static CheckBox Chip(string text, Color accent)
        {
            var chip = new CheckBox { Text = text, Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand, BackColor = Palette.Background, ForeColor = Palette.Text, Font = new Font("Segoe UI", 9), UseVisualStyleBackColor = false };
            chip.FlatAppearance.BorderColor = Palette.Border; chip.FlatAppearance.CheckedBackColor = Color.FromArgb(70, accent); chip.FlatAppearance.MouseOverBackColor = Hover;
            chip.CheckedChanged += delegate { chip.FlatAppearance.BorderColor = chip.Checked ? accent : Palette.Border; };
            return chip;
        }
    }
    sealed class Surface : Panel
    {
        public Surface() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); BackColor = Palette.Background; Padding = new Padding(18); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Palette.Round(new Rectangle(0, 0, Width - 1, Height - 1), 14))
            using (var fill = new SolidBrush(Palette.Card))
            using (var border = new Pen(Palette.Border)) { e.Graphics.FillPath(fill, p); e.Graphics.DrawPath(border, p); }
            base.OnPaint(e);
        }
    }
    sealed class Slider : Control
    {
        int value, maximum = 100;
        public int Maximum { get { return maximum; } set { maximum = Math.Max(1, value); Invalidate(); } }
        public int Value { get { return value; } set { int v = Math.Max(0, Math.Min(maximum, value)); if (this.value == v) return; this.value = v; Invalidate(); if (Scroll != null) Scroll(this, EventArgs.Empty); } }
        public int TickFrequency { get; set; }
        public Color Accent = Palette.Blue;
        public event EventHandler Scroll;
        public Slider() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.Slider; SetStyle(ControlStyles.Selectable, true); Height = 28; BackColor = Palette.Card; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float x = 9 + (Width - 18) * Value / (float)maximum;
            using (var rail = new Pen(Palette.Border, 5)) e.Graphics.DrawLine(rail, 9, Height / 2, Width - 9, Height / 2);
            using (var line = new Pen(Enabled ? Accent : Palette.Muted, 5)) e.Graphics.DrawLine(line, 9, Height / 2, x, Height / 2);
            using (var brush = new SolidBrush(Enabled ? Palette.Text : Palette.Muted)) e.Graphics.FillEllipse(brush, x - 6, Height / 2 - 6, 12, 12);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return; Focus(); Capture = true; SetMouse(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) SetMouse(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { Capture = false; base.OnMouseUp(e); }
        void SetMouse(int x) { Value = (int)Math.Round((x - 9) * maximum / (double)Math.Max(1, Width - 18)); }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) { Value -= e.Shift ? 10 : 1; e.Handled = true; }
            if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) { Value += e.Shift ? 10 : 1; e.Handled = true; }
            base.OnKeyDown(e);
        }
    }
    sealed class LevelMeter : Control
    {
        public float Peak;
        public Color Accent = Palette.Blue;
        public LevelMeter() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); BackColor = Palette.Card; Height = 5; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float normalized = Peak <= 0 ? 0 : (float)Math.Max(0, Math.Min(1, (20 * Math.Log10(Peak) + 60) / 60));
            int radius = Math.Max(1, Height / 2);
            using (var track = Palette.Round(new Rectangle(0, 0, Width - 1, Height - 1), radius))
            using (var b = new SolidBrush(Palette.Border)) e.Graphics.FillPath(b, track);
            int fill = (int)((Width - 1) * normalized);
            if (fill >= Height)
                using (var bar = Palette.Round(new Rectangle(0, 0, fill, Height - 1), radius))
                using (var b = new SolidBrush(Peak >= .98 ? Styles.Red : Accent)) e.Graphics.FillPath(b, bar);
        }
    }
    sealed class WaveTrace
    {
        public string Label, State;
        public WaveHistory History;
        public Color Color;
    }
    sealed class WaveView : Control
    {
        List<WaveTrace> traces;
        float[][] frozen;
        int windowMs = 2000;
        float? cursorA, cursorB;
        int dragging;
        public bool Frozen { get { return frozen != null; } }
        public int TraceCount { get { return traces.Count; } }
        public int WindowMs { get { return windowMs; } set { windowMs = value; Resume(); } }
        public WaveView(WaveHistory a, WaveHistory b)
        {
            traces = new List<WaveTrace> { new WaveTrace { Label = "PRINCIPAL", State = "", History = a, Color = Palette.Blue }, new WaveTrace { Label = "COPIA", State = "", History = b, Color = Palette.Teal } };
            DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); BackColor = Palette.Card; Cursor = Cursors.Cross;
            AccessibleName = "Señales digitales de las salidas. Los cursores miden una distancia manual, no la latencia acústica.";
        }
        public void SetTraces(List<WaveTrace> items)
        {
            bool changed = items.Count != traces.Count || items.Where((item, i) => i >= traces.Count || item.History != traces[i].History || item.Label != traces[i].Label).Any();
            traces = items; if (changed) Resume(); Invalidate();
        }
        float[][] Snapshot(double now)
        {
            return traces.Select(t => t.History == null ? null : t.History.Snapshot(now, windowMs)).ToArray();
        }
        public void Freeze()
        {
            double now = WaveHistory.Now - 120;
            frozen = Snapshot(now);
            cursorA = .35f; cursorB = .65f; Invalidate();
        }
        public void Resume() { frozen = null; cursorA = cursorB = null; Invalidate(); }
        public double CursorDelta { get { return cursorA.HasValue && cursorB.HasValue ? (cursorB.Value - cursorA.Value) * windowMs : 0; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int left = 178, right = Width - 15, top = 8, bottom = Height - 40;
            int w = right - left; if (w < 40 || bottom <= top) return;
            double now = WaveHistory.Now - 120;
            var values = frozen ?? Snapshot(now);
            float largest = 0;
            foreach (var signal in values) if (signal != null) foreach (float value in signal) largest = Math.Max(largest, value);
            float visualGain = largest > .0001f ? Math.Min(16, Math.Max(1, .85f / largest)) : 1;
            using (var grid = new Pen(Color.FromArgb(33, 48, 66)))
            using (var f = new Font("Segoe UI", 8))
            {
                for (int i = 0; i <= 8; i++)
                {
                    int x = left + w * i / 8; g.DrawLine(grid, x, top, x, bottom);
                    TextRenderer.DrawText(g, ((i - 8) * windowMs / 8).ToString() + " ms", f, new Point(x - 15, bottom + 4), Palette.Muted);
                }
                int lane = (bottom - top) / Math.Max(1, traces.Count);
                for (int i = 0; i < traces.Count; i++) DrawLane(g, values[i], left, w, top + lane * i + lane / 2, Math.Max(3, lane / 2 - 8), traces[i].Color, traces[i].Label, traces[i].State, grid, visualGain);
                if (Frozen)
                {
                    DrawCursor(g, left + (int)(cursorA.Value * w), top, bottom, Palette.Blue, "A");
                    DrawCursor(g, left + (int)(cursorB.Value * w), top, bottom, Palette.Teal, "B");
                    TextRenderer.DrawText(g, "Arrastra los cursores A y B: distancia " + CursorDelta.ToString("+0;-0;0") + " ms", f, new Point(left, Height - 17), Palette.Text);
                }
                else TextRenderer.DrawText(g, "Escala automática ×" + visualGain.ToString("0.0") + " (solo para verlas mejor, no cambia el volumen) · Congelar permite medir con dos cursores", f, new Point(left, Height - 17), Palette.Muted);
            }
        }
        void DrawLane(Graphics g, float[] samples, int left, int width, int center, int amplitude, Color color, string label, string state, Pen grid, float visualGain)
        {
            g.DrawLine(grid, left, center, left + width, center);
            using (var font = new Font("Segoe UI", 8, FontStyle.Bold)) TextRenderer.DrawText(g, label, font, new Rectangle(4, center - 21, left - 12, 30), color, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using (var font = new Font("Segoe UI", 7)) TextRenderer.DrawText(g, state ?? "", font, new Rectangle(4, center + 9, left - 12, 17), Palette.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (samples == null) { TextRenderer.DrawText(g, state ?? "Sin monitoreo", Font, new Point(left + 12, center - 9), Palette.Muted); return; }
            if (!samples.Any(v => v > .00001f)) { TextRenderer.DrawText(g, "Sin sonido en este momento", Font, new Point(left + 12, center - 9), Palette.Muted); return; }
            using (var pen = new Pen(color, 1.6f))
                for (int x = 0; x < width; x++)
                {
                    int start = x * samples.Length / width, end = Math.Max(start + 1, (x + 1) * samples.Length / width);
                    float peak = 0; for (int j = start; j < end && j < samples.Length; j++) peak = Math.Max(peak, samples[j]);
                    float height = Math.Min(1, peak * visualGain) * amplitude;
                    if (height > .1) g.DrawLine(pen, left + x, center - height, left + x, center + height);
                }
        }
        void DrawCursor(Graphics g, int x, int top, int bottom, Color c, string label)
        {
            using (var pen = new Pen(c) { DashStyle = DashStyle.Dash }) g.DrawLine(pen, x, top, x, bottom);
            TextRenderer.DrawText(g, label, Font, new Point(x + 4, top), c);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (!Frozen || e.Button != MouseButtons.Left) return;
            float x = Math.Max(0, Math.Min(1, (e.X - 178f) / Math.Max(1, Width - 193)));
            dragging = Math.Abs(x - cursorA.Value) <= Math.Abs(x - cursorB.Value) ? 1 : 2;
            Capture = true; MoveCursor(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture && Frozen) MoveCursor(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = 0; Capture = false; base.OnMouseUp(e); }
        void MoveCursor(int x) { float value = Math.Max(0, Math.Min(1, (x - 178f) / Math.Max(1, Width - 193))); if (dragging == 1) cursorA = value; else cursorB = value; Invalidate(); }
    }
}
