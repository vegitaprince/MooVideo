using System.Text.Json;
namespace MooVideo;
static class Program
{
    [STAThread] static void Main(string[] args)
    {
        if(args.Length>0&&args[0]=="--apply-update"){Environment.ExitCode=Updates.Apply(args);return;}
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test")) { SelfTest.Run(args).GetAwaiter().GetResult(); return; }
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "MooVideo", MessageBoxButtons.OK, MessageBoxIcon.Error);
        var smoke=Array.IndexOf(args,"--ui-smoke");if(smoke>=0&&smoke+1<args.Length)Environment.SetEnvironmentVariable("MOOVIDEO_DATA_DIR",Path.Combine(args[smoke+1],"profile"));
        Application.Run(new PlayerForm(args));
    }
}
public sealed class Preferences
{
    public double Volume { get; set; } = 65;
    public int ThumbnailSize { get; set; } = 136;
    public int DrawingPenSize { get; set; } = 1;
    public int DrawingEraserSize { get; set; } = 1;
    public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;
    public string Folder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    public List<string> Playlist { get; set; } = new();
    public Dictionary<string,double> Positions { get; set; } = new();
    public static string DataDirectory => Environment.GetEnvironmentVariable("MOOVIDEO_DATA_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MooVideo");
    public static string Location => Path.Combine(DataDirectory, "settings.json");
    public static Preferences Load() { try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Location)) ?? new(); } catch { return new(); } }
    public void Save() { try { Directory.CreateDirectory(Path.GetDirectoryName(Location)!); var tmp = Location+".tmp"; File.WriteAllText(tmp,JsonSerializer.Serialize(this)); File.Move(tmp,Location,true); } catch { } }
}
