using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace MooVideo;

internal sealed record AvailableUpdate(Version Version,string ZipUrl,string ChecksumUrl);
internal sealed record PreparedUpdate(Version Version,string StageDirectory);

internal static class Updates
{
    const string Repository="vegitaprince/MooVideo";
    static readonly Uri Latest=new($"https://api.github.com/repos/{Repository}/releases/latest");
    static readonly HttpClient Http=new(){Timeout=TimeSpan.FromSeconds(15)};
    static Updates()=>Http.DefaultRequestHeaders.UserAgent.ParseAdd("MooVideo-Updater/0.5");

    internal static async Task<AvailableUpdate?> CheckAsync(CancellationToken token=default)
    {
        using var response=await Http.GetAsync(Latest,token);
        response.EnsureSuccessStatusCode();
        using var document=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
        var release=document.RootElement;
        if(release.GetProperty("draft").GetBoolean()||release.GetProperty("prerelease").GetBoolean())return null;
        var tag=release.GetProperty("tag_name").GetString()?.TrimStart('v','V');
        if(!Version.TryParse(tag,out var version)||version<=typeof(Program).Assembly.GetName().Version)return null;
        var prefix=$"MooVideo-{version.ToString(3)}-win-x64";
        string? zip=null,checksum=null;
        foreach(var asset in release.GetProperty("assets").EnumerateArray())
        {
            var name=asset.GetProperty("name").GetString();
            var url=asset.GetProperty("browser_download_url").GetString();
            if(name==prefix+".zip")zip=url;
            if(name==prefix+".sha256")checksum=url;
        }
        if(!Uri.TryCreate(zip,UriKind.Absolute,out var zipUri)||zipUri.Host!="github.com"||
           !Uri.TryCreate(checksum,UriKind.Absolute,out var checksumUri)||checksumUri.Host!="github.com")return null;
        return new(version,zip!,checksum!);
    }

    internal static Task<PreparedUpdate> PrepareAsync(AvailableUpdate update,IProgress<double> progress,CancellationToken token)
        => Task.Run(() => PreparePackageAsync(update,progress,token),token);

    static async Task<PreparedUpdate> PreparePackageAsync(AvailableUpdate update,IProgress<double> progress,CancellationToken token)
    {
        var stage=Path.Combine(Preferences.DataDirectory,"updates",update.Version.ToString(3)+"-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var checksum=(await Http.GetStringAsync(update.ChecksumUrl,token)).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if(checksum is not {Length:64}||!checksum.All(Uri.IsHexDigit))throw new InvalidDataException("업데이트 검증 파일이 올바르지 않습니다.");
            var zipPath=Path.Combine(stage,"update.zip");
            await DownloadPackageAsync(update,zipPath,progress,token);
            await using(var file=File.OpenRead(zipPath))
            {
                var actual=Convert.ToHexString(await SHA256.HashDataAsync(file,token));
                if(!actual.Equals(checksum,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("업데이트 파일의 검증 값이 일치하지 않습니다.");
            }
            await ExtractPackageAsync(update.Version,zipPath,Path.Combine(stage,"new-app"),token);
            File.Delete(zipPath);
            token.ThrowIfCancellationRequested();
            File.Copy(Environment.ProcessPath!,Path.Combine(stage,"MooVideo-update-helper.exe"));
            token.ThrowIfCancellationRequested();
            return new(update.Version,stage);
        }
        catch
        {
            DiscardStage(stage);
            throw;
        }
    }

    static async Task DownloadPackageAsync(AvailableUpdate update,string zipPath,IProgress<double> progress,CancellationToken token)
    {
        using(var response=await Http.GetAsync(update.ZipUrl,HttpCompletionOption.ResponseHeadersRead,token))
        {
            response.EnsureSuccessStatusCode();
            var length=response.Content.Headers.ContentLength??0;
            await using var source=await response.Content.ReadAsStreamAsync(token);
            await using var target=File.Create(zipPath);
            var buffer=new byte[1024*1024];long received=0;int read;
            while((read=await source.ReadAsync(buffer,token))>0)
            {
                await target.WriteAsync(buffer.AsMemory(0,read),token);
                received+=read;if(length>0)progress.Report((double)received/length);
            }
        }
    }

    static async Task ExtractPackageAsync(Version version,string zipPath,string app,CancellationToken token)
    {
        Directory.CreateDirectory(app);
        using(var archive=ZipFile.OpenRead(zipPath))
        {
            var prefix=$"MooVideo-{version.ToString(3)}/App/";
            long total=0;
            foreach(var entry in archive.Entries)
            {
                if(!entry.FullName.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||entry.FullName.EndsWith('/'))continue;
                var relative=entry.FullName[prefix.Length..].Replace('/',Path.DirectorySeparatorChar);
                var destination=Path.GetFullPath(Path.Combine(app,relative));
                if(!destination.StartsWith(app+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("업데이트 압축 파일의 경로가 올바르지 않습니다.");
                total+=entry.Length;if(total>1_000_000_000)throw new InvalidDataException("업데이트 파일이 너무 큽니다.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var source=entry.Open();
                await using var target=File.Create(destination);
                await source.CopyToAsync(target,token);
            }
        }
        if(!File.Exists(Path.Combine(app,"MooVideo.exe"))||!File.Exists(Path.Combine(app,"libmpv-2.dll")))
            throw new InvalidDataException("업데이트에 필요한 앱 파일이 없습니다.");
    }

    internal static void DiscardStage(string stage)
    {
        try{Directory.Delete(stage,true);}catch(IOException){}catch(UnauthorizedAccessException){}
    }

    internal static void StartInstallation(PreparedUpdate update,string? reopen)
    {
        var helper=Path.Combine(update.StageDirectory,"MooVideo-update-helper.exe");
        var process=new ProcessStartInfo(helper){UseShellExecute=false,CreateNoWindow=true};
        process.ArgumentList.Add("--apply-update");process.ArgumentList.Add(Environment.ProcessId.ToString());
        process.ArgumentList.Add(Path.Combine(update.StageDirectory,"new-app"));process.ArgumentList.Add(AppContext.BaseDirectory);
        if(reopen!=null)process.ArgumentList.Add(reopen);
        using var installer=Process.Start(process)??throw new InvalidOperationException("업데이트 설치 프로그램을 시작하지 못했습니다.");
    }

    internal static int Apply(string[] args)
    {
        try
        {
            if(args.Length is <4 or >5||!int.TryParse(args[1],out var parentId))return 2;
            var source=Path.GetFullPath(args[2]);var target=Path.GetFullPath(args[3]);
            if(!File.Exists(Path.Combine(source,"MooVideo.exe"))||!Directory.Exists(target))return 3;
            try{using var parent=Process.GetProcessById(parentId);if(!parent.WaitForExit(30000))return 4;}catch(ArgumentException){}
            var backup=Path.Combine(Path.GetDirectoryName(source)!,"backup");Directory.CreateDirectory(backup);
            ReplaceFiles(source,target,backup);
            var restart=new ProcessStartInfo(Path.Combine(target,"MooVideo.exe")){UseShellExecute=true};
            if(args.Length==5&&File.Exists(args[4]))restart.ArgumentList.Add(args[4]);
            Process.Start(restart);
            return 0;
        }
        catch(Exception ex)
        {
            try{File.WriteAllText(Path.Combine(Preferences.DataDirectory,"update-error.txt"),ex.ToString());}catch{}
            return 1;
        }
    }

    internal static void ReplaceFiles(string source,string target,string backup)
    {
        var written=new List<string>();
        try
        {
            foreach(var file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories))
            {
                var relative=Path.GetRelativePath(source,file);var destination=Path.Combine(target,relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if(File.Exists(destination)){var old=Path.Combine(backup,relative);Directory.CreateDirectory(Path.GetDirectoryName(old)!);File.Copy(destination,old,true);}
                written.Add(relative);
                File.Copy(file,destination,true);
            }
        }
        catch
        {
            foreach(var relative in written.AsEnumerable().Reverse())
            {
                var old=Path.Combine(backup,relative);var destination=Path.Combine(target,relative);
                if(File.Exists(old))File.Copy(old,destination,true);else File.Delete(destination);
            }
            throw;
        }
    }
}
