using System.Text.Json;
using System.Diagnostics;
namespace MooVideo;
static class SelfTest
{
    public static async Task Run(string[] args)
    {
        var dir=args.SkipWhile(a=>a!="--self-test").Skip(1).FirstOrDefault()??Path.Combine(AppContext.BaseDirectory,"test-results");Directory.CreateDirectory(dir);
        var log=new List<string>();File.WriteAllText(Path.Combine(dir,"results.txt"),"RUNNING\n");
        void Assert(bool condition,string name){if(!condition)throw new Exception(name);log.Add("PASS "+name);File.AppendAllText(Path.Combine(dir,"results.txt"),"PASS "+name+"\n");}
        async Task Gen(string[] a,string name){await MediaTools.Run("ffmpeg",new[]{"-hide_banner","-v","error","-y"}.Concat(a).Append(Path.Combine(dir,name)));}
        try {
            string src=Path.Combine(dir,"test 영상 with spaces.mp4"),audio=Path.Combine(dir,"tone.m4a");
            await Gen(new[]{"-f","lavfi","-i","testsrc2=size=640x360:rate=30","-f","lavfi","-i","sine=frequency=440:sample_rate=48000","-t","6","-c:v","libx264","-g","30","-keyint_min","30","-sc_threshold","0","-pix_fmt","yuv420p","-c:a","aac"},Path.GetFileName(src));
            await Gen(new[]{"-f","lavfi","-i","sine=frequency=880:sample_rate=48000","-t","6","-c:a","aac"},"tone.m4a");
            var original=await MediaTools.Probe(src);Assert(original.Width==640&&original.Streams.GetArrayLength()==2,"probe video + audio / Unicode path");
            var keys=await MediaTools.Keyframes(src,CancellationToken.None);Assert(keys.Count>=6&&MediaTools.Snap(keys,2.4)==2,"keyframe snapping 2.4 → 2.0");
            async Task<string[]> Hashes(string f,string selector="v:0"){
                var raw=await MediaTools.Run("ffprobe",new[]{"-v","error","-select_streams",selector,"-show_packets","-show_entries","packet=data_hash","-show_data_hash","sha256","-of","csv=p=0",f});
                return raw.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).ToArray();
            }
            var sourceHash=await Hashes(src);
            foreach(var op in new[]{"remove","replace","append","trim"}){
                var dst=Path.Combine(dir,op+".mp4");if(File.Exists(dst))File.Delete(dst);
                await MediaTools.Export(src,dst,op,2,4,audio,CancellationToken.None);var probe=await MediaTools.Probe(dst);var hashes=await Hashes(dst);
                Assert(op=="trim"?hashes.All(sourceHash.Contains):sourceHash.SequenceEqual(hashes),op+" preserves encoded video packets");
                int ac=probe.Streams.EnumerateArray().Count(s=>s.GetProperty("codec_type").GetString()=="audio");Assert(ac==(op=="remove"?0:op=="append"?2:1),op+" audio stream count");
                if(op=="replace"){var originalAudio=await Hashes(audio,"a:0");var outputAudio=await Hashes(dst,"a:0");Assert(originalAudio.SequenceEqual(outputAudio),"replacement audio packets unchanged");}
                if(op=="trim"){Assert(probe.Duration>=1.9&&probe.Duration<2.3,"trim duration within packet boundary");Assert(hashes.SequenceEqual(sourceHash.Skip(60).Take(60)),"trim starts and ends at selected keyframes");}
            }
            foreach(var f in new[]{"split_01.mp4","split_02.mp4"})if(File.Exists(Path.Combine(dir,f)))File.Delete(Path.Combine(dir,f));
            var split=await MediaTools.Split(src,Path.Combine(dir,"split.mp4"),3,CancellationToken.None);Assert(split.Length==2,"split produces two files");
            var splitHashes=new List<string>();foreach(var f in split){splitHashes.AddRange(await Hashes(f));await MediaTools.Run("ffmpeg",new[]{"-v","error","-i",f,"-f","null","-"});}Assert(sourceHash.SequenceEqual(splitHashes),"split preserves every video packet and decodes independently");var firstPart=await Hashes(split[0]);Assert(firstPart.SequenceEqual(sourceHash.Take(90)),"split occurs at requested 3-second keyframe");
            bool refused=false;try{await MediaTools.Export(src,src,"remove",0,0,null,CancellationToken.None);}catch(IOException){refused=true;}Assert(refused,"original overwrite refused");
            var updateSource=Path.Combine(dir,"update-source");var updateTarget=Path.Combine(dir,"update-target");var updateBackup=Path.Combine(dir,"update-backup");
            Directory.CreateDirectory(updateSource);Directory.CreateDirectory(updateTarget);
            File.WriteAllText(Path.Combine(updateSource,"MooVideo.exe"),"new");File.WriteAllText(Path.Combine(updateTarget,"MooVideo.exe"),"old");
            Updates.ReplaceFiles(updateSource,updateTarget,updateBackup);
            Assert(File.ReadAllText(Path.Combine(updateTarget,"MooVideo.exe"))=="new"&&File.ReadAllText(Path.Combine(updateBackup,"MooVideo.exe"))=="old","updater replaces app and backs up prior version");
            var cancelled=Path.Combine(dir,"cancelled.mp4");using(var cancel=new CancellationTokenSource()){cancel.Cancel();try{await MediaTools.Export(src,cancelled,"remove",0,0,null,cancel.Token);}catch(OperationCanceledException){}Assert(!File.Exists(cancelled),"cancel leaves no output");}
            Assert(File.Exists(Path.Combine(dir,"hap.mov"))&&File.Exists(Path.Combine(dir,"hapq.mov")),"official FFmpeg HAP fixtures available");
            await Gen(new[]{"-f","lavfi","-i","testsrc2=size=320x180:rate=30","-t","1","-c:v","prores_ks","-profile:v","2"},"prores.mov");
            await Gen(new[]{"-f","lavfi","-i","testsrc2=size=320x180:rate=30","-t","1","-c:v","libx265","-preset","ultrafast","-x265-params","log-level=error"},"hevc.mp4");
            await Gen(new[]{"-f","lavfi","-i","testsrc2=size=320x180:rate=30","-t","1","-c:v","libaom-av1","-cpu-used","8"},"av1.mkv");
            File.AppendAllText(Path.Combine(dir,"results.txt"),"INITIALIZING MPV\n");
            using var mpv=new Mpv(IntPtr.Zero,true);var decoders=mpv.Get("decoder-list");Assert(decoders.Contains("hap")&&decoders.Contains("notchlc"),"mpv includes HAP and NotchLC decoders");
            foreach(var f in new[]{src,Path.Combine(dir,"hap.mov"),Path.Combine(dir,"hapq.mov"),Path.Combine(dir,"prores.mov"),Path.Combine(dir,"hevc.mp4"),Path.Combine(dir,"av1.mkv")}.Concat(Directory.GetFiles(dir,"notch*.mov"))){
                bool loaded=false;string? err=null;void Load()=>loaded=true;void Fail(string msg)=>err=msg;mpv.Loaded+=Load;mpv.Failed+=Fail;mpv.Command("loadfile",f,"replace");mpv.Set("pause","no");var sw=Stopwatch.StartNew();while(sw.Elapsed.TotalSeconds<8&&(mpv.Number("time-pos")<0.02||!loaded)&&err==null){mpv.Poll();await Task.Delay(20);}mpv.Loaded-=Load;mpv.Failed-=Fail;
                Assert(loaded&&err==null&&mpv.Number("video-params/w")>0,"decode "+Path.GetFileName(f));
            }
            log.Add("ALL PASSED");Environment.ExitCode=0;
        }catch(Exception ex){log.Add("FAIL "+ex);Environment.ExitCode=1;}
        await File.WriteAllLinesAsync(Path.Combine(dir,"results.txt"),log);
    }
}
public sealed partial class PlayerForm
{
    async Task RunFileStartupSmoke()
    {
        var resultDir = initialArgs[Array.IndexOf(initialArgs, "--ui-smoke") + 1];
        var resultPath = Path.Combine(resultDir, "file-startup-results.txt");
        try
        {
            if (video.Empty || startupDefaults.Visible)
                throw new InvalidOperationException("Welcome screen was visible before loading the launch file.");
            AddFiles(startupFiles, true);
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(8))
            {
                if (video.Empty || startupDefaults.Visible)
                    throw new InvalidOperationException("Welcome screen appeared while loading the launch file.");
                if (playbackStarted && position > 0 && player!.Number("video-params/w") > 0) break;
                await Task.Delay(25);
            }
            if (!playbackStarted || position <= 0 || player!.Number("video-params/w") <= 0 || current != startupFiles[0])
                throw new InvalidOperationException("The launch file did not play.");
            await File.WriteAllTextAsync(resultPath, "PASS launch file played without the welcome screen\n");
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(resultPath, "FAIL " + ex);
            Environment.ExitCode = 1;
        }
        Close();
    }

    async Task RunUiSmoke()
    {
        var arg=Array.IndexOf(initialArgs,"--ui-smoke");var resultDir=initialArgs[arg+1];Directory.CreateDirectory(resultDir);var checks=new List<string>();
        void Check(bool b,string s){if(!b)throw new Exception(s);checks.Add("PASS "+s);}
        try {
            await Task.Delay(1200);Check(player!=null,"native player initialized");
            Check(startupDefaults.Visible&&startupDefaults.Text=="기본 앱 설정","startup default app button visible");
            SetUpdatingDisplay(true);Check(video.WelcomeText=="업데이트 중"&&!startupDefaults.Visible,"welcome title changes during update");
            timer.Stop();TopMost=true;Activate();UpdateChromeVisibility(new Point(-1,-1));await Task.Delay(150);
            using(var capture=new Bitmap(Width,Height)){using var g=Graphics.FromImage(capture);g.CopyFromScreen(Location,Point.Empty,Size);capture.Save(Path.Combine(resultDir,"updating-0.5.5.png"));}TopMost=false;timer.Start();
            SetUpdatingDisplay(false);Check(video.WelcomeText=="MooVideo"&&startupDefaults.Visible,"welcome display restored after update cancellation or failure");
            timer.Stop();TopMost=true;BringToFront();Activate();UpdateChromeVisibility(new Point(-1,-1));await Task.Delay(200);
            using(var capture=new Bitmap(Width,Height)){using var g=Graphics.FromImage(capture);g.CopyFromScreen(Location,Point.Empty,Size);capture.Save(Path.Combine(resultDir,"startup-0.5.2.png"));}TopMost=false;timer.Start();
            AddFiles(new[]{Path.Combine(resultDir,"test 영상 with spaces.mp4")},true);await Task.Delay(1500);Check(duration>5&&player!.Number("video-params/w")==640,"embedded playback");
            Check(!startupDefaults.Visible,"startup default app button hides on playback");
            Check(ClientSize==new Size(640,360),"first video matches source dimensions");
            SetVideoWindowScale(0.5);Check(ClientSize==new Size(320,180),"half-size window");
            Check(leftButtons.Where(b=>b.Visible).All(b=>b.Left>=0&&b.Right<=bottom.Width)&&rightButtons.Where(b=>b.Visible).All(b=>b.Left>=0&&b.Right<=bottom.Width),"small window buttons fit");
            Check(edges.Count==8&&edges.All(e=>e.Visible),"all edges and corners have resize targets");
            timer.Stop();UpdateChromeVisibility(new Point(100,100));ShowVolumeFlyout();Check(volumePopup.Top>=0&&volumePopup.Bottom<=ClientSize.Height&&time.Right<=bottom.Width,"small window volume and time fit");await Task.Delay(150);
            using(var small=new Bitmap(Width,Height)){using var g=Graphics.FromImage(small);g.CopyFromScreen(Location,Point.Empty,Size);small.Save(Path.Combine(resultDir,"ui-0.5.1-small.png"));}timer.Start();
            SetVideoWindowScale(1);
            player!.Set("pause","yes");TopMost=true;Activate();UpdateChromeVisibility(new Point(100,100));
            Check(seekHit.Top==bottom.Top+seek.Top-10&&seekHit.Height==seek.Height+20,"seek hit target extends ten pixels above and below");
            foreach(int y in new[]{1,seekHit.Height-2}){
                IntPtr Pack(int x)=>(IntPtr)((y<<16)|(x&0xffff));
                Native.SendMessage(seekHit.Handle,0x201,(IntPtr)1,Pack(seekHit.Width/4));
                Native.SendMessage(seekHit.Handle,0x200,(IntPtr)1,Pack(seekHit.Width/2));
                Check(seek.Dragging,"expanded target maintains drag capture");
                Native.SendMessage(seekHit.Handle,0x202,IntPtr.Zero,Pack(seekHit.Width*3/4));
                await Task.Delay(250);Check(!seek.Dragging&&player.Number("time-pos")>4.3&&player.Number("time-pos")<4.8,$"seek drag works at target y={y}");
            }TopMost=false;
            player!.Set("pause","yes");player.Seek(2);await Task.Delay(200);var before=player.Number("time-pos");Frame(1);await Task.Delay(300);Check(player.Number("time-pos")>before,"forward frame");Frame(-1);await Task.Delay(300);Check(Math.Abs(player.Number("time-pos")-before)<0.05,"back frame");
            var vol=player.Number("volume");Wheel(120,new Point(100,100));Check(player.Number("volume")==Math.Min(100,vol+2),"wheel volume");
            Check(volumePopup.Visible&&volumeState.Text==$"{player.Number("volume"):0}%","wheel shows current volume slider");
            Wheel(120,new Point(ClientSize.Width-100,100));await Task.Delay(600);Check(player.Flag("pause")&&player.Number("speed")==1,"right wheel preserves paused state");
            player.Set("pause","no");Wheel(120,new Point(ClientSize.Width-100,100));await Task.Delay(600);Check(!player.Flag("pause")&&player.Number("speed")==1,"right wheel resumes playing state");
            ToggleSide("playlist");await Task.Delay(600);Check(side.Visible&&tiles?.Controls.Count>0,"playlist renders");ToggleSide("playlist");Check(!side.Visible,"playlist hides");
            prefs.Folder=resultDir;ToggleSide("files");await Task.Delay(1000);Check(nativeFiles?.Ready==true&&nativeFiles.Width>0,"Windows file explorer renders");thumbSlider!.Value=250;Check(prefs.ThumbnailSize==250&&nativeFiles?.CurrentThumbnailSize==250,"Windows thumbnail size changes");
            ToggleSide("edit");var keyWait=Stopwatch.StartNew();while(keyframes.Count==0&&keyWait.Elapsed.TotalSeconds<8)await Task.Delay(100);Check(operation!=null&&keyframes.Count>0,"editor keyframes");
            Size=MinimumSize;LayoutPlayer();Check(!rightButtons.Any(r=>leftButtons.Where(l=>l.Visible).Any(l=>r.Bounds.IntersectsWith(l.Bounds))),"minimum width controls do not overlap");Check(exportButton!=null&&exportButton.Parent is Panel footer&&footer.Bottom<=side.Height&&footer.Top>=0,"editor save control visible at minimum size");
            var sideClose=side.Controls.OfType<ToolButton>().First(c=>Equals(c.Tag,"sideClose"));Check(sideClose.Left>=0&&sideClose.Right<=side.Width-8,"sidebar close button fits at minimum size");
            UpdateChromeVisibility(new Point(40,100));Check(header.Visible&&header.Top==0&&video.Top==0&&video.Height==ClientSize.Height&&bottom.Bottom==ClientSize.Height,"controls overlay video");
            ShowVolumeFlyout();Check(volumePopup.Visible&&volumePopup.Bottom>bottom.Top,"vertical volume slider opens above button");volumePopup.Visible=false;
            UpdateChromeVisibility(new Point(-1,-1));Check(!header.Visible,"title row hides outside viewer");Size=new Size(1360,840);LayoutPlayer();ToggleSide("edit");
            Check(seek.MarkIn<0&&seek.MarkOut<0,"closing editor hides markers immediately");
            ToggleSide("edit");markIn=1;markOut=3;SyncEditMarkers();ToggleSide("files");Check(seek.MarkIn<0&&seek.MarkOut<0,"switching panels hides edit markers");ToggleSide("files");
            SetVideoWindowScale(1.5);timer.Stop();UpdateChromeVisibility(new Point(100,100));ShowVolumeFlyout();await Task.Delay(250);
            using(var capture=new Bitmap(Width,Height)){using var g=Graphics.FromImage(capture);g.CopyFromScreen(Location,Point.Empty,Size);capture.Save(Path.Combine(resultDir,"ui-0.5.1.png"));}timer.Start();
            Fullscreen();UpdateChromeVisibility(new Point(40,40));Check(full&&video.Height==ClientSize.Height&&bottom.Visible&&!header.Visible,"fullscreen controls show on movement");
            lastPointerMove=DateTime.UtcNow.AddSeconds(-3);UpdateChromeVisibility(new Point(40,40));Check(!bottom.Visible&&!header.Visible,"fullscreen controls hide after stillness");
            UpdateChromeVisibility(new Point(90,90));Check(bottom.Visible,"fullscreen controls return on movement");
            ToggleSide("playlist");UpdateChromeVisibility(new Point(ClientSize.Width-20,40));Check(side.Visible&&bottom.Visible,"fullscreen sidebar stays open while in use");Fullscreen();ToggleSide("playlist");Check(!full,"fullscreen exits");
            player!.Set("pause","yes");player.Seek(2);await Task.Delay(250);ToggleInk();await Task.Delay(150);
            Check(inkMode&&inkCanvas.Visible&&inkTools.Visible&&player.Flag("pause"),"drawing opens only on paused frame");
            inkCanvas.PenColor=Color.Red;inkCanvas.PenSize=2;inkCanvas.Stroke(new PointF(.25f,.3f),new PointF(.75f,.3f));
            var inkPosition=player.Number("time-pos");
            foreach(var target in new Control[]{seekHit,seek})foreach(int y in new[]{1,target.Height/2,target.Height-2}){
                IntPtr Pack(int x)=>(IntPtr)((y<<16)|(x&0xffff));
                Native.SendMessage(target.Handle,0x201,(IntPtr)1,Pack(target.Width/4));Native.SendMessage(target.Handle,0x200,(IntPtr)1,Pack(target.Width*3/4));Native.SendMessage(target.Handle,0x202,IntPtr.Zero,Pack(target.Width*3/4));
            }
            await Task.Delay(200);Check(!seek.Dragging&&Math.Abs(player.Number("time-pos")-inkPosition)<.02&&inkMode&&inkCanvas.HasInk,"drawing mode blocks direct and expanded seek dragging without clearing ink");
            var annotated=Path.Combine(resultDir,"annotated.png");SaveFrame(annotated);
            using(var saved=new Bitmap(annotated)){var color=saved.GetPixel(saved.Width/2,(int)(saved.Height*.3));Check(saved.Width==640&&saved.Height==360&&color.R>230&&color.G<30&&color.B<30,"screenshot contains pen at original frame coordinates");}
            var previousClipboard=Clipboard.GetDataObject();
            try{
                ActiveControl=play;var copyKey=new KeyEventArgs(Keys.Control|Keys.C);OnKey(this,copyKey);
                using var copied=Clipboard.GetImage();Check(copyKey.Handled&&copied!=null&&copied.Width==640&&copied.Height==360,"Ctrl+C copies screenshot at original resolution");
                using var bitmap=new Bitmap(copied!);var color=bitmap.GetPixel(bitmap.Width/2,(int)(bitmap.Height*.3));Check(color.R>230&&color.G<30&&color.B<30&&inkMode&&player.Flag("pause"),"clipboard screenshot includes drawing and keeps paused editing state");
                using var input=new TextBox{Text="sample text"};Controls.Add(input);ActiveControl=input;var textCopy=new KeyEventArgs(Keys.Control|Keys.C);OnKey(this,textCopy);Check(!textCopy.Handled,"Ctrl+C in text field stays available for text copying");Controls.Remove(input);ActiveControl=play;
            }finally{if(previousClipboard!=null)Clipboard.SetDataObject(previousClipboard,true,5,100);else Clipboard.Clear();}
            inkCanvas.Erasing=true;inkCanvas.EraserSize=2;inkCanvas.Stroke(new PointF(.4f,.3f),new PointF(.6f,.3f));SaveFrame(Path.Combine(resultDir,"erased.png"));
            using(var saved=new Bitmap(Path.Combine(resultDir,"erased.png")))Check(saved.GetPixel(saved.Width/2,(int)(saved.Height*.3)).ToArgb()!=Color.Red.ToArgb(),"eraser removes drawn ink without erasing video");
            inkCanvas.Erasing=false;inkCanvas.Stroke(new PointF(.2f,.55f),new PointF(.7f,.7f));
            TopMost=true;Activate();timer.Stop();UpdateChromeVisibility(new Point(100,100));await Task.Delay(200);
            using(var capture=new Bitmap(Width,Height)){using var g=Graphics.FromImage(capture);g.CopyFromScreen(Location,Point.Empty,Size);capture.Save(Path.Combine(resultDir,"drawing-0.5.3.png"));}TopMost=false;timer.Start();
            Native.SendMessage(play.Handle,0x201,(IntPtr)1,(IntPtr)(10|(10<<16)));Native.SendMessage(play.Handle,0x202,IntPtr.Zero,(IntPtr)(10|(10<<16)));await Task.Delay(150);Check(!player.Flag("pause")&&!inkMode&&!inkCanvas.HasInk&&!inkTools.Visible&&seek.DragAllowed,"play button still resumes and unlocks seek after drawing");
            player.Set("pause","yes");ToggleInk();OnKey(this,new KeyEventArgs(Keys.Space));await Task.Delay(150);Check(!player.Flag("pause")&&!inkMode&&seek.DragAllowed,"space still resumes and unlocks seek after drawing");
            player.Set("pause","yes");player.Seek(0);await Task.Delay(150);OnKey(this,new KeyEventArgs(Keys.Right));await Task.Delay(150);Check(player.Number("time-pos")>4.8,"right arrow seeks five seconds");OnKey(this,new KeyEventArgs(Keys.Left));await Task.Delay(150);Check(player.Number("time-pos")<.2,"left arrow seeks back five seconds");
            player.Set("volume",50);OnKey(this,new KeyEventArgs(Keys.Up));Check(player.Number("volume")==55,"up arrow raises volume");OnKey(this,new KeyEventArgs(Keys.Down));Check(player.Number("volume")==50,"down arrow lowers volume");
            var numbered=Path.Combine(resultDir,"series");Directory.CreateDirectory(numbered);var src=Path.Combine(resultDir,"test 영상 with spaces.mp4");
            foreach(var name in new[]{"test_10.mp4","test_1.mp4","test_2.mp4","other_3.mp4","test_2_edit.mp4","test_3.mp3"})File.Copy(src,Path.Combine(numbered,name),true);
            var group=RelatedFiles.Find(Path.Combine(numbered,"test_2.mp4"));Check(group.Select(Path.GetFileName).SequenceEqual(new[]{"test_1.mp4","test_2.mp4","test_10.mp4"}),"related playlist uses exact prefix and numeric ordering, excludes other files");
            AddFiles(new[]{Path.Combine(numbered,"test_2.mp4")},true);await Task.Delay(1600);Check(playlist.Contains(group[0])&&playlist.Contains(group[2]),"playing numbered file adds siblings automatically");
            OnKey(this,new KeyEventArgs(Keys.PageUp));await Task.Delay(600);Check(current==group[0],"Page Up opens previous playlist file");OnKey(this,new KeyEventArgs(Keys.PageDown));await Task.Delay(600);Check(current==group[1],"Page Down opens next playlist file");
            await File.WriteAllLinesAsync(Path.Combine(resultDir,"ui-results.txt"),checks.Append("ALL PASSED"));
        }catch(Exception ex){checks.Add("FAIL "+ex);await File.WriteAllLinesAsync(Path.Combine(resultDir,"ui-results.txt"),checks);}
        Close();
    }
}
