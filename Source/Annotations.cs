using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace MooVideo;

sealed class InkCanvas:Control
{
    Bitmap? ink;PointF? previous;
    internal Color PenColor=Color.Red;
    internal bool Erasing;
    internal bool Editing;
    internal int PenSize=1,EraserSize=1;
    internal bool HasInk {get;private set;}
    internal bool Drawing=>previous!=null;
    internal InkCanvas(){Visible=false;TabStop=false;Cursor=Cursors.Cross;}
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x80000;return p;}}
    internal void Prepare(Size frame){if(ink!=null)return;ink=new Bitmap(frame.Width,frame.Height,PixelFormat.Format32bppPArgb);}
    internal void ClearInk(){previous=null;Capture=false;HasInk=false;ink?.Dispose();ink=null;Visible=false;}
    internal void Stroke(PointF from,PointF to)
    {
        if(ink==null)return;
        float size=ink.Width*(Erasing?new[]{.012f,.035f,.07f}[EraserSize]:new[]{.003f,.007f,.014f}[PenSize]);
        using var g=Graphics.FromImage(ink);g.SmoothingMode=SmoothingMode.AntiAlias;
        if(Erasing)g.CompositingMode=CompositingMode.SourceCopy;
        using var pen=new Pen(Erasing?Color.Transparent:PenColor,Math.Max(1,size)){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};
        var a=new PointF(from.X*ink.Width,from.Y*ink.Height);var b=new PointF(to.X*ink.Width,to.Y*ink.Height);
        if(a==b){using var brush=new SolidBrush(pen.Color);g.FillEllipse(brush,a.X-size/2,a.Y-size/2,size,size);}else g.DrawLine(pen,a,b);
        if(!Erasing)HasInk=true;Render();
    }
    PointF Normal(Point p)=>new(Math.Clamp((float)p.X/Math.Max(1,Width),0,1),Math.Clamp((float)p.Y/Math.Max(1,Height),0,1));
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;Capture=true;previous=Normal(e.Location);Stroke(previous.Value,previous.Value);}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(previous is PointF a){var b=Normal(e.Location);Stroke(a,b);previous=b;}}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button!=MouseButtons.Left)return;if(previous is PointF a)Stroke(a,Normal(e.Location));previous=null;Capture=false;}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)previous=null;}
    protected override void WndProc(ref Message m){if(m.Msg==0x84&&!Editing){m.Result=(IntPtr)(-1);return;}base.WndProc(ref m);}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Render();}
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Render();}
    internal void Composite(Graphics g,Size size){if(ink!=null&&HasInk)g.DrawImage(ink,new Rectangle(Point.Empty,size));}
    void Render()
    {
        if(!IsHandleCreated||Width<1||Height<1)return;
        using var surface=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(surface)){g.Clear(Color.FromArgb(1,0,0,0));if(ink!=null)g.DrawImage(ink,ClientRectangle);}
        var dc=CreateCompatibleDC(IntPtr.Zero);var bitmap=surface.GetHbitmap(Color.FromArgb(0));var old=SelectObject(dc,bitmap);
        try{var size=Size;var p=new Point();var blend=new Blend{Alpha=255,Format=1};if(!UpdateLayeredWindow(Handle,IntPtr.Zero,IntPtr.Zero,ref size,dc,ref p,0,ref blend,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}
        finally{SelectObject(dc,old);DeleteObject(bitmap);DeleteDC(dc);}
    }
    protected override void Dispose(bool disposing){if(disposing)ink?.Dispose();base.Dispose(disposing);}
    [StructLayout(LayoutKind.Sequential,Pack=1)]struct Blend{public byte Operation,Flags,Alpha,Format;}
    [DllImport("user32.dll",SetLastError=true)]static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr dst,IntPtr point,ref Size size,IntPtr src,ref Point origin,uint key,ref Blend blend,uint flags);
    [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
}

public sealed partial class PlayerForm
{
    readonly InkCanvas inkCanvas=new();
    readonly Panel inkTools=new(){BackColor=Theme.Panel,Visible=false};
    ToolButton? inkButton,penTool,eraserTool,inkColorButton,inkWidthButton;
    bool inkMode;double inkTime;
    void InitializeInk()
    {
        Controls.Add(inkCanvas);Controls.Add(inkTools);
        penTool=Button("\uED63","펜",()=>{inkCanvas.Erasing=false;UpdateInkTools();});
        eraserTool=Button("\uE75C","지우개",()=>{inkCanvas.Erasing=true;UpdateInkTools();});
        inkTools.Controls.AddRange(new Control[]{penTool,eraserTool});penTool.SetBounds(8,6,36,34);eraserTool.SetBounds(48,6,36,34);
        var color=Button("","색상",()=>{var m=Menu();foreach(var entry in new[]{("흰색",Color.White),("검정",Color.Black),("빨강",Color.Red),("노랑",Color.Yellow),("초록",Color.Lime),("파랑",Color.DeepSkyBlue)}){var c=entry.Item2;var item=m.Items.Add(entry.Item1,null,(_,_)=>{inkCanvas.PenColor=c;inkCanvas.Erasing=false;UpdateInkTools();});item.ForeColor=c==Color.Black?Theme.Text:c;}m.Show(inkTools,new Point(88,44));});color.SetBounds(88,6,52,34);inkTools.Controls.Add(color);
        var thickness=Button("","굵기",()=>{var m=Menu();var names=new[]{"얇음","중간","두꺼움"};for(int i=0;i<3;i++){var n=i;var item=new ToolStripMenuItem(names[i]){Checked=(inkCanvas.Erasing?inkCanvas.EraserSize:inkCanvas.PenSize)==i};item.Click+=(_,_)=>{if(inkCanvas.Erasing)inkCanvas.EraserSize=n;else inkCanvas.PenSize=n;UpdateInkTools();};m.Items.Add(item);}m.Show(inkTools,new Point(144,44));});thickness.SetBounds(144,6,52,34);inkTools.Controls.Add(thickness);
        inkColorButton=color;inkWidthButton=thickness;
        var clear=Button("","비우기",()=>{inkCanvas.ClearInk();PrepareInk();});clear.SetBounds(200,6,64,34);inkTools.Controls.Add(clear);
        var close=Button("\uE8BB","그림 도구 닫기",()=>{inkMode=false;UpdateInkTools();});close.SetBounds(268,6,36,34);inkTools.Controls.Add(close);
        inkCanvas.MouseClick+=(_,e)=>{if(e.Button==MouseButtons.Right)ShowContext();};
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
        if(inkButton==null)return;
        inkButton.Selected=inkMode;inkButton.Invalidate();
        if(penTool!=null){penTool.Selected=!inkCanvas.Erasing;eraserTool!.Selected=inkCanvas.Erasing;penTool.Invalidate();eraserTool.Invalidate();}
        if(inkColorButton!=null){inkColorButton.Text=inkCanvas.PenColor==Color.White?"흰색":inkCanvas.PenColor==Color.Black?"검정":inkCanvas.PenColor==Color.Red?"빨강":inkCanvas.PenColor==Color.Yellow?"노랑":inkCanvas.PenColor==Color.Lime?"초록":"파랑";inkColorButton.Invalidate();inkWidthButton!.Text=new[]{"얇음","중간","두꺼움"}[inkCanvas.Erasing?inkCanvas.EraserSize:inkCanvas.PenSize];inkWidthButton.Invalidate();}
        inkTools.Visible=inkMode;inkCanvas.Visible=inkMode||inkCanvas.HasInk;inkCanvas.Editing=inkMode;
    }
    void LayoutInk()
    {
        if(inkButton==null)return;
        double aspect=player?.Number("video-out-params/dw",0)??0;double dh=player?.Number("video-out-params/dh",0)??0;
        aspect=aspect>0&&dh>0?aspect/dh:(double)Math.Max(1,info?.Width??16)/Math.Max(1,info?.Height??9);
        int w=video.Width,h=(int)Math.Round(w/aspect);if(h>video.Height){h=video.Height;w=(int)Math.Round(h*aspect);}
        inkCanvas.SetBounds(video.Left+(video.Width-w)/2,video.Top+(video.Height-h)/2,w,h);
        inkTools.SetBounds(Math.Max(4,(video.Width-312)/2),48,312,46);
    }
    void ClearAnnotations(){inkMode=false;inkCanvas.ClearInk();UpdateInkTools();}
    void CheckAnnotations()
    {
        if(!inkMode&&!inkCanvas.HasInk)return;
        if(current==null||!player!.Flag("pause")||Math.Abs(player.Number("time-pos")-inkTime)>.04)ClearAnnotations();
    }
    void SaveFrame(string path)
    {
        var temp=Path.Combine(Preferences.DataDirectory,"frame-"+Guid.NewGuid().ToString("N")+".png");Directory.CreateDirectory(Preferences.DataDirectory);
        try{player!.Command("screenshot-to-file",temp,"video");using var image=new Bitmap(temp);using(var g=Graphics.FromImage(image))inkCanvas.Composite(g,image.Size);image.Save(path,System.Drawing.Imaging.ImageFormat.Png);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
