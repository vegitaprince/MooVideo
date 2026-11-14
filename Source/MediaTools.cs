using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
namespace MooVideo;
public record MediaInfo(double Duration,string Codec,int Width,int Height,double Fps,JsonElement Streams);
public static class MediaTools
{
    public static readonly HashSet<string> Extensions=new(StringComparer.OrdinalIgnoreCase) { ".mp4",".mov",".mkv",".avi",".webm",".m4v",".mpg",".mpeg",".mts",".m2ts",".ts",".wmv",".flv",".vob",".mxf",".ogv",".mp3",".wav",".flac",".aac",".m4a",".ogg",".opus",".aiff" };
    public static string Bin(string name) => Path.Combine(AppContext.BaseDirectory,"tools",name+".exe");
    public static string N(double n) => n.ToString("0.######",CultureInfo.InvariantCulture);
    public static async Task<string> Run(string name,IEnumerable<string> args,CancellationToken token=default,Action<double>? progress=null)
    {
        var start=new ProcessStartInfo(Bin(name)) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 };
        foreach(var a in args) start.ArgumentList.Add(a);
        using var p=new Process { StartInfo=start }; p.Start();
        using var reg=token.Register(()=> { try { p.Kill(true); } catch { } });
        var errors=p.StandardError.ReadToEndAsync(); var output=new StringBuilder();
        while(await p.StandardOutput.ReadLineAsync() is {} line) { output.AppendLine(line); if(line.StartsWith("out_time_us=")&&double.TryParse(line[12..],CultureInfo.InvariantCulture,out var us)) progress?.Invoke(us/1000000); }
        await p.WaitForExitAsync(); var err=await errors; token.ThrowIfCancellationRequested();
        if(p.ExitCode!=0) throw new Exception(err.Length>3500?err[^3500..]:err);
        return output.ToString();
    }
    public static async Task<MediaInfo> Probe(string file,CancellationToken ct=default)
    {
        using var doc=JsonDocument.Parse(await Run("ffprobe",new[]{"-v","error","-show_format","-show_streams","-of","json",file},ct));
        var r=doc.RootElement; var streams=r.GetProperty("streams"); var v=streams.EnumerateArray().FirstOrDefault(x=>x.GetProperty("codec_type").GetString()=="video");
        static double Num(JsonElement x,string n) => x.ValueKind==JsonValueKind.Object && x.TryGetProperty(n,out var p)&&double.TryParse(p.ToString(),CultureInfo.InvariantCulture,out var d)?d:0;
        var dur=Num(r.GetProperty("format"),"duration"); double fps=0;
        if(v.ValueKind==JsonValueKind.Object && v.TryGetProperty("avg_frame_rate",out var rate)) { var f=rate.GetString()!.Split('/'); if(f.Length==2&&double.TryParse(f[0],out var a)&&double.TryParse(f[1],out var b)&&b>0) fps=a/b; }
        return new(dur,v.ValueKind==JsonValueKind.Object?v.GetProperty("codec_name").GetString()??"?":"audio",(int)Num(v,"width"),(int)Num(v,"height"),fps,streams.Clone());
    }
    public static async Task<List<double>> Keyframes(string file,CancellationToken ct)
    {
        var raw=await Run("ffprobe",new[]{"-v","error","-select_streams","v:0","-show_packets","-show_entries","packet=pts_time,flags","-of","csv=p=0",file},ct);
        var times=new List<double>();
        foreach(var l in raw.Split('\n')) { var p=l.Trim().Split(','); if(p.Length>=2 && p[1].Contains('K')&&double.TryParse(p[0],CultureInfo.InvariantCulture,out var t)&&t>=0) times.Add(t); }
        return times.Distinct().Order().ToList();
    }
    public static double Snap(IEnumerable<double> keys,double at) => keys.Where(t=>t<=at+0.000001).DefaultIfEmpty(0).Max();
    public static List<string> CopyArgs(string input,string operation,double start,double end,string? audio)
    {
        var a=new List<string>{"-hide_banner","-nostdin","-v","error","-n","-progress","pipe:1"};
        if(operation=="trim") a.AddRange(new[]{"-ss",N(start)});
        a.AddRange(new[]{"-i",input});
        if(operation is "replace" or "append") { if(string.IsNullOrWhiteSpace(audio)) throw new ArgumentException("오디오 파일을 선택하세요."); a.AddRange(new[]{"-i",audio}); }
        a.AddRange(new[]{"-map","0"});
        if(operation=="remove" || operation=="replace") a.AddRange(new[]{"-map","-0:a?"});
        if(operation is "replace" or "append") a.AddRange(new[]{"-map","1:a:0"});
        a.AddRange(new[]{"-map_metadata","0","-map_chapters","0","-c","copy"});
        if(operation=="trim") { if(end<=start) throw new ArgumentException("끝 지점은 시작 지점보다 뒤여야 합니다."); a.AddRange(new[]{"-t",N(end-start),"-avoid_negative_ts","make_zero"}); }
        return a;
    }
    public static async Task Export(string input,string output,string operation,double start,double end,string? audio,CancellationToken ct,Action<double>? progress=null)
    {
        if(Path.GetFullPath(input).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase)||File.Exists(output)) throw new IOException("원본 또는 기존 파일을 덮어쓰지 않습니다. 새로운 파일 이름을 선택하세요.");
        var temp=Path.Combine(Path.GetDirectoryName(output)!,"."+Guid.NewGuid().ToString("N")+Path.GetExtension(output));
        try {
            if(operation=="trim") {
                if(end<=start)throw new ArgumentException("끝 지점은 시작 지점보다 뒤여야 합니다.");
                var staging=Path.Combine(Path.GetDirectoryName(output)!,".moovideo-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
                try {
                    // Segment on presentation-time keyframes. Input -ss streamcopy may retain an earlier GOP for B-frame codecs.
                    var cuts=start>0?N(start)+","+N(end):N(end);var pattern=Path.Combine(staging,"part_%03d"+Path.GetExtension(output));
                    await Run("ffmpeg",new[]{"-hide_banner","-nostdin","-v","error","-n","-i",input,"-map","0","-c","copy","-f","segment","-segment_times",cuts,"-segment_time_delta","0.000001","-reset_timestamps","1","-progress","pipe:1",pattern},ct,progress);
                    var segment=Path.Combine(staging,$"part_{(start>0?1:0):000}"+Path.GetExtension(output));
                    if(!File.Exists(segment))throw new IOException("선택한 구간을 찾을 수 없습니다.");
                    File.Move(segment,temp,false);
                } finally { if(Directory.Exists(staging))Directory.Delete(staging,true); }
            }else {var a=CopyArgs(input,operation,start,end,audio);a.Add(temp);await Run("ffmpeg",a,ct,progress);}
            ct.ThrowIfCancellationRequested();File.Move(temp,output,false);
        }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
    public static async Task<string[]> Split(string input,string baseOutput,double time,CancellationToken ct,Action<double>? progress=null)
    {
        var dir=Path.GetDirectoryName(baseOutput)!; var name=Path.GetFileNameWithoutExtension(baseOutput); var ext=Path.GetExtension(baseOutput);
        var result=new[]{Path.Combine(dir,name+"_01"+ext),Path.Combine(dir,name+"_02"+ext)};
        if(result.Any(File.Exists)) throw new IOException("분할 파일 이름이 이미 존재합니다. 다른 이름을 선택하세요.");
        var temp=Path.Combine(dir,".moovideo-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp); var moved=new List<string>();
        try {
            await Run("ffmpeg",new[]{"-hide_banner","-nostdin","-v","error","-n","-i",input,"-map","0","-c","copy","-f","segment","-segment_times",N(time),"-segment_time_delta","0.000001","-reset_timestamps","1","-progress","pipe:1",Path.Combine(temp,"part_%02d"+ext)},ct,progress);
            var parts=Directory.GetFiles(temp).Order().ToArray(); if(parts.Length!=2) throw new IOException("이 지점에서는 두 개의 독립된 구간을 만들 수 없습니다. 다른 키프레임을 선택하세요.");
            ct.ThrowIfCancellationRequested(); for(var i=0;i<2;i++) { File.Move(parts[i],result[i],false); moved.Add(result[i]); } return result;
        } catch { foreach(var f in moved) File.Delete(f); throw; }
        finally { if(Directory.Exists(temp)) Directory.Delete(temp,true); }
    }
}
