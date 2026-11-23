using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.ComponentModel;
namespace MooVideo;

enum DrawingTool { Pen, Eraser, Text, Arrow }

sealed class InkCanvas : Control
{
    sealed record StrokePath(DrawingTool Tool, Color Color, int Size, List<PointF> Points);
    sealed record TextBoxInk(string Text, Color Color, PointF Origin, int FontSize);
    static readonly float[] PenWidths = [2, 4.5f, 9];
    static readonly float[] EraserWidths = [8, 22, 45];
    const int UndoLimit = 100;
    readonly List<Action> undo = [];
    List<StrokePath> strokes = [];
    List<TextBoxInk> labels = [];
    Bitmap? ink;
    StrokePath? pending;
    PointF? pointerStart;
    PointF? pointerEnd;
    PointF textOffset;
    int selectedText = -1;
    TextBoxInk? textBeforeDrag;
    SizeF? textResizeStart;
    int? preferredTextSize;
    internal const int MinTextSize = 8;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color PenColor { get; set; } = Color.Red;
    internal DrawingTool Tool { get; private set; }
    internal bool Erasing => Tool == DrawingTool.Eraser;
    internal bool Editing;
    internal int PenSize = 1, EraserSize = 1;
    internal bool HasInk => strokes.Count != 0 || labels.Count != 0;
    internal bool Drawing => pointerStart != null;
    internal bool CanUndo => undo.Count > 0;
    internal int MaxTextSize => ink == null ? 512 : Math.Clamp((int)(Math.Min(ink.Width, ink.Height) * .8f), MinTextSize, 512);
    internal int DefaultTextSize => Math.Clamp(preferredTextSize ??
        (ink == null ? 23 : (int)Math.Round(Math.Min(ink.Width, ink.Height * 1.78f) * .036f)), MinTextSize, MaxTextSize);
    internal int TextSize => selectedText >= 0 ? labels[selectedText].FontSize : DefaultTextSize;
    internal event Action? HistoryChanged;
    internal event Action? TextSizeChanged;
    internal event Action<PointF>? TextRequested;
    internal event Action<PointF>? ColorPickRequested;
    internal bool PickingColor { get; private set; }

    internal InkCanvas() { Visible = false; TabStop = false; Cursor = Cursors.Cross; }
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x80000; return p; } }

    internal static float WidthAt640(DrawingTool tool, int size) => (tool == DrawingTool.Eraser ? EraserWidths : PenWidths)[Math.Clamp(size, 0, 2)];

    internal void Prepare(Size frame)
    {
        if (ink != null) return;
        ink = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppPArgb);
        Render();
    }

    internal void ClearInk()
    {
        FinishGesture();
        selectedText = -1;
        strokes.Clear();
        labels.Clear();
        undo.Clear();
        ink?.Dispose();
        ink = null;
        Render();
        Visible = false;
        TextSizeChanged?.Invoke();
        HistoryChanged?.Invoke();
    }

    internal void ClearDrawing()
    {
        FinishGesture();
        if (!HasInk) return;
        var savedStrokes = strokes;
        var savedLabels = labels;
        RecordUndo(() => { strokes = savedStrokes; labels = savedLabels; });
        strokes = [];
        labels = [];
        selectedText = -1;
        TextSizeChanged?.Invoke();
        RepaintStrokes();
        Render();
    }

    internal void Undo()
    {
        FinishGesture();
        if (!CanUndo) return;
        var revert = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        revert();
        selectedText = -1;
        TextSizeChanged?.Invoke();
        RepaintStrokes();
        Render();
        HistoryChanged?.Invoke();
    }

    void RecordUndo(Action revert)
    {
        if (undo.Count == UndoLimit) undo.RemoveAt(0);
        undo.Add(revert);
        HistoryChanged?.Invoke();
    }

    internal void SelectTool(DrawingTool tool)
    {
        FinishGesture();
        Tool = tool;
        if (tool != DrawingTool.Text) selectedText = -1;
        Cursor = tool == DrawingTool.Text ? Cursors.IBeam : Cursors.Cross;
        Render();
    }

    internal void SetColorPicking(bool enabled)
    {
        FinishGesture();
        PickingColor = enabled;
        Cursor = enabled || Tool != DrawingTool.Text ? Cursors.Cross : Cursors.IBeam;
    }

    internal void AddText(string text, PointF origin, int fontSize)
    {
        if (ink == null || string.IsNullOrWhiteSpace(text)) return;
        preferredTextSize = Math.Clamp(fontSize, MinTextSize, MaxTextSize);
        var label = new TextBoxInk(text, PenColor, origin, preferredTextSize.Value);
        using (var g = Graphics.FromImage(ink))
            labels.Add(FitText(g, label));
        selectedText = labels.Count - 1;
        RecordUndo(() => labels.RemoveAt(labels.Count - 1));
        TextSizeChanged?.Invoke();
        Render();
    }

    internal void SetTextSize(int fontSize)
    {
        if (ink == null) return;
        fontSize = Math.Clamp(fontSize, MinTextSize, MaxTextSize);
        if (selectedText < 0) preferredTextSize = fontSize;
        else
        {
            int index = selectedText;
            var original = labels[index];
            if (fontSize == original.FontSize) return;
            using var g = Graphics.FromImage(ink);
            labels[index] = FitText(g, original with { FontSize = fontSize });
            RecordUndo(() => labels[index] = original);
        }
        TextSizeChanged?.Invoke();
        Render();
    }

    void CommitStroke(StrokePath path)
    {
        strokes.Add(path);
        RecordUndo(() => strokes.RemoveAt(strokes.Count - 1));
    }

    internal void Stroke(PointF from, PointF to)
    {
        if (ink == null) return;
        var path = new StrokePath(Tool, PenColor, Erasing ? EraserSize : PenSize, [from, to]);
        PaintSegment(path, from, to);
        CommitStroke(path);
        Render();
    }

    PointF ToFrame(PointF normalized) => new(normalized.X * ink!.Width, normalized.Y * ink.Height);
    PointF Normalize(Point screen) => new(Math.Clamp((float)screen.X / Math.Max(1, Width), 0, 1), Math.Clamp((float)screen.Y / Math.Max(1, Height), 0, 1));
    float StrokeWidth(StrokePath path) => Math.Max(1, ink!.Width * WidthAt640(path.Tool, path.Size) / 640f);

    void PaintSegment(StrokePath path, PointF from, PointF to)
    {
        using var g = Graphics.FromImage(ink!);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        DrawSegment(g, path, from, to);
    }

    void DrawSegment(Graphics g, StrokePath path, PointF from, PointF to)
    {
        var a = ToFrame(from);
        var b = ToFrame(to);
        float width = StrokeWidth(path);
        bool erase = path.Tool == DrawingTool.Eraser;
        g.CompositingMode = erase ? CompositingMode.SourceCopy : CompositingMode.SourceOver;
        var color = erase ? Color.Transparent : path.Color;
        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (a == b)
        {
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, a.X - width / 2, a.Y - width / 2, width, width);
        }
        else g.DrawLine(pen, a, b);
    }

    void DrawArrow(Graphics g, StrokePath path, PointF start, PointF end)
    {
        var a = ToFrame(start);
        var b = ToFrame(end);
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1) return;
        float width = StrokeWidth(path);
        float head = Math.Min(length, Math.Max(width * 3.5f, ink!.Width * .021f));
        var basePoint = new PointF(b.X - dx / length * head, b.Y - dy / length * head);
        float offsetX = dy / length * head * .46f, offsetY = dx / length * head * .46f;
        using var pen = new Pen(path.Color, width) { StartCap = LineCap.Round };
        using var brush = new SolidBrush(path.Color);
        g.DrawLine(pen, a, basePoint);
        g.FillPolygon(brush, [b, new PointF(basePoint.X + offsetX, basePoint.Y - offsetY), new PointF(basePoint.X - offsetX, basePoint.Y + offsetY)]);
    }

    void RepaintStrokes()
    {
        if (ink == null) return;
        using var g = Graphics.FromImage(ink);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var path in strokes)
        {
            if (path.Tool == DrawingTool.Arrow)
            {
                g.CompositingMode = CompositingMode.SourceOver;
                DrawArrow(g, path, path.Points[0], path.Points[^1]);
                continue;
            }
            DrawSegment(g, path, path.Points[0], path.Points[0]);
            for (int i = 1; i < path.Points.Count; i++)
                DrawSegment(g, path, path.Points[i - 1], path.Points[i]);
        }
    }

    static Font TextFont(TextBoxInk label) => new("Segoe UI", label.FontSize, FontStyle.Bold, GraphicsUnit.Pixel);
    RectangleF TextBounds(Graphics g, TextBoxInk label, Font font)
    {
        var origin = ToFrame(label.Origin);
        var limit = new SizeF(Math.Max(1, ink!.Width * .8f), Math.Max(1, ink.Height - 4));
        var size = g.MeasureString(label.Text, font, limit);
        return new RectangleF(origin, new SizeF(Math.Min(limit.Width, size.Width) + 2, Math.Min(limit.Height, size.Height) + 2));
    }

    PointF ClampTextOrigin(PointF origin, SizeF textSize) => new(
        Math.Clamp(origin.X, 0, Math.Max(0, 1 - textSize.Width / ink!.Width)),
        Math.Clamp(origin.Y, 0, Math.Max(0, 1 - textSize.Height / ink.Height)));

    TextBoxInk FitText(Graphics g, TextBoxInk label)
    {
        using var font = TextFont(label);
        return label with { Origin = ClampTextOrigin(label.Origin, TextBounds(g, label, font).Size) };
    }

    int FindText(Graphics g, PointF framePoint)
    {
        for (int i = labels.Count - 1; i >= 0; i--)
        {
            using var font = TextFont(labels[i]);
            if (TextBounds(g, labels[i], font).Contains(framePoint)) return i;
        }
        return -1;
    }

    RectangleF ResizeHandle(RectangleF bounds, float radius)
    {
        float rx = Math.Min(ink!.Width / 2f, radius * ink.Width / Math.Max(1, Width));
        float ry = Math.Min(ink.Height / 2f, radius * ink.Height / Math.Max(1, Height));
        // Keep the handle center clear of the viewer's window-resize edges.
        float edgeX = Math.Min(ink.Width / 2f, 12f * ink.Width / Math.Max(1, Width));
        float edgeY = Math.Min(ink.Height / 2f, 12f * ink.Height / Math.Max(1, Height));
        float x = Math.Clamp(bounds.Right - rx, rx, ink.Width - edgeX);
        float y = Math.Clamp(bounds.Bottom - ry, ry, ink.Height - edgeY);
        return new RectangleF(x - rx, y - ry, 2 * rx, 2 * ry);
    }

    RectangleF? ResizeTarget(Graphics g, PointF framePoint)
    {
        if (selectedText < 0) return null;
        var label = labels[selectedText];
        using var font = TextFont(label);
        var bounds = TextBounds(g, label, font);
        var hit = ResizeHandle(bounds, 4);
        hit.Inflate(4f * ink!.Width / Math.Max(1, Width), 4f * ink.Height / Math.Max(1, Height));
        return hit.Contains(framePoint) ? bounds : null;
    }

    void DrawTextLabels(Graphics g, bool showSelection)
    {
        if (labels.Count == 0) return;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        for (int i = 0; i < labels.Count; i++)
        {
            var label = labels[i];
            using var font = TextFont(label);
            var bounds = TextBounds(g, label, font);
            using var shadow = new SolidBrush(label.Color.GetBrightness() > .4 ? Color.FromArgb(220, Color.Black) : Color.FromArgb(220, Color.White));
            using var fill = new SolidBrush(label.Color);
            var shadowBounds = bounds;
            shadowBounds.Offset(1.5f, 1.5f);
            g.DrawString(label.Text, font, shadow, shadowBounds);
            g.DrawString(label.Text, font, fill, bounds);
            if (showSelection && selectedText == i)
            {
                float lineWidth = ink!.Width / (float)Math.Max(1, Width);
                using var edge = new Pen(Color.Black, lineWidth * 2);
                using var border = new Pen(Color.White, lineWidth) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(edge, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                g.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                var handle = ResizeHandle(bounds, 4);
                g.FillRectangle(Brushes.White, handle);
                g.DrawRectangle(edge, handle.X, handle.Y, handle.Width, handle.Height);
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Editing || ink == null) return;
        var point = Normalize(e.Location);
        if (PickingColor)
        {
            ColorPickRequested?.Invoke(point);
            return;
        }
        Capture = true;
        pointerStart = pointerEnd = point;
        if (Tool == DrawingTool.Text)
        {
            using var g = Graphics.FromImage(ink);
            var framePoint = ToFrame(point);
            var target = ResizeTarget(g, framePoint);
            textResizeStart = target?.Size;
            if (target == null) selectedText = FindText(g, framePoint);
            if (selectedText >= 0)
            {
                textBeforeDrag = labels[selectedText];
                var origin = textBeforeDrag.Origin;
                textOffset = new PointF(point.X - origin.X, point.Y - origin.Y);
            }
            Cursor = target != null ? Cursors.SizeNWSE : selectedText >= 0 ? Cursors.SizeAll : Cursors.IBeam;
            TextSizeChanged?.Invoke();
        }
        else
        {
            pending = new StrokePath(Tool, PenColor, Erasing ? EraserSize : PenSize, [point]);
            if (Tool != DrawingTool.Arrow) PaintSegment(pending, point, point);
        }
        Render();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Editing || ink == null || PickingColor) return;
        var point = Normalize(e.Location);
        if (pointerStart == null)
        {
            if (Tool == DrawingTool.Text)
            {
                using var g = Graphics.FromImage(ink);
                var framePoint = ToFrame(point);
                Cursor = ResizeTarget(g, framePoint) != null ? Cursors.SizeNWSE :
                    FindText(g, framePoint) >= 0 ? Cursors.SizeAll : Cursors.IBeam;
            }
            return;
        }
        MoveGesture(point);
    }

    void MoveGesture(PointF point)
    {
        if (pointerEnd == point) return;
        pointerEnd = point;
        if (textBeforeDrag is { } original && selectedText >= 0)
        {
            using var g = Graphics.FromImage(ink!);
            if (textResizeStart is SizeF size)
            {
                var start = pointerStart!.Value;
                var movement = ToFrame(new PointF(point.X - start.X, point.Y - start.Y));
                float scale = 1 + (movement.X * size.Width + movement.Y * size.Height) /
                    (size.Width * size.Width + size.Height * size.Height);
                int fontSize = Math.Clamp((int)Math.Round(original.FontSize * scale), MinTextSize, MaxTextSize);
                if (fontSize == labels[selectedText].FontSize) return;
                labels[selectedText] = FitText(g, original with { FontSize = fontSize });
                TextSizeChanged?.Invoke();
            }
            else
            {
                var origin = new PointF(point.X - textOffset.X, point.Y - textOffset.Y);
                labels[selectedText] = FitText(g, original with { Origin = origin });
            }
        }
        else if (pending is { Tool: not DrawingTool.Arrow } path)
        {
            PaintSegment(path, path.Points[^1], point);
            path.Points.Add(point);
        }
        Render();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || pointerStart is not PointF start) return;
        var end = Normalize(e.Location);
        MoveGesture(end);
        bool addText = Tool == DrawingTool.Text && textBeforeDrag == null &&
            Math.Abs(start.X - end.X) * Width < 4 && Math.Abs(start.Y - end.Y) * Height < 4;
        FinishGesture();
        if (addText) TextRequested?.Invoke(end);
    }

    void FinishGesture()
    {
        if (pointerStart == null) return;
        var end = pointerEnd;
        pointerStart = pointerEnd = null;
        Capture = false;
        if (pending is StrokePath path)
        {
            if (path.Tool != DrawingTool.Arrow) CommitStroke(path);
            else if (end is PointF tip && tip != path.Points[0])
            {
                path.Points.Add(tip);
                using var g = Graphics.FromImage(ink!);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                DrawArrow(g, path, path.Points[0], tip);
                CommitStroke(path);
            }
            pending = null;
        }
        if (textBeforeDrag is TextBoxInk original && selectedText >= 0)
        {
            int index = selectedText;
            if (labels[index] != original) RecordUndo(() => labels[index] = original);
        }
        textBeforeDrag = null;
        textResizeStart = null;
        Render();
    }

    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) FinishGesture(); }
    protected override void WndProc(ref Message m) { if (m.Msg == 0x84 && !Editing) { m.Result = (IntPtr)(-1); return; } base.WndProc(ref m); }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Render(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Render(); }

    internal void Composite(Graphics g, Size size) => DrawContent(g, size, false);
    void DrawContent(Graphics g, Size size, bool preview)
    {
        if (ink == null) return;
        g.DrawImage(ink, new Rectangle(Point.Empty, size));
        var saved = g.Save();
        g.ScaleTransform((float)size.Width / ink.Width, (float)size.Height / ink.Height);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (preview && pending is { Tool: DrawingTool.Arrow } path && pointerEnd is PointF end)
            DrawArrow(g, path, path.Points[0], end);
        DrawTextLabels(g, preview && Editing);
        g.Restore(saved);
    }

    internal void Render()
    {
        if (!IsHandleCreated || Width < 1 || Height < 1) return;
        using var surface = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(surface))
        {
            g.Clear(Color.FromArgb(1, 0, 0, 0));
            DrawContent(g, Size, true);
        }
        var dc = CreateCompatibleDC(IntPtr.Zero);
        var bitmap = surface.GetHbitmap(Color.FromArgb(0));
        var old = SelectObject(dc, bitmap);
        try
        {
            var size = Size;
            var origin = new Point();
            var blend = new Blend { Alpha = 255, Format = 1 };
            if (!UpdateLayeredWindow(Handle, IntPtr.Zero, IntPtr.Zero, ref size, dc, ref origin, 0, ref blend, 2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { SelectObject(dc, old); DeleteObject(bitmap); DeleteDC(dc); }
    }

    protected override void Dispose(bool disposing) { if (disposing) ink?.Dispose(); base.Dispose(disposing); }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Blend { public byte Operation, Flags, Alpha, Format; }
    [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, IntPtr point, ref Size size, IntPtr src, ref Point origin, uint key, ref Blend blend, uint flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
}

public sealed partial class PlayerForm
{
    readonly InkCanvas inkCanvas=new();
    readonly Panel inkTools=new(){BackColor=Theme.Panel,Visible=false};
    ToolButton? inkButton, inkColorButton, inkWidthButton, inkUndoButton;
    readonly Dictionary<DrawingTool, ToolButton> drawingToolButtons = [];
    static readonly string[] DrawingSizeLabels = ["얇음", "중간", "두꺼움"];
    bool inkMode;
    double inkTime;

    void InitializeInk()
    {
        Controls.Add(inkCanvas);
        Controls.Add(inkTools);
        inkCanvas.PenSize = Math.Clamp(prefs.DrawingPenSize, 0, 2);
        inkCanvas.EraserSize = Math.Clamp(prefs.DrawingEraserSize, 0, 2);

        void AddTool(DrawingTool tool, ToolButton button)
        {
            drawingToolButtons.Add(tool, button);
            inkTools.Controls.Add(button);
        }
        AddTool(DrawingTool.Pen, Button("\uED63", "펜", () => SelectDrawingTool(DrawingTool.Pen)));
        AddTool(DrawingTool.Eraser, Button("\uE75C", "지우개", () => SelectDrawingTool(DrawingTool.Eraser)));
        var text = Button("", "텍스트", () => SelectDrawingTool(DrawingTool.Text));
        text.Text = "T";
        tips.SetToolTip(text, "텍스트 · 영상에서 클릭하여 입력, 본문을 끌어 이동, 모서리를 끌어 크기 조절");
        AddTool(DrawingTool.Text, text);
        var arrow = Button("", "화살표", () => SelectDrawingTool(DrawingTool.Arrow));
        arrow.DrawContent = (g, area) =>
        {
            float x = area.Width / 2f, y = area.Height / 2f;
            using var p = new Pen(Theme.Text, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLine(p, x - 7, y + 7, x + 7, y - 7);
            g.DrawLines(p, [new PointF(x + 1, y - 7), new PointF(x + 7, y - 7), new PointF(x + 7, y - 1)]);
        };
        AddTool(DrawingTool.Arrow, arrow);
        inkColorButton = Button("", "색상환", PickInkColor);
        inkColorButton.DrawContent = (g, bounds) => InkColorPicker.DrawSwatch(g, bounds, inkCanvas.PenColor, 18);
        inkWidthButton = Button("", "굵기 선택", () =>
        {
            if (inkCanvas.Tool == DrawingTool.Text) ShowInkTextSettings();
            else ShowInkWidths();
        });
        inkWidthButton.DrawContent = (g, bounds) =>
        {
            if (inkCanvas.Tool == DrawingTool.Text)
            {
                using var font = Theme.Font(9);
                Theme.DrawText(g, inkCanvas.TextSize.ToString(), font, Theme.Text, bounds,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else DrawWidthSample(g, bounds, inkCanvas.Erasing ? inkCanvas.EraserSize : inkCanvas.PenSize);
        };
        inkUndoButton = Button("\uE7A7", "실행 취소 · Ctrl+Z", inkCanvas.Undo);
        inkUndoButton.Enabled = false;
        inkTools.Controls.AddRange([inkColorButton, inkWidthButton, inkUndoButton,
            Button("\uE74D", "비우기", inkCanvas.ClearDrawing),
            Button("\uE8BB", "그림 도구 닫기", () => { inkMode = false; UpdateInkTools(); })]);
        inkCanvas.HistoryChanged += () => inkUndoButton.Enabled = inkCanvas.CanUndo;
        inkCanvas.TextRequested += position => ShowInkTextSettings(position);
        inkCanvas.TextSizeChanged += UpdateInkWidthButton;
        inkCanvas.ColorPickRequested += point => Safe(() => PickVideoColor(point));
        inkCanvas.MouseClick += (_, e) => { if (e.Button == MouseButtons.Right && !CancelInkColorPick()) ShowContext(); };
    }

    void SelectDrawingTool(DrawingTool tool)
    {
        CancelInkColorPick();
        inkCanvas.SelectTool(tool);
        UpdateInkTools();
    }

    void PickInkColor()
    {
        CancelInkColorPick();
        using var picker = new InkColorPicker(inkCanvas.PenColor);
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (picker.PickFromVideo)
        {
            SetInkColorPicking(true);
            status.Text = "영상에서 색을 선택하세요 · Esc 취소";
            return;
        }
        inkCanvas.PenColor = picker.SelectedColor;
        UpdateInkTools();
    }

    bool CancelInkColorPick()
    {
        if (!inkCanvas.PickingColor) return false;
        SetInkColorPicking(false);
        status.Text = "스포이드 취소";
        return true;
    }

    void SetInkColorPicking(bool enabled)
    {
        inkCanvas.SetColorPicking(enabled);
        inkColorButton!.Selected = enabled;
        inkColorButton.Invalidate();
    }

    void PickVideoColor(PointF point)
    {
        SetInkColorPicking(false);
        using var frame = CaptureFrame(includeInk: false);
        int x = Math.Min(frame.Width - 1, (int)(point.X * frame.Width));
        int y = Math.Min(frame.Height - 1, (int)(point.Y * frame.Height));
        inkCanvas.PenColor = frame.GetPixel(x, y);
        UpdateInkTools();
        status.Text = $"색상 선택 · #{inkCanvas.PenColor.R:X2}{inkCanvas.PenColor.G:X2}{inkCanvas.PenColor.B:X2}";
    }

    void DrawWidthSample(Graphics g, Rectangle bounds, int level)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float width = InkCanvas.WidthAt640(inkCanvas.Tool, level);
        if (inkCanvas.Erasing) width *= .32f;
        using var p = new Pen(Theme.Text, Math.Max(2, width)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(p, bounds.Left + 8, bounds.Height / 2f, bounds.Right - 8, bounds.Height / 2f);
    }

    sealed class InkWidthMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.Black;
        public override Color ImageMarginGradientBegin => Color.Black;
        public override Color ImageMarginGradientMiddle => Color.Black;
        public override Color ImageMarginGradientEnd => Color.Black;
        public override Color CheckBackground => Color.Black;
        public override Color CheckSelectedBackground => Color.Black;
        public override Color CheckPressedBackground => Color.Black;
        public override Color ButtonSelectedBorder => Theme.Muted;
        public override Color MenuBorder => Theme.Line;
        public override Color MenuItemSelected => Color.Black;
        public override Color MenuItemBorder => Theme.Muted;
    }

    void ShowInkWidths() => CreateInkWidthMenu().Show(inkWidthButton!, new Point(0, inkWidthButton!.Height));

    ContextMenuStrip CreateInkWidthMenu()
    {
        var menu = Menu();
        var samples = new List<Bitmap>();
        menu.BackColor = Color.Black;
        menu.Renderer = new ToolStripProfessionalRenderer(new InkWidthMenuColors()) { RoundedEdges = false };
        menu.ShowImageMargin = true;
        menu.ImageScalingSize = new Size(34, 22);
        for (int level = 0; level < DrawingSizeLabels.Length; level++)
        {
            int selected = level;
            var sample = new Bitmap(34, 22);
            samples.Add(sample);
            using (var g = Graphics.FromImage(sample)) DrawWidthSample(g, new Rectangle(0, 0, sample.Width, sample.Height), level);
            var item = new ToolStripMenuItem(DrawingSizeLabels[level], sample)
            {
                ForeColor = Theme.Text,
                ImageScaling = ToolStripItemImageScaling.None,
                Checked = (inkCanvas.Erasing ? inkCanvas.EraserSize : inkCanvas.PenSize) == level
            };
            item.Click += (_, _) => SetInkWidth(selected);
            menu.Items.Add(item);
        }
        menu.Disposed += (_, _) => { foreach (var sample in samples) sample.Dispose(); };
        return menu;
    }

    void SetInkWidth(int level)
    {
        if (inkCanvas.Erasing) prefs.DrawingEraserSize = inkCanvas.EraserSize = level;
        else prefs.DrawingPenSize = inkCanvas.PenSize = level;
        Remember();
        prefs.Save();
        UpdateInkTools();
    }

    void ShowInkTextSettings(PointF? position = null)
    {
        bool adding = position.HasValue;
        if (!adding) CancelInkColorPick();
        int sizeTop = adding ? 124 : 14;
        int buttonsTop = sizeTop + 42;
        using var dialog = new Form
        {
            Text = adding ? "영상에 텍스트 추가" : "텍스트 크기", StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false, ClientSize = new Size(352, buttonsTop + 44), AutoScaleMode = AutoScaleMode.Dpi,
            Font = Font, BackColor = Theme.Panel, ForeColor = Theme.Text
        };
        using var input = Theme.Input();
        if (adding)
        {
            input.Multiline = true;
            input.MaxLength = 240;
            input.ScrollBars = ScrollBars.Vertical;
            input.AccessibleName = "영상에 표시할 텍스트";
            input.SetBounds(12, 12, 328, 98);
            dialog.Controls.Add(input);
        }
        var label = Theme.Label("글자 크기 (px)");
        label.SetBounds(12, sizeTop, 110, 28);
        var fontSize = new NumericUpDown
        {
            Minimum = InkCanvas.MinTextSize, Maximum = inkCanvas.MaxTextSize,
            Value = adding ? inkCanvas.DefaultTextSize : inkCanvas.TextSize,
            Bounds = new Rectangle(128, sizeTop, 90, 28),
            BackColor = Theme.Field, ForeColor = Theme.Text, AccessibleName = "글자 크기 (픽셀)"
        };
        var ok = new Button { Text = adding ? "추가" : "적용", DialogResult = DialogResult.OK, Bounds = new Rectangle(172, buttonsTop, 80, 32) };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(260, buttonsTop, 80, 32) };
        foreach (var button in new[] { ok, cancel })
        {
            button.BackColor = Theme.Field;
            button.FlatStyle = FlatStyle.Flat;
        }
        dialog.Controls.AddRange([label, fontSize, ok, cancel]);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        dialog.ActiveControl = adding ? input : fontSize;
        if (dialog.ShowDialog(this) != DialogResult.OK || !inkMode) return;
        if (position is PointF origin) inkCanvas.AddText(input.Text.Trim(), origin, (int)fontSize.Value);
        else inkCanvas.SetTextSize((int)fontSize.Value);
    }

    void ToggleInk()
    {
        if(current==null||player==null||player.Number("video-params/w")<=0)return;StopShuttle();player.Set("pause","yes");
        inkMode=!inkMode;
        if(inkMode){player.Set("panscan",0);player.Set("video-zoom",0);player.Set("video-unscaled","no");PrepareInk();}
        UpdateInkTools();LayoutInk();
    }
    void PrepareInk(){int w=(int)player!.Number("video-params/w",640),h=(int)player.Number("video-params/h",360);inkCanvas.Prepare(new Size(Math.Max(1,w),Math.Max(1,h)));inkTime=player.Number("time-pos");}
    void UpdateInkTools()
    {
        if (inkButton == null) return;
        seek.DragAllowed = !inkMode;
        seekHit.SyncDragPermission();
        inkButton.Selected = inkMode;
        inkButton.Invalidate();
        foreach (var (tool, button) in drawingToolButtons)
        {
            button.Selected = inkCanvas.Tool == tool;
            button.Invalidate();
        }
        if (inkColorButton != null)
        {
            var color = inkCanvas.PenColor;
            var colorName = $"색상환 · #{color.R:X2}{color.G:X2}{color.B:X2}";
            inkColorButton.AccessibleName = colorName;
            tips.SetToolTip(inkColorButton, colorName);
            inkColorButton.Invalidate();
        }
        UpdateInkWidthButton();
        if (!inkMode) CancelInkColorPick();
        inkTools.Visible = inkMode;
        inkCanvas.Editing = inkMode;
        if (!inkMode && inkCanvas.Drawing) inkCanvas.Capture = false;
        inkCanvas.Visible = inkMode || inkCanvas.HasInk;
        if (inkCanvas.Visible) inkCanvas.Render();
    }

    void UpdateInkWidthButton()
    {
        if (inkWidthButton == null) return;
        string tool = inkCanvas.Erasing ? "지우개" : "펜";
        string level = DrawingSizeLabels[inkCanvas.Erasing ? inkCanvas.EraserSize : inkCanvas.PenSize];
        string description = inkCanvas.Tool == DrawingTool.Text ?
            $"글자 크기 · {inkCanvas.TextSize} px" : $"{tool} 굵기 · {level}";
        if (inkWidthButton.AccessibleName != description)
        {
            inkWidthButton.AccessibleName = description;
            tips.SetToolTip(inkWidthButton, description);
        }
        inkWidthButton.Invalidate();
    }

    void LayoutInk()
    {
        if(inkButton==null)return;
        double aspect=player?.Number("video-out-params/dw",0)??0;double dh=player?.Number("video-out-params/dh",0)??0;
        aspect=aspect>0&&dh>0?aspect/dh:(double)Math.Max(1,info?.Width??16)/Math.Max(1,info?.Height??9);
        int w=video.Width,h=(int)Math.Round(w/aspect);if(h>video.Height){h=video.Height;w=(int)Math.Round(h*aspect);}
        inkCanvas.SetBounds(video.Left+(video.Width-w)/2,video.Top+(video.Height-h)/2,w,h);
        int columns = Math.Clamp((video.Width - 14) / 34, 1, inkTools.Controls.Count);
        int toolbarWidth = columns * 34 + 6;
        int rows = (inkTools.Controls.Count + columns - 1) / columns;
        inkTools.SetBounds(video.Left + Math.Max(4, (video.Width - toolbarWidth) / 2), 48, toolbarWidth, rows * 36 + 8);
        for (int i = 0; i < inkTools.Controls.Count; i++)
            inkTools.Controls[i].SetBounds(4 + (i % columns) * 34, 4 + (i / columns) * 36, 32, 34);
    }
    void ClearAnnotations(){inkMode=false;inkCanvas.ClearInk();UpdateInkTools();}
    void CheckAnnotations()
    {
        if(!inkMode&&!inkCanvas.HasInk)return;
        if(current==null||!player!.Flag("pause")||Math.Abs(player.Number("time-pos")-inkTime)>.04)ClearAnnotations();
    }
    Bitmap CaptureFrame(bool includeInk = true)
    {
        var temp=Path.Combine(Preferences.DataDirectory,"frame-"+Guid.NewGuid().ToString("N")+".png");Directory.CreateDirectory(Preferences.DataDirectory);
        try
        {
            player!.Command("screenshot-to-file",temp,"video");
            using var source = new Bitmap(temp);
            var image = new Bitmap(source);
            if (!includeInk) return image;
            try{using var g=Graphics.FromImage(image);inkCanvas.Composite(g,image.Size);return image;}
            catch{image.Dispose();throw;}
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    void SaveFrame(string path){using var image=CaptureFrame();image.Save(path,System.Drawing.Imaging.ImageFormat.Png);}
}
