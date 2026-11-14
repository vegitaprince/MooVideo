using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MooVideo;

internal static class WindowsIntegration
{
    const string AppName="MooVideo";
    const string ProgId="MooVideo.Video";
    const string CapabilityPath=@"Software\MooVideo\Capabilities";
    internal static readonly string[] VideoExtensions=
    [
        ".mp4",".mov",".mkv",".avi",".webm",".m4v",".mpg",".mpeg",
        ".mts",".m2ts",".ts",".wmv",".flv",".vob",".mxf",".ogv"
    ];

    // Registers MooVideo as an available handler. Windows keeps the user's existing
    // defaults until they choose MooVideo in Settings; UserChoice is never modified.
    internal static void OpenDefaultApps()
    {
        var exe=Environment.ProcessPath??Path.Combine(AppContext.BaseDirectory,"MooVideo.exe");
        using(var type=Registry.CurrentUser.CreateSubKey(@"Software\Classes\"+ProgId))
            type?.SetValue(null,"MooVideo video");
        using(var icon=Registry.CurrentUser.CreateSubKey(@"Software\Classes\"+ProgId+@"\DefaultIcon"))
            icon?.SetValue(null,$"\"{exe}\",0");
        using(var command=Registry.CurrentUser.CreateSubKey(@"Software\Classes\"+ProgId+@"\shell\open\command"))
            command?.SetValue(null,$"\"{exe}\" \"%1\"");
        using(var capabilities=Registry.CurrentUser.CreateSubKey(CapabilityPath))
        {
            capabilities?.SetValue("ApplicationName",AppName);
            capabilities?.SetValue("ApplicationDescription","영상과 오디오를 재생하고 무손실로 편집하는 MooVideo 플레이어");
            using var associations=capabilities?.CreateSubKey("FileAssociations");
            foreach(var extension in VideoExtensions)associations?.SetValue(extension,ProgId);
        }
        using(var registered=Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            registered?.SetValue(AppName,CapabilityPath);
        using(var app=Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\MooVideo.exe\shell\open\command"))
            app?.SetValue(null,$"\"{exe}\" \"%1\"");
        using(var types=Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\MooVideo.exe\SupportedTypes"))
            foreach(var extension in VideoExtensions)types?.SetValue(extension,"");
        SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero); // SHCNE_ASSOCCHANGED
        var uri="ms-settings:defaultapps?registeredAppUser="+Uri.EscapeDataString(AppName);
        Process.Start(new ProcessStartInfo(uri){UseShellExecute=true});
    }

    [DllImport("shell32.dll")] static extern void SHChangeNotify(uint eventId,uint flags,IntPtr item1,IntPtr item2);
}
