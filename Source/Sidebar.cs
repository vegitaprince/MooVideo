using System.Text;
using System.Text.Json;
namespace MooVideo;
public sealed partial class PlayerForm
{
    Control? sideContent;FlowLayoutPanel? tiles;NativeExplorer? nativeFiles;TextBox? searchBox,pathBox;Label? libraryCount; TrackBar? thumbSlider;
    CancellationTokenSource? libraryToken,keyToken,exportToken,populateToken;bool exportBusy;string filter="";int libraryPage;
    List<double> keyframes=new();string keyFile="";ComboBox? operation;TimeField? inBox,outBox;Label? keyLabel,jobLabel;ToolButton? exportButton;string? audioFile;Label? audioLabel;ProgressBar? progressBar;
    void ToggleSide(string mode)
    {
        if(exportBusy){status.Text="진행 중인 편집 작업을 완료하거나 취소하세요.";return;}
        if(sideOpen&&sideMode==mode){sideOpen=false;sideMode="";libraryToken?.Cancel();keyToken?.Cancel();}
        else {sideMode=mode;sideOpen=true;if(mode is "files" or "playlist")BuildLibrary();else if(mode=="edit")BuildEditor();else BuildInfo();}
        MinimumSize=sideOpen?new Size(680,500):new Size(320,180);SyncEditMarkers();
        playlistButton.Selected=sideMode=="playlist";folderButton.Selected=sideMode=="files";editButton.Selected=sideMode=="edit";
        playlistButton.Invalidate();folderButton.Invalidate();editButton.Invalidate();LayoutPlayer();
    }
    void ClearSide(string heading,string eyebrow)
    {
        libraryToken?.Cancel();populateToken?.Cancel();keyToken?.Cancel();foreach(Control c in side.Controls.Cast<Control>().ToArray()){side.Controls.Remove(c);c.Dispose();}
        tiles=null;nativeFiles=null;sideContent=null;searchBox=null;pathBox=null;thumbSlider=null;libraryCount=null;keyLabel=null;jobLabel=null;progressBar=null;operation=null;inBox=null;outBox=null;exportButton=null;
        var upper=Theme.Label(eyebrow,8,Theme.Muted);upper.SetBounds(22,18,260,20);side.Controls.Add(upper);
        var h=Theme.Label(heading,19);h.Tag="sideHeading";h.SetBounds(20,40,Math.Max(80,side.Width-104),42);side.Controls.Add(h);
        var close=Button("\uE8BB","패널 닫기",()=>ToggleSide(sideMode));close.Tag="sideClose";close.SetBounds(Math.Max(0,side.Width-48),20,36,36);side.Controls.Add(close);
    }
    void ResizeSide(){
        foreach(var close in side.Controls.OfType<ToolButton>().Where(c=>Equals(c.Tag,"sideClose")))close.SetBounds(Math.Max(0,side.Width-48),20,36,36);
        foreach(var heading in side.Controls.OfType<Label>().Where(c=>Equals(c.Tag,"sideHeading")))heading.Width=Math.Max(80,side.Width-104);
        if(sideContent!=null){sideContent.SetBounds(8,sideContent.Top,Math.Max(1,side.Width-16),Math.Max(1,side.Height-sideContent.Top-(sideMode=="edit"?124:14)));
            if(sideMode=="edit"&&side.Width>300){int limit=side.Width-16-SystemInformation.VerticalScrollBarWidth-12;foreach(Control c in sideContent.Controls)if(c.Right>limit)c.Width=Math.Max(28,limit-c.Left);}
        }foreach(var footer in side.Controls.OfType<Panel>().Where(c=>Equals(c.Tag,"editorFooter")))footer.SetBounds(8,side.Height-112,Math.Max(1,side.Width-16),108);ResizeTiles();
    }
    void ChooseFolder()
    {
        using var d=new FolderBrowserDialog{Description="영상 폴더 선택",UseDescriptionForTitle=true,InitialDirectory=prefs.Folder};
        if(d.ShowDialog(this)==DialogResult.OK){prefs.Folder=d.SelectedPath;filter="";libraryPage=0;if(sideMode!="files")ToggleSide("files");else BuildLibrary();}
    }
    void BuildLibrary()
    {
        var mode=sideMode;ClearSide(mode=="files"?"파일 탐색":"재생목록",mode=="files"?"LIBRARY":"PLAY QUEUE");
        libraryToken=new();var ct=libraryToken.Token;
        var add=Button("\uE710",mode=="files"?"폴더 선택":"파일 추가",()=>{if(mode=="files")ChooseFolder();else {using var d=new OpenFileDialog{Multiselect=true,Filter="모든 미디어|*.*"};if(d.ShowDialog(this)==DialogResult.OK)AddFiles(d.FileNames,false);}});add.SetBounds(20,90,34,32);side.Controls.Add(add);
        if(mode=="files") {
            var up=Button("\uE74A","상위 폴더",()=>{var p=Directory.GetParent(prefs.Folder);if(p!=null){prefs.Folder=p.FullName;libraryPage=0;BuildLibrary();}});up.SetBounds(58,90,34,32);side.Controls.Add(up);
            pathBox=Theme.Input(prefs.Folder);pathBox.SetBounds(100,94,177,28);side.Controls.Add(pathBox);tips.SetToolTip(pathBox,"폴더 경로를 입력하고 Enter");pathBox.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;if(Directory.Exists(pathBox.Text)){prefs.Folder=Path.GetFullPath(pathBox.Text);libraryPage=0;BuildLibrary();}else status.Text="폴더를 찾을 수 없습니다.";}};
            var treeButton=Button("\uE70D","폴더 트리 열기",ShowFolderTree);treeButton.SetBounds(282,91,36,34);side.Controls.Add(treeButton);
        }else{
            var more=Button("\uE712","재생목록 저장 / 불러오기 / 비우기",ShowPlaylistMenu);more.SetBounds(58,90,34,32);side.Controls.Add(more);
            var hint=Theme.Label("더블클릭으로 재생",9,Theme.Muted);hint.SetBounds(104,90,214,32);side.Controls.Add(hint);
        }
        if(mode=="playlist"){
            searchBox=Theme.Input(filter);searchBox.PlaceholderText="이름으로 검색";searchBox.SetBounds(20,136,300,28);side.Controls.Add(searchBox);
            var debounce=new System.Windows.Forms.Timer{Interval=300};searchBox.Disposed+=(_,_)=>debounce.Dispose();searchBox.TextChanged+=(_,_)=>{filter=searchBox.Text;debounce.Stop();debounce.Start();};debounce.Tick+=(_,_)=>{debounce.Stop();libraryPage=0;PopulateLibrary(ct);};
        }
        var sizeLabel=Theme.Label(mode=="files"?"썸네일":"항목 크기",9,Theme.Muted);sizeLabel.SetBounds(20,176,58,28);side.Controls.Add(sizeLabel);
        thumbSlider=new TrackBar{Minimum=96,Maximum=280,Value=Math.Clamp(prefs.ThumbnailSize,96,280),TickStyle=TickStyle.None,BackColor=Theme.Panel,AutoSize=false};thumbSlider.SetBounds(76,176,174,28);side.Controls.Add(thumbSlider);
        thumbSlider.ValueChanged+=(_,_)=>{prefs.ThumbnailSize=thumbSlider.Value;if(mode=="files")nativeFiles?.SetThumbnailSize(prefs.ThumbnailSize);else ResizeTiles();};
        libraryCount=Theme.Label("",9,Theme.Muted);libraryCount.SetBounds(254,176,70,28);side.Controls.Add(libraryCount);
        if(mode=="files"){
            libraryCount.Text="OS";
            nativeFiles=new NativeExplorer(prefs.Folder,prefs.ThumbnailSize,paths=>AddFiles(paths,true),path=>{prefs.Folder=path;if(pathBox!=null&&!pathBox.IsDisposed)pathBox.Text=path;});
            sideContent=nativeFiles;side.Controls.Add(nativeFiles);nativeFiles.Top=220;ResizeSide();
        }else{
            tiles=new FlowLayoutPanel{AutoScroll=true,BackColor=Theme.Panel,WrapContents=true,FlowDirection=FlowDirection.LeftToRight};sideContent=tiles;side.Controls.Add(tiles);tiles.Top=220;ResizeSide();PopulateLibrary(ct);
        }
    }
    void ShowFolderTree()
    {
        var tree=new TreeView{Size=new Size(310,380),BackColor=Theme.Panel,ForeColor=Theme.Text,BorderStyle=BorderStyle.None,Font=Theme.Font(9),HideSelection=false};
        TreeNode Node(string path){var n=new TreeNode(Path.GetFileName(path.TrimEnd('\\')) is {Length:>0} name?name:path){Tag=path};n.Nodes.Add("…");return n;}
        foreach(var drive in DriveInfo.GetDrives().Where(d=>d.IsReady)){tree.Nodes.Add(Node(drive.RootDirectory.FullName));}
        tree.BeforeExpand+=(_,e)=>{var n=e.Node;if(n?.Tag is not string path)return;n.Nodes.Clear();try{foreach(var child in Directory.EnumerateDirectories(path).Order(StringComparer.OrdinalIgnoreCase))n.Nodes.Add(Node(child));}catch(UnauthorizedAccessException){n.Nodes.Add("접근할 수 없음");}catch(IOException){n.Nodes.Add("읽을 수 없음");}};
        var menu=Menu();var host=new ToolStripControlHost(tree){Margin=Padding.Empty,Padding=Padding.Empty,AutoSize=false,Size=tree.Size};menu.Items.Add(host);
        tree.NodeMouseDoubleClick+=(_,e)=>{if(e.Node?.Tag is string path&&Directory.Exists(path)){prefs.Folder=path;filter="";libraryPage=0;menu.Close();BuildLibrary();}};
        var currentDrive=Path.GetPathRoot(prefs.Folder);var selected=tree.Nodes.Cast<TreeNode>().FirstOrDefault(n=>string.Equals(n.Tag as string,currentDrive,StringComparison.OrdinalIgnoreCase));
        if(selected!=null&&currentDrive!=null){var rest=prefs.Folder[currentDrive.Length..].Split(Path.DirectorySeparatorChar,StringSplitOptions.RemoveEmptyEntries);foreach(var part in rest){var parent=selected;if(parent==null)break;parent.Expand();selected=parent.Nodes.Cast<TreeNode>().FirstOrDefault(n=>n.Text.Equals(part,StringComparison.OrdinalIgnoreCase));}if(selected!=null){selected.EnsureVisible();tree.SelectedNode=selected;}}
        menu.Show(side, new Point(Math.Max(0,side.ClientSize.Width-tree.Width-8),125));
    }
    void PopulateLibrary(CancellationToken parent)
    {
        if(tiles==null||parent.IsCancellationRequested)return;
        populateToken?.Cancel();populateToken=CancellationTokenSource.CreateLinkedTokenSource(parent);parent=populateToken.Token;
        var target=tiles;target.SuspendLayout();foreach(Control c in target.Controls.Cast<Control>().ToArray()){target.Controls.Remove(c);c.Dispose();}target.ResumeLayout();
        try{
            var entries=playlist.ToList();
            if(parent.IsCancellationRequested||target.IsDisposed)return;
            entries=entries.Where(f=>Path.GetFileName(f).Contains(filter,StringComparison.OrdinalIgnoreCase)).ToList();libraryCount!.Text=$"{entries.Count}개";
            var page=entries.Skip(libraryPage*100).Take(100).ToList();
            target.SuspendLayout();
            try{foreach(var path in page){bool dir=Directory.Exists(path);var tile=new Panel{BackColor=path==current?Theme.Field:Theme.Panel,Margin=new Padding(4),Tag=path,Cursor=Cursors.Hand};
                var pic=new PictureBox{SizeMode=PictureBoxSizeMode.Zoom,BackColor=Theme.Field,Tag="thumb"};tile.Controls.Add(pic);pic.Disposed+=(_,_)=>pic.Image?.Dispose();
                pic.Paint+=(_,e)=>{if(pic.Image!=null)return;using var icon=new Font("Segoe Fluent Icons",22);Theme.DrawText(e.Graphics,dir?"\uE8B7":"\uE714",icon,Theme.Muted,pic.ClientRectangle,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);};
                var name=Theme.Label((dir?"▸ ":"")+Path.GetFileName(path),9,path==current?Theme.AccentInk:Theme.Text);name.Tag="name";name.AutoEllipsis=true;tile.Controls.Add(name);tips.SetToolTip(name,path);
                var detail=Theme.Label(dir?"폴더":Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),8,Theme.Muted);detail.Tag="detail";tile.Controls.Add(detail);
                foreach(var c in new Control[]{tile,pic,name,detail}){c.DoubleClick+=(_,_)=>{if(dir){prefs.Folder=path;libraryPage=0;BuildLibrary();}else AddFiles(new[]{path},true);};c.MouseUp+=(_,e)=>{if(e.Button==MouseButtons.Right)TileMenu(path,dir);};}
                target.Controls.Add(tile);
            }
            if(entries.Count>100){var pager=new FlowLayoutPanel{Width=290,Height=40};if(libraryPage>0)pager.Controls.Add(Button("\uE76B","이전 페이지",()=>{libraryPage--;PopulateLibrary(libraryToken!.Token);}));var pg=Theme.Label($"{libraryPage+1} / {(entries.Count+99)/100}",9);pg.Size=new(130,34);pager.Controls.Add(pg);if((libraryPage+1)*100<entries.Count)pager.Controls.Add(Button("\uE76C","다음 페이지",()=>{libraryPage++;PopulateLibrary(libraryToken!.Token);}));target.Controls.Add(pager);}}
            finally{target.ResumeLayout(false);}
            ResizeTiles();target.PerformLayout();
        }catch(OperationCanceledException){}catch(Exception ex){if(!closing)status.Text="목록 표시 실패: "+ex.Message;}
    }
    void ResizeTiles()
    {
        if(tiles==null)return;var size=prefs.ThumbnailSize;
        foreach(var tile in tiles.Controls.OfType<Panel>().Where(t=>t.Tag is string)){
            var w=Math.Min(size,Math.Max(80,tiles.ClientSize.Width-26));var h=(int)(w*0.56);tile.Size=new(w,h+52);
            foreach(Control c in tile.Controls){if((string?)c.Tag=="thumb")c.SetBounds(4,4,w-8,h);if((string?)c.Tag=="name")c.SetBounds(6,h+6,w-12,23);if((string?)c.Tag=="detail")c.SetBounds(6,h+29,w-12,18);}
        }
    }
    void TileMenu(string path,bool directory)
    {
        var m=Menu();m.Items.Add(directory?"폴더 열기":"지금 재생",null,(_,_)=>{if(directory){prefs.Folder=path;BuildLibrary();}else AddFiles(new[]{path},true);});m.Items.Add("재생목록에 추가",null,(_,_)=>AddFiles(new[]{path},false));
        if(sideMode=="playlist"){
            m.Items.Add("목록에서 제거",null,(_,_)=>{playlist.Remove(path);index=current==null?-1:playlist.IndexOf(current);BuildLibrary();});
            foreach(var delta in new[]{-1,1}){var d=delta;m.Items.Add(d<0?"위로 이동":"아래로 이동",null,(_,_)=>{var i=playlist.IndexOf(path);var j=i+d;if(i>=0&&j>=0&&j<playlist.Count){(playlist[i],playlist[j])=(playlist[j],playlist[i]);index=current==null?-1:playlist.IndexOf(current);BuildLibrary();}});}
        }m.Show(Cursor.Position);
    }
    void ShowPlaylistMenu()
    {
        var m=Menu();m.Items.Add("재생목록 저장 (.m3u8)",null,(_,_)=>{using var d=new SaveFileDialog{Filter="재생목록|*.m3u8",FileName="MooVideo.m3u8"};if(d.ShowDialog(this)==DialogResult.OK)Safe(()=>File.WriteAllLines(d.FileName,new[]{"#EXTM3U"}.Concat(playlist),new UTF8Encoding(false)));});
        m.Items.Add("재생목록 불러오기",null,(_,_)=>{using var d=new OpenFileDialog{Filter="재생목록|*.m3u;*.m3u8"};if(d.ShowDialog(this)==DialogResult.OK)Safe(()=>AddFiles(File.ReadAllLines(d.FileName).Where(l=>!string.IsNullOrWhiteSpace(l)&&!l.StartsWith('#')).Select(l=>Path.IsPathRooted(l)?l:Path.Combine(Path.GetDirectoryName(d.FileName)!,l)),false));});
        m.Items.Add("목록 비우기",null,(_,_)=>{playlist.Clear();index=-1;BuildLibrary();});m.Show(Cursor.Position);
    }
    void BuildInfo()
    {
        ClearSide("영상 정보","INSPECT");var text=new StringBuilder();
        if(current!=null){text.AppendLine(Path.GetFileName(current)).AppendLine();if(info!=null){text.AppendLine($"{info.Width} × {info.Height}  ·  {info.Fps:0.###} fps").AppendLine($"{Clock(info.Duration)}  ·  {info.Codec.ToUpperInvariant()}").AppendLine();foreach(var s in info.Streams.EnumerateArray()){string Get(string n)=>s.TryGetProperty(n,out var p)?p.ToString():"";text.AppendLine($"#{Get("index")}  {Get("codec_type")}  {Get("codec_name")}").AppendLine($"{Get("sample_rate")} {Get("channel_layout")}");}}text.AppendLine(current).AppendLine();}
        text.AppendLine("재생 / 탐색").AppendLine("Space / 영상 더블클릭    재생 · 일시정지").AppendLine("← / →    5초 (Shift: 10초)").AppendLine(", / .    이전 · 다음 프레임").AppendLine("Ctrl + ← / →    이전 · 다음 영상").AppendLine("↑ / ↓    음량 5% (Shift: 10%)").AppendLine("M    음소거").AppendLine("F11 / 휠 클릭    전체화면").AppendLine("Ctrl + 0    원본 크기로 창 조절").AppendLine("S    현재 프레임 PNG 저장").AppendLine("[ / ]    편집 시작 · 끝 지정").AppendLine().AppendLine("휠 제어").AppendLine("화면 왼쪽 절반: 볼륨").AppendLine("화면 오른쪽 절반: 프레임 탐색").AppendLine("빠르게 스크롤: 가속 탐색 (최대 16×)").AppendLine("멈춘 뒤: 이전 재생·정지 상태로 복귀").AppendLine().AppendLine("전체화면에서 마우스를 움직이면 조작부 표시").AppendLine().AppendLine("드래그 앤 드롭").AppendLine("화면 위 1/3: 목록에 추가").AppendLine("화면 중·하단: 즉시 재생").AppendLine().AppendLine("MooVideo 0.5.1").AppendLine(player?.Get("mpv-version")).AppendLine("FFmpeg · libmpv").AppendLine("H.264 / HEVC / AV1 / VP9 / ProRes").AppendLine("DNxHD / HAP / HAP Q / NotchLC").AppendLine().AppendLine("재생 엔진에 포함된 디코더를 사용합니다.").AppendLine("HAP·NotchLC의 실시간 성능은 해상도와").AppendLine("장치 성능에 따라 달라집니다.");
        var defaults=Button("","영상 기본 앱 설정",WindowsIntegration.OpenDefaultApps,true);defaults.SetBounds(20,94,300,36);side.Controls.Add(defaults);
        var check=Button("","업데이트 확인",()=>_=CheckForUpdates(true));check.SetBounds(20,136,300,36);side.Controls.Add(check);
        var box=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=Theme.Panel,ForeColor=Theme.Text,Font=Theme.Font(10),Text=text.ToString(),Dock=DockStyle.Fill};
        sideContent=new Panel{Top=186,Padding=new Padding(8)};sideContent.Controls.Add(box);side.Controls.Add(sideContent);ResizeSide();
    }
    void BuildEditor()
    {
        if(exportBusy)return;ClearSide("무손실 편집","STREAM COPY");
        sideContent=new Panel{Top=96,AutoScroll=true};side.Controls.Add(sideContent);ResizeSide();var p=sideContent;
        void LabelAt(string s,int y,int h=26,Color? c=null){var l=Theme.Label(s,9,c??Theme.Muted);l.SetBounds(8,y,298,h);p.Controls.Add(l);}
        if(current==null){LabelAt("먼저 영상을 열어 주세요.",10);return;}
        LabelAt(Path.GetFileName(current),0,38,Theme.Text);
        operation=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Theme.Field,ForeColor=Theme.Text,FlatStyle=FlatStyle.Flat};operation.Items.AddRange(new object[]{"구간 추출","현재 지점에서 둘로 분할","오디오 트랙 모두 제거","다른 오디오로 교체","오디오 트랙 추가"});operation.SetBounds(8,50,292,30);p.Controls.Add(operation);
        LabelAt("시작 / 분할 위치 (초)",94);inBox=NumberBox(markIn>=0?markIn:0);inBox.SetBounds(8,126,206,30);p.Controls.Add(inBox);var setIn=Button("","지금",()=>inBox.Value=(decimal)Math.Clamp(position,0,(double)inBox.Maximum));setIn.SetBounds(220,123,80,34);p.Controls.Add(setIn);
        LabelAt("끝 위치 (초)",166);outBox=NumberBox(markOut>0?markOut:duration);outBox.SetBounds(8,198,206,30);p.Controls.Add(outBox);var setOut=Button("","지금",()=>outBox.Value=(decimal)Math.Clamp(position,0,(double)outBox.Maximum));setOut.SetBounds(220,195,80,34);p.Controls.Add(setOut);
        keyLabel=Theme.Label("키프레임을 확인하고 있습니다…",9,Theme.AccentInk);keyLabel.SetBounds(8,242,292,70);p.Controls.Add(keyLabel);
        var audio=Button("\uE8D6","오디오 파일 선택",()=>{using var d=new OpenFileDialog{Filter="오디오 / 미디어|*.wav;*.mp3;*.m4a;*.aac;*.flac;*.ogg;*.opus;*.aiff;*.mp4;*.mov;*.mkv|모든 파일|*.*"};if(d.ShowDialog(this)==DialogResult.OK){audioFile=d.FileName;audioLabel!.Text=Path.GetFileName(audioFile);UpdateEditor();}});audio.SetBounds(8,318,36,34);p.Controls.Add(audio);
        audioLabel=Theme.Label(audioFile==null?"추가·교체할 오디오 선택":Path.GetFileName(audioFile),9);audioLabel.SetBounds(52,318,248,38);p.Controls.Add(audioLabel);
        LabelAt("원본 유지 · 영상과 오디오를 그대로 복사합니다.\n오디오 추가는 믹싱이 아닌 별도 트랙입니다.\n구간 추출 시 원본 크기만큼 임시 공간이 필요합니다.",374,72);
        var footer=new Panel{Tag="editorFooter",BackColor=Theme.Panel};side.Controls.Add(footer);
        exportButton=Button("","새 파일로 저장",()=>_=ExportFromUi(),true);exportButton.SetBounds(4,2,300,38);footer.Controls.Add(exportButton);
        progressBar=new ProgressBar{Style=ProgressBarStyle.Continuous};progressBar.SetBounds(4,46,300,5);footer.Controls.Add(progressBar);
        jobLabel=Theme.Label("",9,Theme.Muted);jobLabel.SetBounds(4,54,205,48);footer.Controls.Add(jobLabel);
        var cancel=Button("","작업 취소",()=>exportToken?.Cancel());cancel.SetBounds(212,60,92,34);footer.Controls.Add(cancel);
        inBox.ValueChanged+=(_,_)=>UpdateEditor();outBox.ValueChanged+=(_,_)=>UpdateEditor();operation.SelectedIndexChanged+=(_,_)=>UpdateEditor();operation.SelectedIndex=0;
        var file=current;keyToken=new();var ct=keyToken.Token;if(keyFile!=file){keyframes.Clear();_=LoadKeyframes(file,ct);}else UpdateEditor();ResizeSide();
    }
    TimeField NumberBox(double value)=>new(){Maximum=(decimal)Math.Max(1,duration),Value=(decimal)Math.Clamp(value,0,Math.Max(1,duration))};
    async Task LoadKeyframes(string file,CancellationToken ct)
    {
        try{var keys=await MediaTools.Keyframes(file,ct);if(ct.IsCancellationRequested||current!=file)return;keyframes=keys;keyFile=file;UpdateEditor();}
        catch(OperationCanceledException){}catch(Exception ex){if(!ct.IsCancellationRequested&&keyLabel!=null){keyLabel.Text="키프레임 분석 실패: "+ex.Message.Split('\n')[0];}}
    }
    void UpdateEditor()
    {
        if(operation==null||inBox==null||outBox==null||exportButton==null||keyLabel==null)return;
        var op=operation.SelectedIndex;bool cut=op<2;bool ready=keyFile==current&&keyframes.Count>0;
        inBox.Enabled=cut&&!exportBusy;outBox.Enabled=op==0&&!exportBusy;
        var start=MediaTools.Snap(keyframes,(double)inBox.Value);var end=MediaTools.Snap(keyframes,(double)outBox.Value);
        if(Math.Abs((double)outBox.Value-duration)<0.05)end=duration;
        keyLabel.Text=cut?(ready?$"실제 {(op==0?"추출 시작":"분할")}: {start:0.000}초"+(op==0?$"  →  {end:0.000}초":"")+"\n이전 키프레임으로 맞춥니다.\n컨테이너·오디오 경계에서 약간의 차이가 생깁니다.":"키프레임을 분석하고 있습니다…"):(op>=3?"첫 번째 오디오 트랙을 그대로 추가합니다.\n길이가 달라도 자르거나 반복하지 않습니다.\n파일 형식이 호환되지 않으면 작업이 중단됩니다.":"모든 오디오 트랙을 제거합니다.\n영상은 원본 그대로 복사됩니다.");
        bool valid=!cut||inBox.Valid&&(op!=0||outBox.Valid);
        if(!valid)keyLabel.Text=$"0~{duration:0.000} 사이의 초 단위 숫자를 입력하세요.";
        exportButton.Enabled=!exportBusy&&valid&&(!cut||ready&&(op==0?end>start:start>0&&start<duration))&&(op<3||File.Exists(audioFile));exportButton.Invalidate();
        if(cut){markIn=(double)inBox.Value;markOut=op==0?(double)outBox.Value:-1;}
    }
    async Task ExportFromUi()
    {
        if(current==null||operation==null||inBox==null||outBox==null||exportBusy)return;
        var file=current;int op=operation.SelectedIndex;double start=MediaTools.Snap(keyframes,(double)inBox.Value),end=Math.Abs((double)outBox.Value-duration)<0.05?duration:MediaTools.Snap(keyframes,(double)outBox.Value);
        using var dialog=new SaveFileDialog{Title="새 파일로 저장 · 기존 파일은 덮어쓰지 않습니다",FileName=Path.GetFileNameWithoutExtension(file)+(op==1?"_split":"_edited")+Path.GetExtension(file),Filter="원본 컨테이너|*"+Path.GetExtension(file)+"|Matroska|*.mkv",OverwritePrompt=true};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        exportBusy=true;exportToken=new();UpdateEditor();operation.Enabled=false;var ct=exportToken.Token;var job=jobLabel!;var progress=progressBar!;job.Text="원본 스트림을 복사하는 중…";
        var p=new Progress<double>(seconds=>{if(!progress.IsDisposed){progress.Value=Math.Clamp((int)(seconds/Math.Max(0.1,op==0?end-start:duration)*100),0,100);job.Text=$"복사 중  {progress.Value}%";}});
        try{
            string[] outputs;if(op==1)outputs=await MediaTools.Split(file,dialog.FileName,start,ct,seconds=>((IProgress<double>)p).Report(seconds));
            else{await MediaTools.Export(file,dialog.FileName,new[]{"trim","split","remove","replace","append"}[op],start,end,audioFile,ct,seconds=>((IProgress<double>)p).Report(seconds));outputs=new[]{dialog.FileName};}
            progress.Value=100;job.Text="저장 완료\n"+string.Join("\n",outputs.Select(Path.GetFileName));status.Text="무손실 편집 완료";
        }catch(OperationCanceledException){job.Text="작업을 취소했습니다.\n완료되지 않은 임시 파일을 정리했습니다.";progress.Value=0;}
        catch(Exception ex){job.Text="저장하지 못했습니다. 원본은 유지됩니다.";Error(new Exception("스트림을 그대로 저장하지 못했습니다.\n추가 오디오와 컨테이너가 호환되는지 확인하세요.\n\n"+ex.Message));}
        finally{exportBusy=false;operation.Enabled=true;exportToken.Dispose();exportToken=null;UpdateEditor();}
    }
}
