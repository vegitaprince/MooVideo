using System.Drawing.Drawing2D;

namespace MooVideo;

sealed class InkColorPicker : Form
{
    readonly ColorWheel wheel;

    internal Color SelectedColor => wheel.SelectedColor;
    internal bool PickFromVideo { get; private set; }

    internal InkColorPicker(Color initial)
    {
        Text = "그림 색상";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        ClientSize = new Size(268, 326);
        AutoScaleMode = AutoScaleMode.Dpi;

        wheel = new ColorWheel(initial) { Bounds = new Rectangle(28, 14, 212, 212) };
        var brightnessLabel = Theme.Label("밝기", 9);
        brightnessLabel.SetBounds(16, 239, 44, 25);
        var brightness = new TrackBar
        {
            Minimum = 0, Maximum = 100, Value = (int)Math.Round(wheel.Brightness * 100),
            TickStyle = TickStyle.None, AutoSize = false, AccessibleName = "색상 밝기",
            Bounds = new Rectangle(58, 235, 143, 32)
        };
        var swatch = Theme.Label("", 9);
        swatch.SetBounds(214, 233, 38, 38);
        swatch.Paint += (_, e) => { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; DrawSwatch(e.Graphics, swatch.ClientRectangle, wheel.SelectedColor, 27); };
        wheel.ColorChanged += () => swatch.Invalidate();
        brightness.ValueChanged += (_, _) => wheel.SetBrightness(brightness.Value / 100f);
        brightness.MouseWheel += (_, e) => { if (e is HandledMouseEventArgs handled) handled.Handled = true; brightness.Value = Math.Clamp(brightness.Value + Math.Sign(e.Delta) * 5, 0, 100); };

        var eyedropper = new Button { Text = "스포이드", Bounds = new Rectangle(16, 282, 76, 32) };
        eyedropper.Click += (_, _) => { PickFromVideo = true; DialogResult = DialogResult.OK; };
        var ok = new Button { Text = "적용", DialogResult = DialogResult.OK, Bounds = new Rectangle(98, 282, 74, 32) };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(178, 282, 74, 32) };
        foreach (var button in new[] { eyedropper, ok, cancel })
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Theme.Field;
            button.ForeColor = Theme.Text;
        }
        Controls.AddRange([wheel, brightnessLabel, brightness, swatch, eyedropper, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    internal static void DrawSwatch(Graphics g, Rectangle area, Color color, int size)
    {
        var circle = new Rectangle((area.Width - size) / 2, (area.Height - size) / 2, size, size);
        using var fill = new SolidBrush(color);
        using var outline = new Pen(Theme.Muted);
        g.FillEllipse(fill, circle);
        g.DrawEllipse(outline, circle);
    }

    sealed class ColorWheel : Control
    {
        const int TextureSize = 212;
        float hue, saturation;
        Bitmap? texture;
        internal float Brightness { get; private set; }
        internal Color SelectedColor => FromHsv(hue, saturation, Brightness);
        internal event Action? ColorChanged;

        internal ColorWheel(Color initial)
        {
            hue = initial.GetHue();
            Brightness = Math.Max(initial.R, Math.Max(initial.G, initial.B)) / 255f;
            saturation = Brightness == 0 ? 0 : 1 - Math.Min(initial.R, Math.Min(initial.G, initial.B)) / (255f * Brightness);
            AccessibleName = "색상환";
            AccessibleRole = AccessibleRole.Graphic;
            Cursor = Cursors.Cross;
            BackColor = Theme.Panel;
            TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.Selectable, true);
        }

        internal void SetBrightness(float value)
        {
            Brightness = value;
            texture?.Dispose();
            texture = null;
            NotifyColorChanged();
        }

        void NotifyColorChanged() { Invalidate(); ColorChanged?.Invoke(); }
        float Radius => Math.Max(1, Math.Min(Width, Height) / 2f - 6);

        protected override void OnPaint(PaintEventArgs e)
        {
            texture ??= MakeTexture();
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float radius = Radius;
            var center = new PointF(Width / 2f, Height / 2f);
            g.DrawImage(texture, new RectangleF(center.X - radius, center.Y - radius, 2 * radius, 2 * radius));
            float angle = hue * MathF.PI / 180;
            float x = center.X + MathF.Cos(angle) * radius * saturation;
            float y = center.Y + MathF.Sin(angle) * radius * saturation;
            using var dark = new Pen(Color.Black, 4);
            using var light = new Pen(Color.White, 2);
            g.DrawEllipse(dark, x - 5, y - 5, 10, 10);
            g.DrawEllipse(light, x - 5, y - 5, 10, 10);
            if (Focused) ControlPaint.DrawFocusRectangle(g, ClientRectangle);
        }

        Bitmap MakeTexture()
        {
            var result = new Bitmap(TextureSize, TextureSize);
            float radius = TextureSize / 2f;
            for (int y = 0; y < TextureSize; y++)
            for (int x = 0; x < TextureSize; x++)
            {
                float dx = x + .5f - radius, dy = y + .5f - radius;
                float distance = MathF.Sqrt(dx * dx + dy * dy) / radius;
                if (distance <= 1)
                    result.SetPixel(x, y, FromHsv(Angle(dx, dy), distance, Brightness));
            }
            return result;
        }

        void Pick(Point point, bool insideOnly)
        {
            float dx = point.X - Width / 2f, dy = point.Y - Height / 2f;
            float distance = MathF.Sqrt(dx * dx + dy * dy) / Radius;
            if (insideOnly && distance > 1) return;
            hue = Angle(dx, dy);
            saturation = Math.Clamp(distance, 0, 1);
            NotifyColorChanged();
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); Pick(e.Location, true); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) Pick(e.Location, false); }
        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Left: hue = (hue + 355) % 360; break;
                case Keys.Right: hue = (hue + 5) % 360; break;
                case Keys.Up: saturation = Math.Min(1, saturation + .05f); break;
                case Keys.Down: saturation = Math.Max(0, saturation - .05f); break;
                default: return;
            }
            e.Handled = true;
            NotifyColorChanged();
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void Dispose(bool disposing) { if (disposing) texture?.Dispose(); base.Dispose(disposing); }

        static float Angle(float dx, float dy) => (MathF.Atan2(dy, dx) * 180 / MathF.PI + 360) % 360;

        static Color FromHsv(float hue, float saturation, float value)
        {
            float chroma = value * saturation;
            float sector = hue / 60;
            float blend = chroma * (1 - Math.Abs(sector % 2 - 1));
            float baseValue = value - chroma;
            var (r, g, b) = (int)sector switch
            {
                0 => (chroma, blend, 0f), 1 => (blend, chroma, 0f), 2 => (0f, chroma, blend),
                3 => (0f, blend, chroma), 4 => (blend, 0f, chroma), _ => (chroma, 0f, blend)
            };
            int Channel(float component) => Math.Clamp((int)Math.Round((component + baseValue) * 255), 0, 255);
            return Color.FromArgb(Channel(r), Channel(g), Channel(b));
        }
    }
}
