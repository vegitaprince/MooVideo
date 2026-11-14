using System.Globalization;
using System.Text.Json;
using System.Diagnostics;
namespace MooVideo;
public sealed partial class PlayerForm:Form,IMessageFilter
{
    readonly Preferences prefs=Preferences.Load();
    readonly VideoSurface video=new();
    readonly OverlayPanel bottom=new(),header=new();readonly Panel side=new(){BackColor=Theme.Panel,Visible=false},volumePopup=new(){BackColor=Theme.Panel,Visible=false};
    readonly Label title=Theme.Label("MooVideo",10),time=Theme.Label("00:00 / 00:00",9,Theme.Text),status=Theme.Label("READY",9,Theme.Muted),volumeState=Theme.Label("65%",9,Theme.Text);
    readonly VolumeSlider volumeSlider=new();
    readonly SeekBar seek=new(); readonly ToolTip tips=new(){BackColor=Theme.Field,ForeColor=Theme.Text,InitialDelay=450};
    readonly System.Windows.Forms.Timer timer=new(){Interval=50},wheelTimer=new(){Interval=240};
    readonly List<ToolButton> leftButtons=new(),rightButtons=new();
    readonly ToolButton play,volume,speed,windowSize,loop,playlistButton,folderButton,editButton;
    readonly GradientShade topShade=new(true),bottomShade=new(false);
    readonly List<ResizeEdge> edges=new();
    bool firstVideoSized;
    DateTime volumeFeedbackUntil=DateTime.MinValue;
    readonly List<string> playlist=new(); Mpv? player; string? current; MediaInfo? info;
    double position,duration,markIn=-1,markOut=-1; int index=-1; bool eofHandled,shuttle,full,shuttleResume,sideOpen; string repeat="off"; Point? videoDown,lastPointer;
    DateTime lastWheel=DateTime.MinValue,lastSave=DateTime.UtcNow,lastPointerMove=DateTime.MinValue,lastVolumeHover=DateTime.MinValue; int wheelBurst,lastDirection;
    Rectangle restoreBounds; FormWindowState restoreState; CancellationTokenSource? probeToken,updateToken; bool checkingUpdate; string sideMode="";
    readonly string[] initialArgs; bool closing;
    public PlayerForm(string[] args)
    {
        initialArgs=args; Text="MooVideo";Name="MooVideo";BackColor=Theme.Bg;ForeColor=Theme.Text;Font=Theme.Font();
        AutoScaleMode=AutoScaleMode.Dpi; FormBorderStyle=FormBorderStyle.None;MinimumSize=new(320,180);Size=new(1360,840);StartPosition=FormStartPosition.CenterScreen;KeyPreview=true;AllowDrop=true;
        var ico=Path.Combine(AppContext.BaseDirectory,"MooVideo.ico");if(File.Exists(ico))Icon=new Icon(ico);
        Controls.Add(video);Controls.Add(topShade);Controls.Add(bottomShade); Controls.Add(side); Controls.Add(bottom); Controls.Add(header);Controls.Add(volumePopup);
        foreach(var hit in new[]{10,11,12,15,13,14,16,17}){var edge=new ResizeEdge(this,hit);edges.Add(edge);Controls.Add(edge);}
        title.AutoEllipsis=true; header.Controls.Add(title);
        var min=Button("\uE921","최소화",()=>WindowState=FormWindowState.Minimized);var max=Button("\uE922","최대화 / 복원",Maximize);var close=Button("\uE8BB","닫기",Close);
        header.Controls.AddRange(new Control[]{min,max,close});
        header.Resize+=(_,_)=>{title.SetBounds(18,4,Math.Max(20,header.Width-166),36);min.SetBounds(header.Width-132,4,36,36);max.SetBounds(header.Width-88,4,36,36);close.SetBounds(header.Width-44,4,36,36);};
        header.MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left){Native.ReleaseCapture();Native.SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}};
        title.MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left){Native.ReleaseCapture();Native.SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}};
        title.DoubleClick+=(_,_)=>Maximize();
        leftButtons.Add(Button("\uE8E5","파일 열기 · Ctrl+O",OpenFiles));
        leftButtons.Add(Button("\uE892","이전 영상 · Ctrl+←",()=>Next(-1)));
        play=Button("\uE768","재생 / 일시정지 · Space",TogglePlay,true);leftButtons.Add(play);
        leftButtons.Add(Button("\uE893","다음 영상 · Ctrl+→",()=>Next(1)));
        leftButtons.Add(Button("\uE892","이전 프레임 · ,",()=>Frame(-1)));
        leftButtons.Add(Button("\uE893","다음 프레임 · .",()=>Frame(1)));
        volume=Button("\uE767","볼륨 · 누르면 음소거",()=>{player?.Command("cycle","mute");UpdateControls();});leftButtons.Add(volume);
        volume.MouseEnter+=(_,_)=>ShowVolumeFlyout();volumePopup.MouseEnter+=(_,_)=>lastVolumeHover=DateTime.UtcNow;
        volumePopup.Controls.Add(volumeState);volumePopup.Controls.Add(volumeSlider);
        volumeState.TextAlign=ContentAlignment.MiddleCenter;volumeState.SetBounds(2,4,52,26);volumeSlider.SetBounds(7,32,42,132);
        volumeSlider.ValueChanged+=v=>{if(player==null)return;player.Set("volume",v);player.Set("mute","no");UpdateControls();};
        speed=Button("","재생 속도",ShowSpeed);speed.Text="1×";leftButtons.Add(speed);
        windowSize=Button("\uE9A6","창 크기 · 원본 / 배율",ShowWindowSizes);leftButtons.Add(windowSize);
        loop=Button("\uE8EE","반복: 끔 / 한 곡 / 목록",CycleRepeat);leftButtons.Add(loop);
        rightButtons.Add(Button("\uE8B9","스크린샷 · S",Screenshot));
        rightButtons.Add(Button("\uE8F2","자막 / 오디오 트랙",ShowTracks));
        editButton=Button("\uE8C6","무손실 편집 · E",()=>ToggleSide("edit"));rightButtons.Add(editButton);
        folderButton=Button("\uE8B7","파일 탐색 · Ctrl+Shift+O",()=>ToggleSide("files"));rightButtons.Add(folderButton);
        playlistButton=Button("\uE8FD","재생목록 · P",()=>ToggleSide("playlist"));rightButtons.Add(playlistButton);
        rightButtons.Add(Button("\uE946","파일 정보 / 도움말 · I",()=>ToggleSide("info")));
        rightButtons.Add(Button("\uE740","전체화면 · F11",Fullscreen));
        bottom.Controls.AddRange(leftButtons.Concat(rightButtons).Cast<Control>().ToArray());bottom.Controls.AddRange(new Control[]{seek,time,status});
        foreach(var b in leftButtons.Concat(rightButtons).Concat(header.Controls.OfType<ToolButton>()))b.BackColor=OverlayPanel.KeyColor;
        seek.BackColor=OverlayPanel.KeyColor;
        seek.Seek+=v=>{player?.Seek(v);eofHandled=false;};
        seek.MarkInChanged+=v=>{markIn=v;if(inBox!=null&&sideMode=="edit")inBox.Value=(decimal)v;};
        seek.MarkOutChanged+=v=>{markOut=v;if(outBox!=null&&sideMode=="edit")outBox.Value=(decimal)v;};
        video.MouseClick+=(_,e)=>{if(e.Button==MouseButtons.Right)ShowContext();};
        video.DoubleClick+=(_,_)=>TogglePlay();video.MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left)videoDown=e.Location;};
        video.MouseMove+=(_,e)=>{if(videoDown is Point p&&e.Button==MouseButtons.Left&&Math.Abs(p.X-e.X)+Math.Abs(p.Y-e.Y)>7&&!full){videoDown=null;Native.ReleaseCapture();Native.SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}};
        video.MouseUp+=(_,_)=>videoDown=null;video.DragEnter+=DragEnterFiles;video.DragOver+=DragOverFiles;video.DragDrop+=DropFiles;
        DragEnter+=DragEnterFiles;DragOver+=DragOverFiles;DragDrop+=DropFiles;
        KeyDown+=OnKey;Resize+=(_,_)=>LayoutPlayer();
        wheelTimer.Tick+=(_,_)=>{wheelTimer.Stop();if(shuttle){shuttle=false;wheelBurst=0;player?.Set("speed",1);player?.Set("pause",shuttleResume?"no":"yes");status.Text=shuttleResume?"1× · 정상 재생":"일시정지";}};
        timer.Tick+=(_,_)=>Tick(); Shown+=OnShown;FormClosing+=OnClosing;Application.AddMessageFilter(this);
        playlist.AddRange(prefs.Playlist.Where(File.Exists));LayoutPlayer();
    }
    ToolButton Button(string glyph,string label,Action action,bool primary=false){var b=new ToolButton(glyph,label,()=>Safe(action),primary){BackColor=Theme.Panel};tips.SetToolTip(b,label);return b;}
    void Safe(Action a){try{a();}catch(Exception ex){Error(ex);}}
    void Error(Exception ex){status.Text="작업 실패";MessageBox.Show(this,ex.Message,"MooVideo",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    async void OnShown(object? sender,EventArgs e)
    {
        try{
            player=new Mpv(video.Handle);player.Set("volume",Math.Clamp(prefs.Volume,0,100));player.Loaded+=OnLoaded;player.Failed+=m=>{video.Empty=true;video.Invalidate();Error(new Exception("파일을 재생할 수 없습니다.\n"+m));};
            timer.Start();
            var paths=initialArgs.Where(File.Exists).ToArray();if(paths.Length>0)AddFiles(paths,true);
            if(!initialArgs.Contains("--ui-smoke"))_=CheckForUpdates(false);
            if(initialArgs.Contains("--ui-smoke"))await RunUiSmoke();
        }catch(Exception ex){Error(new Exception("재생 엔진을 시작하지 못했습니다. 앱 폴더의 libmpv-2.dll을 확인하세요.\n"+ex.Message));}
    }
    void LayoutPlayer()
    {
        bool compact=ClientSize.Width<1050;int bh=compact?112:88,th=44;int sw=sideOpen&&!full?340:0;video.SetBounds(0,0,ClientSize.Width-sw,ClientSize.Height);
        side.SetBounds(full?ClientSize.Width-340:ClientSize.Width-sw,0,full?340:sw,ClientSize.Height-bh);
        bottom.SetBounds(0,ClientSize.Height-bh,ClientSize.Width,bh);header.SetBounds(0,0,ClientSize.Width-sw,th);
        topShade.SetBounds(0,0,video.Width,82);bottomShade.SetBounds(0,ClientSize.Height-bh-40,ClientSize.Width,bh+40);
        topShade.BringToFront();bottomShade.BringToFront();bottom.BringToFront();side.BringToFront();header.BringToFront();volumePopup.BringToFront();
        seek.SetBounds(14,4,bottom.Width-28,22);int x=14;
        foreach(var b in leftButtons){bool show=ClientSize.Width>=600||b==leftButtons[0]||b==play||b==volume||b==speed||b==windowSize;b.Visible=show;if(!show)continue;b.SetBounds(x,32,b==play?44:36,36);x+=b.Width+5;}
        int right=bottom.Width-14;foreach(var b in rightButtons.AsEnumerable().Reverse()){right-=36;b.SetBounds(right,compact?72:32,36,36);right-=5;}
        time.SetBounds(x+12,28,Math.Max(100,right-x-22),24);status.SetBounds(x+12,52,Math.Max(100,right-x-22),22);
        time.Visible=true;status.Visible=!compact;
        if(compact)time.SetBounds(x+8,36,Math.Max(45,bottom.Width-x-22),24);
        int popupHeight=Math.Min(176,Math.Max(80,bottom.Top+24));
        volumePopup.SetBounds(volume.Left-10,Math.Max(8,bottom.Top+32-popupHeight),56,popupHeight);
        volumeSlider.Height=popupHeight-40;
        int edgeWidth=6;var rects=new[]{new Rectangle(0,edgeWidth,edgeWidth,Height-2*edgeWidth),new Rectangle(Width-edgeWidth,edgeWidth,edgeWidth,Height-2*edgeWidth),new Rectangle(edgeWidth,0,Width-2*edgeWidth,edgeWidth),new Rectangle(edgeWidth,Height-edgeWidth,Width-2*edgeWidth,edgeWidth),new Rectangle(0,0,edgeWidth,edgeWidth),new Rectangle(Width-edgeWidth,0,edgeWidth,edgeWidth),new Rectangle(0,Height-edgeWidth,edgeWidth,edgeWidth),new Rectangle(Width-edgeWidth,Height-edgeWidth,edgeWidth,edgeWidth)};
        for(int i=0;i<edges.Count;i++){edges[i].Bounds=rects[i];edges[i].Visible=!full&&WindowState==FormWindowState.Normal;edges[i].BringToFront();}
        ResizeSide();UpdateChromeVisibility();
    }
    void UpdateChromeVisibility(Point? pointer=null)
    {
        var p=pointer??PointToClient(Cursor.Position);bool inside=ClientRectangle.Contains(p);var now=DateTime.UtcNow;
        if(lastPointer!=p){lastPointer=p;lastPointerMove=now;}
        bool onVolume=volumePopup.Visible&&volumePopup.Bounds.Contains(p),onButton=bottom.Visible&&volume.Bounds.Contains(bottom.PointToClient(PointToScreen(p)));
        if(onVolume||onButton)lastVolumeHover=now;
        if(volumePopup.Visible&&!volumeSlider.Dragging&&!onVolume&&!onButton&&now>volumeFeedbackUntil&&(now-lastVolumeHover).TotalMilliseconds>350)volumePopup.Visible=false;
        bool hold=bottom.Visible&&bottom.Bounds.Contains(p)||sideOpen&&side.Visible&&side.Bounds.Contains(p)||volumePopup.Visible&&onVolume;
        bool visible=inside&&(!full||(now-lastPointerMove).TotalSeconds<2.2||hold);
        bottom.Visible=visible;header.Visible=visible&&!full;side.Visible=sideOpen&&(!full||visible);
        bottomShade.Visible=visible;topShade.Visible=visible&&!full;
        if(!visible)volumePopup.Visible=false;
    }
    void ShowVolumeFlyout(){if(!bottom.Visible)return;lastVolumeHover=DateTime.UtcNow;volumePopup.Visible=true;volumePopup.BringToFront();}
    void Tick()
    {
        UpdateChromeVisibility();if(player==null)return;player.Poll();
        // mpv creates a child HWND on its render thread. Disabling only its input routes mouse/drop to our parent, while video rendering remains active.
        Native.EnumChildWindows(video.Handle,(h,_)=>{Native.EnableWindow(h,false);return true;},IntPtr.Zero);
        position=player.Number("time-pos");duration=player.Number("duration");
        if(!seek.Dragging){seek.Value=position;seek.Duration=duration;SyncEditMarkers();}
        time.Text=ClientSize.Width<600?Clock(position):$"{Clock(position)} / {Clock(duration)}";UpdateControls();
        if(current!=null&&!shuttle&&player.Flag("eof-reached")&&!eofHandled){eofHandled=true;if(repeat=="one"){player.Seek(0);player.Set("pause","no");eofHandled=false;}else if(index+1<playlist.Count||repeat=="all")Next(1);}
        if((DateTime.UtcNow-lastSave).TotalSeconds>15){Remember();prefs.Save();lastSave=DateTime.UtcNow;}
    }
    void UpdateControls(){if(player==null)return;play.Glyph=player.Flag("pause")||current==null?"\uE768":"\uE769";play.Invalidate();volume.Glyph=player.Flag("mute")?"\uE74F":"\uE767";volume.Invalidate();volumeState.Text=player.Flag("mute")?"MUTE":$"{player.Number("volume"):0}%";if(!volumeSlider.Dragging)volumeSlider.Level=(int)Math.Round(player.Number("volume"));speed.Text=$"{player.Number("speed",1):0.##}×";speed.Invalidate();}
    public static string Clock(double t)=>t>=3600?TimeSpan.FromSeconds(Math.Max(0,t)).ToString(@"hh\:mm\:ss"):TimeSpan.FromSeconds(Math.Max(0,t)).ToString(@"mm\:ss");
    void OnLoaded()
    {
        eofHandled=false;video.Empty=false;video.Invalidate();player?.Set("pause","no");player?.Set("speed",1);
        if(current!=null&&prefs.Positions.TryGetValue(current,out var p)&&p>3&&p<player!.Number("duration")-3)player.Seek(p);
        status.Text="재생 중";
        TrySizeFirstVideo();
    }
    void SyncEditMarkers(){bool editing=sideOpen&&sideMode=="edit";seek.MarkIn=editing?markIn:-1;seek.MarkOut=editing?markOut:-1;seek.Invalidate();}
    void TrySizeFirstVideo(){if(firstVideoSized||info==null)return;firstVideoSized=true;if(!full)SetVideoWindowScale(1);}
    void ShowWindowSizes(){var m=Menu();foreach(var factor in new[]{0.5,0.75,1.0,1.5,2.0}){var scale=factor;var item=m.Items.Add(scale==1?"원본 크기 · 1×":$"{scale:0.##}× 크기",null,(_,_)=>SetVideoWindowScale(scale));item.Enabled=info!=null;}m.Show(windowSize,new Point(0,-m.PreferredSize.Height));}
    void SetVideoWindowScale(double scale)
    {
        if(info==null||info.Width<=0||info.Height<=0)return;if(full)Fullscreen();
        WindowState=FormWindowState.Normal;var area=Screen.FromControl(this).WorkingArea;
        double width=info.Width*scale,height=info.Height*scale;int extra=sideOpen?340:0;
        double fit=Math.Min(1,Math.Min((area.Width-extra)/width,area.Height/height));
        ClientSize=new((int)Math.Round(width*fit)+extra,(int)Math.Round(height*fit));
        Location=new(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
        player?.Set("video-unscaled","no");player?.Set("panscan",0);player?.Set("video-zoom",0);
        status.Text=$"창 크기 {scale:0.##}× · {video.Width} × {video.Height}";
    }
    void Remember(){if(player==null)return;prefs.Volume=player.Number("volume",65);prefs.Playlist=playlist.Take(1000).ToList();if(current!=null&&position>0)prefs.Positions[current]=position;if(prefs.Positions.Count>500)prefs.Positions=prefs.Positions.TakeLast(500).ToDictionary();}
    async void PlayFile(string file)
    {
        if(player==null)return;if(exportBusy){status.Text="편집 작업을 완료하거나 취소한 뒤 다른 영상을 열어 주세요.";return;}if(!File.Exists(file)){Error(new FileNotFoundException("파일을 찾을 수 없습니다.",file));return;}
        Remember();StopShuttle();current=file;index=playlist.IndexOf(file);markIn=markOut=-1;info=null;eofHandled=false;position=duration=0;
        title.Text=Path.GetFileName(file)+"   ·   MooVideo";Text=title.Text;status.Text="불러오는 중…";
        player.Command("loadfile",file,"replace");probeToken?.Cancel();probeToken=new();var ct=probeToken.Token;
        try{var result=await MediaTools.Probe(file,ct);if(ct.IsCancellationRequested||current!=file)return;info=result;TrySizeFirstVideo();status.Text=$"{result.Width} × {result.Height}  ·  {result.Fps:0.###} fps  ·  {result.Codec.ToUpperInvariant()}";if(sideMode=="info")BuildInfo();if(sideMode=="edit")BuildEditor();if(sideMode=="playlist")BuildLibrary();}
        catch(OperationCanceledException){}catch(Exception ex){if(!closing)status.Text="정보 읽기 실패: "+ex.Message.Split('\n')[0];}
    }
    void OpenFiles(){using var d=new OpenFileDialog{Title="영상 열기",Multiselect=true,Filter="미디어 파일|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.mxf;*.m4v;*.ts;*.mpg;*.wmv;*.mp3;*.wav;*.flac|모든 파일|*.*"};if(d.ShowDialog(this)==DialogResult.OK)AddFiles(d.FileNames,true);}
    public void AddFiles(IEnumerable<string> paths,bool playNow)
    {
        var accepted=new List<string>();foreach(var p in paths){if(Directory.Exists(p)){try{accepted.AddRange(Directory.EnumerateFiles(p).Where(f=>MediaTools.Extensions.Contains(Path.GetExtension(f))).Order(StringComparer.OrdinalIgnoreCase));}catch(Exception ex){Error(ex);}}else if(File.Exists(p))accepted.Add(Path.GetFullPath(p));}
        accepted=accepted.Distinct(StringComparer.OrdinalIgnoreCase).ToList();foreach(var p in accepted)if(!playlist.Contains(p,StringComparer.OrdinalIgnoreCase))playlist.Add(p);
        if(playNow&&accepted.Count>0)PlayFile(accepted[0]);else status.Text=$"{accepted.Count}개를 재생목록에 추가했습니다";
        if(sideMode=="playlist")BuildLibrary();
    }
    void TogglePlay(){if(player==null)return;if(current==null){OpenFiles();return;}StopShuttle();if(player.Flag("eof-reached")){player.Seek(0);player.Set("pause","no");eofHandled=false;}else player.Command("cycle","pause");}
    void Next(int delta){if(playlist.Count==0)return;var next=index+delta;if(next<0)next=playlist.Count-1;if(next>=playlist.Count)next=0;PlayFile(playlist[next]);}
    void Frame(int direction){if(current==null)return;StopShuttle();player?.Set("pause","yes");player?.Command(direction>0?"frame-step":"frame-back-step");}
    void StopShuttle(){wheelTimer.Stop();shuttle=false;wheelBurst=0;player?.Set("speed",1);}
    void CycleRepeat(){repeat=repeat=="off"?"one":repeat=="one"?"all":"off";loop.Selected=repeat!="off";loop.Invalidate();tips.SetToolTip(loop,"반복: "+(repeat=="off"?"끔":repeat=="one"?"한 곡":"목록"));status.Text="반복: "+(repeat=="off"?"끔":repeat=="one"?"한 곡":"목록");}
    void ShowSpeed(){var m=Menu();foreach(var rate in new[]{0.25,0.5,0.75,1,1.25,1.5,2,3,4}){var r=rate;m.Items.Add($"{r}×",null,(_,_)=>{StopShuttle();player?.Set("speed",r);});}m.Show(speed,new Point(0,-m.PreferredSize.Height));}
    new ContextMenuStrip Menu(){var m=new ContextMenuStrip{BackColor=Theme.Panel,ForeColor=Theme.Text,ShowImageMargin=false};m.Closed+=(_,_)=>BeginInvoke(m.Dispose);return m;}
    void Maximize(){WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
    void Fullscreen(){if(!full){restoreBounds=Bounds;restoreState=WindowState;WindowState=FormWindowState.Normal;full=true;Bounds=Screen.FromControl(this).Bounds;}else{full=false;Bounds=restoreBounds;WindowState=restoreState;}lastPointerMove=DateTime.MinValue;volumePopup.Visible=false;LayoutPlayer();}
    void OriginalWindowSize()=>SetVideoWindowScale(1);
    void Screenshot(){if(current==null)return;using var d=new SaveFileDialog{Title="현재 프레임 저장",Filter="PNG 이미지|*.png",FileName=Path.GetFileNameWithoutExtension(current)+"_"+position.ToString("0.000",CultureInfo.InvariantCulture)+".png"};if(d.ShowDialog(this)==DialogResult.OK){player?.Command("screenshot-to-file",d.FileName,"video");status.Text="프레임 저장 완료";}}
    void ShowTracks()
    {
        if(player==null)return;var m=Menu();m.Items.Add("외부 자막 열기…",null,(_,_)=>{using var d=new OpenFileDialog{Filter="자막|*.srt;*.ass;*.ssa;*.vtt;*.sub|모든 파일|*.*"};if(d.ShowDialog(this)==DialogResult.OK)Safe(()=>player.Command("sub-add",d.FileName,"select"));});
        m.Items.Add("자막 끄기",null,(_,_)=>player.Set("sid","no"));m.Items.Add(new ToolStripSeparator());
        var json=player.Get("track-list");if(json.Length>0){using var doc=JsonDocument.Parse(json);foreach(var t in doc.RootElement.EnumerateArray()){var type=t.GetProperty("type").GetString();if(type is not ("audio" or "sub"))continue;var id=t.GetProperty("id").GetInt32();string Read(string k)=>t.TryGetProperty(k,out var p)?p.ToString():"";m.Items.Add($"{(type=="audio"?"오디오":"자막")} {id}   {Read("lang")} {Read("title")}",null,(_,_)=>player.Set(type=="audio"?"aid":"sid",id));}}
        m.Items.Add(new ToolStripSeparator());m.Items.Add("자막 0.1초 늦게",null,(_,_)=>player.Command("add","sub-delay","0.1"));m.Items.Add("자막 0.1초 빠르게",null,(_,_)=>player.Command("add","sub-delay","-0.1"));
        m.Show(Cursor.Position);
    }
    void ShowContext(){var m=Menu();m.Items.Add("파일 열기",null,(_,_)=>OpenFiles());m.Items.Add("재생 / 일시정지",null,(_,_)=>TogglePlay());m.Items.Add("화면에 맞춤",null,(_,_)=>{player?.Set("video-unscaled","no");player?.Set("panscan",0);player?.Set("video-zoom",0);});m.Items.Add("화면 채우기",null,(_,_)=>player?.Set("panscan",1));m.Items.Add("원본 크기로 창 조절",null,(_,_)=>OriginalWindowSize());m.Items.Add("항상 위",null,(_,_)=>TopMost=!TopMost);m.Items.Add("전체화면",null,(_,_)=>Fullscreen());m.Items.Add("파일 정보",null,(_,_)=>ToggleSide("info"));m.Items.Add(new ToolStripSeparator());m.Items.Add("영상 기본 앱 설정",null,(_,_)=>Safe(WindowsIntegration.OpenDefaultApps));m.Items.Add("업데이트 확인",null,(_,_)=>_=CheckForUpdates(true));m.Items.Add(new ToolStripSeparator());m.Items.Add("앱 종료",null,(_,_)=>Close());m.Show(Cursor.Position);}
    async Task CheckForUpdates(bool manual)
    {
        if(checkingUpdate||closing||(!manual&&DateTime.UtcNow-prefs.LastUpdateCheckUtc<TimeSpan.FromDays(1)))return;
        checkingUpdate=true;
        try
        {
            if(manual)status.Text="업데이트를 확인하는 중…";
            var available=await Updates.CheckAsync();
            if(closing)return;
            prefs.LastUpdateCheckUtc=DateTime.UtcNow;
            if(available==null){if(manual)MessageBox.Show(this,"최신 버전입니다.","MooVideo",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
            status.Text=$"MooVideo {available.Version.ToString(3)} 업데이트 가능";
            if(MessageBox.Show(this,$"MooVideo {available.Version.ToString(3)} 버전이 있습니다.\n다운로드하여 설치할까요?","MooVideo 업데이트",MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes)return;
            updateToken=new();var progress=new Progress<double>(fraction=>{if(!closing)status.Text=$"업데이트 다운로드 {(int)(fraction*100)}%";});
            await Updates.DownloadAndStartAsync(available,progress,updateToken.Token);
            status.Text="업데이트를 적용하기 위해 다시 시작합니다.";Close();
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(manual&&!closing)Error(new Exception("업데이트를 확인하거나 설치하지 못했습니다.\n"+ex.Message));}
        finally{checkingUpdate=false;updateToken?.Dispose();updateToken=null;}
    }
    void OnKey(object? s,KeyEventArgs e)
    {
        if(ActiveControl is TextBoxBase||ActiveControl is NumericUpDown)return;
        bool done=true;Safe(()=>{switch(e.KeyCode){case Keys.O:if(e.Control&&e.Shift)ChooseFolder();else if(e.Control)OpenFiles();else done=false;break;case Keys.Space:TogglePlay();break;case Keys.Left:if(e.Control)Next(-1);else player?.Seek(e.Shift?-10:-5,true);break;case Keys.Right:if(e.Control)Next(1);else player?.Seek(e.Shift?10:5,true);break;case Keys.Up:ChangeVolume(e.Shift?10:5);break;case Keys.Down:ChangeVolume(e.Shift?-10:-5);break;case Keys.Oemcomma:Frame(-1);break;case Keys.OemPeriod:Frame(1);break;case Keys.M:player?.Command("cycle","mute");break;case Keys.F11:case Keys.F:Fullscreen();break;case Keys.D0:if(e.Control)OriginalWindowSize();else done=false;break;case Keys.Escape:if(full)Fullscreen();else if(side.Visible)ToggleSide(sideMode);break;case Keys.P:ToggleSide("playlist");break;case Keys.E:ToggleSide("edit");break;case Keys.I:ToggleSide("info");break;case Keys.S:Screenshot();break;case Keys.Home:player?.Seek(0);break;case Keys.OemOpenBrackets:markIn=position;status.Text="시작점 지정: "+Clock(position);break;case Keys.OemCloseBrackets:markOut=position;status.Text="끝점 지정: "+Clock(position);break;default:done=false;break;}});if(done){e.Handled=true;e.SuppressKeyPress=true;}
    }
    void ChangeVolume(double d){if(player==null)return;var v=Math.Clamp(player.Number("volume")+d,0,100);player.Set("volume",v);player.Set("mute","no");UpdateControls();}
    public bool PreFilterMessage(ref Message m)
    {
        if(m.Msg==0x207&&Native.GetForegroundWindow()==Handle){var point=PointToClient(Cursor.Position);if(ClientRectangle.Contains(point)&&!(side.Visible&&side.Bounds.Contains(point))){Safe(Fullscreen);return true;}}
        if(m.Msg==0x201&&!full&&WindowState==FormWindowState.Normal&&Native.GetForegroundWindow()==Handle){
            var hit=PointToClient(Cursor.Position);if(ClientRectangle.Contains(hit)){int edge=10;bool l=hit.X<edge,r=hit.X>=Width-edge,t=hit.Y<edge,b=hit.Y>=Height-edge;int code=t&&l?13:t&&r?14:b&&l?16:b&&r?17:l?10:r?11:t?12:b?15:0;
                if(code!=0){Native.ReleaseCapture();Native.SendMessage(Handle,0xA1,(IntPtr)code,IntPtr.Zero);return true;}
            }
        }
        if(m.Msg!=0x20A||Native.GetForegroundWindow()!=Handle||player==null)return false;
        var p=PointToClient(Cursor.Position);if(!ClientRectangle.Contains(p)||side.Visible&&side.Bounds.Contains(p))return false;
        var delta=(short)(((long)m.WParam>>16)&0xffff);Safe(()=>Wheel(delta,p));return true;
    }
    public void Wheel(int delta,Point p)
    {
        if(delta==0)return;if(p.X<video.Right/2){ChangeVolume(Math.Sign(delta)*2);volumeFeedbackUntil=DateTime.UtcNow.AddMilliseconds(1300);lastPointerMove=DateTime.UtcNow;UpdateChromeVisibility(p);ShowVolumeFlyout();return;}if(current==null)return;
        var now=DateTime.UtcNow;var gap=(now-lastWheel).TotalMilliseconds;var direction=Math.Sign(delta);wheelBurst=gap<150&&direction==lastDirection?wheelBurst+1:1;lastWheel=now;lastDirection=direction;
        if(!shuttle)shuttleResume=!player!.Flag("pause");shuttle=true;player!.Set("pause","yes");
        if(wheelBurst<3){player.Command(direction>0?"frame-step":"frame-back-step");status.Text=direction>0?"다음 프레임":"이전 프레임";}
        else{double rate=Math.Clamp(wheelBurst*0.65,2,16);double seconds=Math.Max(1/Math.Max(1,info?.Fps??30),Math.Clamp(gap/1000,0.015,0.15)*rate);player.Seek(direction*seconds,true);status.Text=$"{(direction>0?"앞으로":"뒤로")} {rate:0.#}× 탐색";}
        wheelTimer.Stop();wheelTimer.Start();
    }
    void DragEnterFiles(object? s,DragEventArgs e){e.Effect=e.Data?.GetDataPresent(DataFormats.FileDrop)==true?DragDropEffects.Copy:DragDropEffects.None;}
    void DragOverFiles(object? s,DragEventArgs e){DragEnterFiles(s,e);var p=PointToClient(new Point(e.X,e.Y));title.Text=p.Y<video.Height/3?"＋ 놓으면 재생목록에 추가":"▶ 놓으면 즉시 재생";}
    void DropFiles(object? s,DragEventArgs e){if(e.Data?.GetData(DataFormats.FileDrop) is string[] files){var p=PointToClient(new Point(e.X,e.Y));AddFiles(files,p.Y>=video.Height/3);title.Text=current==null?"MooVideo":Path.GetFileName(current)+"   ·   MooVideo";}}
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);if(m.Msg==0x84&&!full&&WindowState==FormWindowState.Normal){var pt=PointToClient(new Point((short)((long)m.LParam&0xffff),(short)(((long)m.LParam>>16)&0xffff)));int edge=10;bool l=pt.X<edge,r=pt.X>=Width-edge,t=pt.Y<edge,b=pt.Y>=Height-edge;m.Result=(IntPtr)(t&&l?13:t&&r?14:b&&l?16:b&&r?17:l?10:r?11:t?12:b?15:(int)m.Result);}
    }
    void OnClosing(object? sender,FormClosingEventArgs e)
    {
        if(exportBusy){MessageBox.Show(this,"작업을 취소하거나 완료한 뒤 닫아 주세요.","MooVideo");e.Cancel=true;return;}
        closing=true;Remember();prefs.Save();timer.Stop();wheelTimer.Stop();probeToken?.Cancel();updateToken?.Cancel();libraryToken?.Cancel();keyToken?.Cancel();Application.RemoveMessageFilter(this);player?.Dispose();player=null;
        timer.Dispose();wheelTimer.Dispose();tips.Dispose();
    }
}
