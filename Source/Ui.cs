using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
namespace MooVideo;
static class Theme
{
    public static readonly Color Bg=Color.FromArgb(8,10,12), Panel=Color.FromArgb(17,20,23), Field=Color.FromArgb(29,34,38), Line=Color.FromArgb(48,56,62), Hover=Color.FromArgb(62,72,79), Text=Color.FromArgb(244,247,249), Muted=Color.FromArgb(168,179,187), Accent=Color.FromArgb(240,244,246), AccentInk=Color.FromArgb(219,227,232);
    public static Font Font(float size=10,FontStyle style=FontStyle.Regular)=>new("Segoe UI",size,style);
    public static Label Label(string text,float size=10,Color? color=null) => new(){Text=text,ForeColor=color??Text,Font=Font(size),AutoSize=false,BackColor=Color.Transparent,TextAlign=ContentAlignment.MiddleLeft};
    public static TextBox Input(string text="")=>new(){Text=text,BackColor=Field,ForeColor=Text,BorderStyle=BorderStyle.FixedSingle,Font=Font(10)};
    public static void DrawText(Graphics g,string text,Font font,Color color,Rectangle rect,TextFormatFlags flags=TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis) => TextRenderer.DrawText(g,text,font,rect,color,flags);
}
sealed class ToolButton:Control
{
    public string Glyph=""; public bool Selected; public bool Primary; bool hover;
    internal Action<Graphics,Rectangle>? DrawContent;
    public ToolButton(string glyph,string label,Action action,bool primary=false)
    {
        Glyph=glyph; AccessibleName=label; AccessibleRole=AccessibleRole.PushButton; Text=label; Primary=primary; Cursor=Cursors.Hand; Size=new(36,34); TabStop=true;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);
        Click+=(_,_)=>action(); MouseEnter+=(_,_)=>{hover=true;Invalidate();}; MouseLeave+=(_,_)=>{hover=false;Invalidate();};
        KeyDown+=(_,e)=>{if(e.KeyCode is Keys.Space or Keys.Enter) {action();e.Handled=true;}};
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        using var bg=new SolidBrush(Primary?(hover?Theme.Hover:Theme.Line):Selected?Theme.Field:hover?Theme.Hover:BackColor);
        g.FillRoundedRectangle(bg,new Rectangle(1,1,Width-2,Height-2),new Size(8,8));
        var c=!Enabled?Theme.Muted:Theme.Text;
        using var font=new Font("Segoe Fluent Icons",14); using var text=Theme.Font(9);
        if(DrawContent!=null)DrawContent(g,ClientRectangle);
        else Theme.DrawText(g,Glyph.Length>0?Glyph:Text,Glyph.Length>0?font:text,c,ClientRectangle,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|(Glyph.Length>0?TextFormatFlags.NoPadding:TextFormatFlags.EndEllipsis));
        if(Focused) {using var p=new Pen(Theme.Accent,2);g.DrawRectangle(p,3,3,Width-7,Height-7);}
    }
}
sealed class OverlayPanel:Panel
{
    public static readonly Color KeyColor=Color.FromArgb(1,0,1);
    public OverlayPanel(){BackColor=KeyColor;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x00080000;return p;}}
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Native.SetLayeredWindowAttributes(Handle,0x010001,255,1);}
}
sealed class ResizeEdge:Control
{
    public ResizeEdge(Form owner,int hit){BackColor=Color.Black;TabStop=false;Cursor=hit is 10 or 11?Cursors.SizeWE:hit is 12 or 15?Cursors.SizeNS:hit is 13 or 17?Cursors.SizeNWSE:Cursors.SizeNESW;MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left){Native.ReleaseCapture();Native.SendMessage(owner.Handle,0xA1,(IntPtr)hit,IntPtr.Zero);}};}
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x80000;return p;}}
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Native.SetLayeredWindowAttributes(Handle,0,1,2);}
}
// A separate, click-through native layer supplies true per-pixel alpha over mpv's child HWND.
sealed class GradientShade:Control
{
    readonly bool top;
    public GradientShade(bool top){this.top=top;TabStop=false;Enabled=false;}
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x80020;return p;}}
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Render();}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Render();}
    internal void Render()
    {
        if(!IsHandleCreated||Width<=0||Height<=0)return;
        using var bitmap=new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(bitmap))
        using(var brush=new LinearGradientBrush(new Rectangle(0,0,Width,Height),Color.FromArgb(top?160:0,0,0,0),Color.FromArgb(top?0:190,0,0,0),LinearGradientMode.Vertical))g.FillRectangle(brush,0,0,Width,Height);
        var dc=CreateCompatibleDC(IntPtr.Zero);var dib=bitmap.GetHbitmap(Color.FromArgb(0));var old=SelectObject(dc,dib);
        try{var size=new Size(Width,Height);var origin=new Point();var blend=new Blend{Alpha=255,Format=1};if(!UpdateLayeredWindow(Handle,IntPtr.Zero,IntPtr.Zero,ref size,dc,ref origin,0,ref blend,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}
        finally{SelectObject(dc,old);DeleteObject(dib);DeleteDC(dc);}
    }
    [StructLayout(LayoutKind.Sequential,Pack=1)]struct Blend{public byte Operation,Flags,Alpha,Format;}
    [DllImport("user32.dll",SetLastError=true)]static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr dst,IntPtr point,ref Size size,IntPtr src,ref Point origin,uint key,ref Blend blend,uint flags);
    [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
}
sealed class VolumeSlider:Control
{
    int level=65;bool dragging;
    public event Action<int>? ValueChanged;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Level {get=>level;set{level=Math.Clamp(value,0,100);Invalidate();}}
    public bool Dragging=>dragging;
    public VolumeSlider(){Size=new(42,132);Cursor=Cursors.Hand;TabStop=true;AccessibleName="볼륨";AccessibleRole=AccessibleRole.Slider;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);}
    void SetFromY(int y){var next=Math.Clamp((int)Math.Round((Height-13-y)*100.0/Math.Max(1,Height-26)),0,100);if(next==level)return;level=next;ValueChanged?.Invoke(next);Invalidate();}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){dragging=true;Capture=true;SetFromY(e.Y);}}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging)SetFromY(e.Y);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(dragging){SetFromY(e.Y);dragging=false;Capture=false;}}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode is Keys.Up or Keys.Down){var next=Math.Clamp(level+(e.KeyCode==Keys.Up?5:-5),0,100);if(next!=level){level=next;ValueChanged?.Invoke(next);Invalidate();}e.Handled=true;}}
    protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;int center=Width/2,top=13,bottom=Height-13;using var track=new Pen(Theme.Line,4){StartCap=LineCap.Round,EndCap=LineCap.Round};g.DrawLine(track,center,top,center,bottom);int thumb=bottom-(int)((bottom-top)*level/100.0);using var fill=new Pen(Theme.Accent,4){StartCap=LineCap.Round,EndCap=LineCap.Round};g.DrawLine(fill,center,bottom,center,thumb);using var brush=new SolidBrush(Theme.Text);g.FillEllipse(brush,center-6,thumb-6,12,12);if(Focused){using var focus=new Pen(Theme.Accent,1);g.DrawRectangle(focus,1,1,Width-3,Height-3);}}
}
sealed class SeekBar:Control
{
    internal bool DragAllowed=true;
    public double Value,Duration,MarkIn=-1,MarkOut=-1; public bool Dragging; string dragging=""; public event Action<double>? Seek; public event Action<double>? MarkInChanged,MarkOutChanged;
    public SeekBar() {Height=22;Cursor=Cursors.Hand;AccessibleName="재생 위치";SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint,true);}
    void UpdatePosition(int x){if(Duration>0){Value=Math.Clamp((double)(x-8)/Math.Max(1,Width-16),0,1)*Duration;Seek?.Invoke(Value);Invalidate();}}
    double At(int x)=>Math.Clamp((double)(x-8)/Math.Max(1,Width-16),0,1)*Duration;
    float X(double t)=>8+(float)(Math.Clamp(t/Math.Max(Duration,0.001),0,1)*(Width-16));
    void DragTo(int x){if(dragging=="in"){MarkIn=Math.Min(At(x),MarkOut>=0?MarkOut:Duration);MarkInChanged?.Invoke(MarkIn);}else if(dragging=="out"){MarkOut=Math.Max(At(x),MarkIn>=0?MarkIn:0);MarkOutChanged?.Invoke(MarkOut);}else UpdatePosition(x);Invalidate();}
    internal void BeginDrag(int x){if(!DragAllowed)return;dragging=MarkIn>=0&&Math.Abs(x-X(MarkIn))<11?"in":MarkOut>=0&&Math.Abs(x-X(MarkOut))<11?"out":"seek";Dragging=true;DragTo(x);}
    internal void MoveDrag(int x){if(DragAllowed&&Dragging)DragTo(x);}
    internal void EndDrag(int x){if(DragAllowed&&Dragging)DragTo(x);CancelDrag();}
    internal void CancelDrag(){Dragging=false;dragging="";}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left&&DragAllowed){BeginDrag(e.X);Capture=true;}}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!DragAllowed){Cursor=Cursors.No;return;}if(Dragging)DragTo(e.X);else Cursor=MarkIn>=0&&Math.Abs(e.X-X(MarkIn))<11||MarkOut>=0&&Math.Abs(e.X-X(MarkOut))<11?Cursors.SizeWE:Cursors.Hand;}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left){EndDrag(e.X);Capture=false;}}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;var y=Height/2f;var width=Width-16;
        using var p=new Pen(Theme.Line,3);g.DrawLine(p,8,y,Width-8,y);
        if(Duration<=0)return;
        if(MarkIn>=0&&MarkOut>MarkIn){using var b=new SolidBrush(Color.FromArgb(48,Theme.Accent));g.FillRectangle(b,X(MarkIn),2,X(MarkOut)-X(MarkIn),Height-4);}
        using var filled=new Pen(Theme.Accent,3);g.DrawLine(filled,8,y,X(Value),y);
        using var dot=new SolidBrush(Theme.Accent);g.FillEllipse(dot,X(Value)-4,y-4,8,8);
        foreach(var t in new[]{MarkIn,MarkOut})if(t>=0){using var m=new Pen(Theme.Accent,2);g.DrawLine(m,X(t),1,X(t),Height-1);using var brush=new SolidBrush(Theme.Accent);g.FillEllipse(brush,X(t)-5,y-5,10,10);}
    }
}
// Separate from the color-keyed chrome so its visually empty pixels still accept input.
sealed class SeekHitTarget:Control
{
    readonly SeekBar seek;readonly Func<Point,bool> blocked;
    internal SeekHitTarget(SeekBar seek,Func<Point,bool> blocked){this.seek=seek;this.blocked=blocked;BackColor=Color.Black;Cursor=Cursors.Hand;TabStop=false;AccessibleName="재생 위치 드래그 영역";}
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x80000;return p;}}
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Native.SetLayeredWindowAttributes(Handle,0,1,2);}
    protected override void WndProc(ref Message m){if(m.Msg==0x84&&!Capture&&blocked(Cursor.Position)){m.Result=(IntPtr)(-1);return;}base.WndProc(ref m);}
    internal void SyncDragPermission(){if(!seek.DragAllowed){seek.CancelDrag();seek.Capture=false;Capture=false;}Cursor=seek.Cursor=seek.DragAllowed?Cursors.Hand:Cursors.No;}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left&&seek.DragAllowed){Capture=true;seek.BeginDrag(e.X);}}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);Cursor=seek.DragAllowed?Cursors.Hand:Cursors.No;seek.MoveDrag(e.X);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left){seek.EndDrag(e.X);Capture=false;}}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)seek.CancelDrag();}
}
sealed class TimeField:TextBox
{
    internal decimal Maximum=1;
    internal bool Valid=>decimal.TryParse(Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)&&value>=0&&value<=Maximum;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal decimal Value {get=>decimal.TryParse(Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)?value:0;set=>Text=value.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture);}
    internal event EventHandler? ValueChanged;
    public TimeField(){AutoSize=false;BackColor=Theme.Field;ForeColor=Theme.Text;BorderStyle=BorderStyle.FixedSingle;Font=Theme.Font(11);TextChanged+=(_,_)=>ValueChanged?.Invoke(this,EventArgs.Empty);}
}
sealed class VideoSurface:Panel
{
    public bool Empty=true; public string DropHint="";
    internal bool Updating;
    internal string WelcomeText=>Updating?"업데이트 중":"MooVideo";
    internal int WelcomeTitleHeight {get{using var font=Theme.Font(25,FontStyle.Bold);return TextRenderer.MeasureText("MooVideo",font,Size.Empty,TextFormatFlags.SingleLine).Height+12;}}
    internal int WelcomeButtonTop=>Height/2-23+WelcomeTitleHeight+16;
    public VideoSurface(){BackColor=Theme.Bg;DoubleBuffered=true;AllowDrop=true;TabStop=true;SetStyle(ControlStyles.ResizeRedraw,true);}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);if(!Empty)return;
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;var x=Width/2;var y=Height/2-45;
        using var pen=new Pen(Theme.Muted,2);g.DrawRoundedRectangle(pen,new Rectangle(x-34,y-48,68,48),new Size(11,11));
        using var b=new SolidBrush(Theme.Accent);g.FillPolygon(b,new[]{new Point(x-5,y-36),new Point(x-5,y-12),new Point(x+13,y-24)});
        using var f=Theme.Font(25,FontStyle.Bold);Theme.DrawText(g,WelcomeText,f,Theme.Text,new Rectangle(0,y+22,Width,WelcomeTitleHeight),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        using var small=Theme.Font(10);
        Theme.DrawText(g,"파일 열기  Ctrl + O    ·    폴더 열기  Ctrl + Shift + O",small,Theme.Muted,new Rectangle(0,WelcomeButtonTop+62,Width,28),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        Theme.DrawText(g,"상단에 놓으면 재생목록에 추가  /  중·하단에 놓으면 바로 재생",small,Theme.Muted,new Rectangle(0,WelcomeButtonTop+94,Width,28),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
    }
}
static class Native
{
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h,EnumProc p,IntPtr param);
    public delegate bool EnumProc(IntPtr h,IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnableWindow(IntPtr h,bool enable);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h,uint color,byte alpha,uint flags);
}
