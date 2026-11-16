using System.Numerics;
using System.Text.RegularExpressions;
namespace MooVideo;
static class RelatedFiles
{
    static readonly Regex Numbered=new(@"^(.*?)([0-9]+)$",RegexOptions.CultureInvariant);
    internal static string[] Find(string path)
    {
        var match=Numbered.Match(Path.GetFileNameWithoutExtension(path));
        if(!match.Success||!match.Groups[1].Value.Any(char.IsLetter))return Array.Empty<string>();
        string prefix=match.Groups[1].Value;
        try{return Directory.EnumerateFiles(Path.GetDirectoryName(Path.GetFullPath(path))!).Where(f=>MediaTools.Extensions.Contains(Path.GetExtension(f))&&!new[]{".mp3",".wav",".flac",".m4a",".aac",".ogg",".opus",".aiff"}.Contains(Path.GetExtension(f),StringComparer.OrdinalIgnoreCase))
            .Select(f=>(Path:f,Match:Numbered.Match(Path.GetFileNameWithoutExtension(f))))
            .Where(x=>x.Match.Success&&x.Match.Groups[1].Value.Equals(prefix,StringComparison.OrdinalIgnoreCase))
            .OrderBy(x=>BigInteger.Parse(x.Match.Groups[2].Value)).ThenBy(x=>x.Path,StringComparer.OrdinalIgnoreCase).Select(x=>x.Path).ToArray();}
        catch(IOException){return Array.Empty<string>();}catch(UnauthorizedAccessException){return Array.Empty<string>();}
    }
}
