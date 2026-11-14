using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace MooVideo;
public sealed class Mpv : IDisposable
{
    const string Dll = "libmpv-2.dll";
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern IntPtr mpv_create();
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern int mpv_initialize(IntPtr ctx);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern int mpv_set_option_string(IntPtr ctx,[MarshalAs(UnmanagedType.LPUTF8Str)] string name,[MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern int mpv_set_property_string(IntPtr ctx,[MarshalAs(UnmanagedType.LPUTF8Str)] string name,[MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern IntPtr mpv_get_property_string(IntPtr ctx,[MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern int mpv_command(IntPtr ctx,IntPtr args);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern IntPtr mpv_wait_event(IntPtr ctx,double timeout);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern void mpv_free(IntPtr ptr);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern void mpv_terminate_destroy(IntPtr ctx);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] static extern IntPtr mpv_error_string(int code);
    [StructLayout(LayoutKind.Sequential)] struct Event { public int Id; public int Error; public ulong Reply; public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential)] struct EndFile { public int Reason; public int Error; }
    IntPtr handle;
    public event Action? Loaded;
    public event Action? Ended;
    public event Action<string>? Failed;
    public Mpv(IntPtr window, bool headless=false)
    {
        handle=mpv_create(); if(handle==IntPtr.Zero) throw new Exception("재생 엔진을 초기화할 수 없습니다.");
        SetOption("config","no"); SetOption("terminal","no"); SetOption("input-default-bindings","no"); SetOption("input-vo-keyboard","no");
        SetOption("osc","no"); SetOption("osd-level","0"); SetOption("idle","yes"); SetOption("keep-open","yes");
        SetOption("hwdec","auto-safe"); SetOption("hr-seek","yes"); SetOption("audio-display","no");
        if(headless) { SetOption("vo","null"); SetOption("ao","null"); }
        else { SetOption("wid",window.ToInt64().ToString(CultureInfo.InvariantCulture)); SetOption("vo","gpu-next"); SetOption("gpu-api","d3d11"); }
        Check(mpv_initialize(handle));
    }
    void SetOption(string n,string v) => Check(mpv_set_option_string(handle,n,v));
    static void Check(int e) { if(e<0) throw new Exception(Marshal.PtrToStringUTF8(mpv_error_string(e))??$"mpv: {e}"); }
    public void Set(string n,object v) => Check(mpv_set_property_string(handle,n,Convert.ToString(v,CultureInfo.InvariantCulture)??""));
    public string Get(string n)
    {
        if(handle==IntPtr.Zero) return "";
        var ptr=mpv_get_property_string(handle,n); if(ptr==IntPtr.Zero) return "";
        try { return Marshal.PtrToStringUTF8(ptr)??""; } finally { mpv_free(ptr); }
    }
    public double Number(string n,double fallback=0) => double.TryParse(Get(n),CultureInfo.InvariantCulture,out var d)&&double.IsFinite(d)?d:fallback;
    public bool Flag(string n) => Get(n)=="yes";
    public void Command(params string[] args)
    {
        var strings=args.Select(Marshal.StringToCoTaskMemUTF8).ToArray(); var arr=Marshal.AllocHGlobal((args.Length+1)*IntPtr.Size);
        try { for(var i=0;i<strings.Length;i++) Marshal.WriteIntPtr(arr,i*IntPtr.Size,strings[i]); Marshal.WriteIntPtr(arr,strings.Length*IntPtr.Size,IntPtr.Zero); Check(mpv_command(handle,arr)); }
        finally { foreach(var p in strings) Marshal.FreeCoTaskMem(p); Marshal.FreeHGlobal(arr); }
    }
    public void Seek(double seconds,bool relative=false) => Command("seek",seconds.ToString("0.######",CultureInfo.InvariantCulture),relative?"relative+exact":"absolute+exact");
    public void Poll()
    {
        if(handle==IntPtr.Zero) return;
        for(int i=0;i<100;i++) {
            var e=Marshal.PtrToStructure<Event>(mpv_wait_event(handle,0)); if(e.Id==0) break;
            if(e.Id==8) Loaded?.Invoke();
            if(e.Id==7 && e.Data!=IntPtr.Zero) { var end=Marshal.PtrToStructure<EndFile>(e.Data); if(end.Reason==4) Failed?.Invoke(Marshal.PtrToStringUTF8(mpv_error_string(end.Error))??"재생 실패"); else if(end.Reason==0) Ended?.Invoke(); }
        }
    }
    public void Dispose() { if(handle!=IntPtr.Zero) { mpv_terminate_destroy(handle); handle=IntPtr.Zero; } }
}
