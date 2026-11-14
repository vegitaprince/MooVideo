using System.Runtime.InteropServices;
using System.Text;

namespace MooVideo;

// Hosts the Windows Shell's real folder view. Windows owns enumeration, virtualisation,
// icons, and its shared thumbnail cache; MooVideo only supplies the surrounding controls.
[ComVisible(true),ClassInterface(ClassInterfaceType.None)]
public sealed class NativeExplorer : Control, IExplorerBrowserEvents, ICommDlgBrowser3, IExplorerPaneVisibility, IShellServiceProvider
{
    static readonly Guid BrowserClass = new("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
    static readonly Guid FolderViewId = new("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
    static readonly Guid ShellItemArrayId = new("B63EA76D-1F85-456F-A19C-48159EFA858B");
    static readonly Guid BrowserFrameService = new("000214F1-0000-0000-C000-000000000046");
    IExplorerBrowser? browser;
    uint eventCookie;
    string folder;
    int thumbnailSize;
    readonly Action<string[]> playFiles;
    readonly Action<string> navigated;
    internal bool Ready=>browser!=null;
    internal int CurrentThumbnailSize
    {
        get
        {
            if(browser==null)return 0;
            var iid=new Guid("1AF3A467-214F-4298-908E-06B03E0B39F9");
            if(browser.GetCurrentView(ref iid,out var ptr)!=0||ptr==IntPtr.Zero)return 0;
            try{var view=(IFolderView2Size)Marshal.GetObjectForIUnknown(ptr);try{view.GetViewModeAndIconSize(out _,out var size);return size;}finally{Marshal.ReleaseComObject(view);}}
            finally{Marshal.Release(ptr);}
        }
    }

    internal NativeExplorer(string initialFolder, int iconSize, Action<string[]> onPlay, Action<string> onNavigate)
    {
        folder=initialFolder;thumbnailSize=iconSize;playFiles=onPlay;navigated=onNavigate;
        BackColor=Theme.Panel;SetStyle(ControlStyles.UserPaint,false);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if(DesignMode)return;
        try
        {
            var type=Type.GetTypeFromCLSID(BrowserClass,true)!;
            browser=(IExplorerBrowser)Activator.CreateInstance(type)!;
            SetBrowserSite(true);
            browser.Advise(this,out eventCookie);
            var bounds=new ShellRect(0,0,Math.Max(1,Width),Math.Max(1,Height));
            var settings=new FolderSettings(5,1); // Windows thumbnail view, auto arranged.
            browser.Initialize(Handle,ref bounds,ref settings);
            browser.SetOptions(0x2|0x40); // Frame service enables default-command callbacks; panes stay hidden.
            Navigate(folder);
        }
        catch
        {
            ReleaseBrowser();
            throw;
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ReleaseBrowser();base.OnHandleDestroyed(e);
    }

    void ReleaseBrowser()
    {
        if(browser==null)return;
        try{if(eventCookie!=0)browser.Unadvise(eventCookie);}catch{}eventCookie=0;
        try{browser.Destroy();}catch{}
        try{SetBrowserSite(false);}catch{}
        try{Marshal.ReleaseComObject(browser);}catch{}
        browser=null;
    }

    void SetBrowserSite(bool active)
    {
        if(browser==null)return;
        var unknown=Marshal.GetIUnknownForObject(browser);
        var site=active?Marshal.GetComInterfaceForObject(this,typeof(IShellServiceProvider)):IntPtr.Zero;
        try{Marshal.ThrowExceptionForHR(IUnknown_SetSite(unknown,site));}
        finally{Marshal.Release(unknown);if(site!=IntPtr.Zero)Marshal.Release(site);}
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if(browser!=null&&IsHandleCreated)browser.SetRect(IntPtr.Zero,new ShellRect(0,0,Math.Max(1,Width),Math.Max(1,Height)));
    }

    internal void Navigate(string path)
    {
        folder=path;
        if(browser==null||!Directory.Exists(path))return;
        var hr=SHParseDisplayName(path,IntPtr.Zero,out var pidl,0,out _);
        if(hr<0)Marshal.ThrowExceptionForHR(hr);
        try{browser.BrowseToIDList(pidl,0);}finally{Marshal.FreeCoTaskMem(pidl);}
    }

    internal void SetThumbnailSize(int size)
    {
        thumbnailSize=size;
        if(browser==null)return;
        var iid=new Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"); // IFolderView2
        if(browser.GetCurrentView(ref iid,out var ptr)!=0||ptr==IntPtr.Zero)return;
        try{var view=(IFolderView2Size)Marshal.GetObjectForIUnknown(ptr);try{view.SetViewModeAndIconSize(5,(uint)Math.Clamp(size,32,512));}finally{Marshal.ReleaseComObject(view);}}
        finally{Marshal.Release(ptr);}
    }

    string[] SelectedFiles()
    {
        if(browser==null)return [];
        var iid=FolderViewId;
        if(browser.GetCurrentView(ref iid,out var viewPtr)!=0||viewPtr==IntPtr.Zero)return [];
        try
        {
            var view=(IFolderView)Marshal.GetObjectForIUnknown(viewPtr);
            try
            {
                var arrayId=ShellItemArrayId;
                if(view.Items(1,ref arrayId,out var arrayPtr)!=0||arrayPtr==IntPtr.Zero)return [];
                try
                {
                    var array=(IShellItemArray)Marshal.GetObjectForIUnknown(arrayPtr);
                    try
                    {
                        array.GetCount(out var count);
                        var paths=new List<string>();
                        for(uint i=0;i<count;i++)
                        {
                            array.GetItemAt(i,out var item);
                            try
                            {
                                item.GetDisplayName(0x80058000,out var pathPtr); // SIGDN_FILESYSPATH
                                try{var path=Marshal.PtrToStringUni(pathPtr);if(path!=null&&File.Exists(path))paths.Add(path);}
                                finally{Marshal.FreeCoTaskMem(pathPtr);}
                            }
                            catch(COMException){} // Virtual Shell items have no file-system path.
                            finally{Marshal.ReleaseComObject(item);}
                        }
                        return paths.ToArray();
                    }
                    finally{Marshal.ReleaseComObject(array);}
                }
                finally{Marshal.Release(arrayPtr);}
            }
            finally{Marshal.ReleaseComObject(view);}
        }
        finally{Marshal.Release(viewPtr);}
    }

    int ICommDlgBrowser3.OnDefaultCommand(IntPtr shellView)
    {
        var selected=SelectedFiles();
        if(selected.Length==0)return 1; // Let Windows navigate into folders.
        BeginInvoke(()=>playFiles(selected));
        return 0;
    }
    int ICommDlgBrowser3.OnStateChange(IntPtr shellView,uint change)=>0;
    int ICommDlgBrowser3.IncludeObject(IntPtr shellView,IntPtr pidl)=>0;
    int ICommDlgBrowser3.Notify(IntPtr shellView,uint type)=>0;
    int ICommDlgBrowser3.GetDefaultMenuText(IntPtr shellView,IntPtr text,int length)=>1;
    int ICommDlgBrowser3.GetViewFlags(out uint flags){flags=0;return 0;}
    int ICommDlgBrowser3.OnColumnClicked(IntPtr shellView,int column)=>0;
    int ICommDlgBrowser3.GetCurrentFilter(IntPtr text,int length){if(text!=IntPtr.Zero&&length>0)Marshal.WriteInt16(text,0);return 0;}
    int ICommDlgBrowser3.OnPreViewCreated(IntPtr shellView)=>0;

    int IShellServiceProvider.QueryService(ref Guid service,ref Guid iid,out IntPtr result)
    {
        result=IntPtr.Zero;
        if(service==typeof(IExplorerPaneVisibility).GUID&&iid==typeof(IExplorerPaneVisibility).GUID)
        {
            result=Marshal.GetComInterfaceForObject(this,typeof(IExplorerPaneVisibility));return 0;
        }
        if(service!=BrowserFrameService||iid!=typeof(ICommDlgBrowser).GUID&&iid!=typeof(ICommDlgBrowser3).GUID)return unchecked((int)0x80004002);
        result=Marshal.GetComInterfaceForObject(this,typeof(ICommDlgBrowser3));
        return 0;
    }

    int IExplorerPaneVisibility.GetPaneState(ref Guid pane,out uint state){state=0x20002;return 0;}

    int IExplorerBrowserEvents.OnNavigationPending(IntPtr pidl)=>0;
    int IExplorerBrowserEvents.OnViewCreated(IntPtr shellView)=>0;
    int IExplorerBrowserEvents.OnNavigationComplete(IntPtr pidl)
    {
        var path=new StringBuilder(32768);
        if(SHGetPathFromIDListEx(pidl,path,(uint)path.Capacity,0))
        {
            folder=path.ToString();
            navigated(folder);
            SetThumbnailSize(thumbnailSize);
        }
        return 0;
    }
    int IExplorerBrowserEvents.OnNavigationFailed(IntPtr pidl)=>0;

    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern int SHParseDisplayName(string name,IntPtr bindContext,out IntPtr pidl,uint attributes,out uint returnedAttributes);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool SHGetPathFromIDListEx(IntPtr pidl,StringBuilder path,uint length,uint flags);
    [DllImport("shlwapi.dll")] static extern int IUnknown_SetSite(IntPtr unknown,IntPtr site);
}

[StructLayout(LayoutKind.Sequential)] internal struct ShellRect(int left,int top,int right,int bottom)
{ public int Left=left,Top=top,Right=right,Bottom=bottom; }
[StructLayout(LayoutKind.Sequential)] internal struct FolderSettings(uint viewMode,uint flags)
{ public uint ViewMode=viewMode,Flags=flags; }

[ComImport,Guid("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExplorerBrowser
{
    void Initialize(IntPtr parent,ref ShellRect rect,ref FolderSettings settings);
    void Destroy();
    void SetRect(IntPtr deferredWindowPosition,ShellRect rect);
    void SetPropertyBag([MarshalAs(UnmanagedType.LPWStr)] string name);
    void SetEmptyText([MarshalAs(UnmanagedType.LPWStr)] string text);
    void SetFolderSettings(ref FolderSettings settings);
    void Advise([MarshalAs(UnmanagedType.Interface)] IExplorerBrowserEvents events,out uint cookie);
    void Unadvise(uint cookie);
    void SetOptions(uint flags);
    void GetOptions(out uint flags);
    void BrowseToIDList(IntPtr pidl,uint flags);
    void BrowseToObject(IntPtr item,uint flags);
    void FillFromObject(IntPtr item,uint flags);
    void RemoveAll();
    [PreserveSig] int GetCurrentView(ref Guid iid,out IntPtr view);
}

[ComImport,Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectWithSite
{
    void SetSite([MarshalAs(UnmanagedType.IUnknown)] object? site);
    void GetSite(ref Guid iid,out IntPtr site);
}

[ComVisible(true),Guid("361BBDC7-E6EE-4E13-BE58-58E2240C810F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerBrowserEvents
{
    [PreserveSig] int OnNavigationPending(IntPtr pidl);
    [PreserveSig] int OnViewCreated(IntPtr shellView);
    [PreserveSig] int OnNavigationComplete(IntPtr pidl);
    [PreserveSig] int OnNavigationFailed(IntPtr pidl);
}

[ComVisible(true),Guid("000214F1-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ICommDlgBrowser
{
    [PreserveSig] int OnDefaultCommand(IntPtr shellView);
    [PreserveSig] int OnStateChange(IntPtr shellView,uint change);
    [PreserveSig] int IncludeObject(IntPtr shellView,IntPtr pidl);
}

[ComVisible(true),Guid("C8AD25A1-3294-41EE-8165-71174BD01C57"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ICommDlgBrowser3
{
    [PreserveSig] int OnDefaultCommand(IntPtr shellView);
    [PreserveSig] int OnStateChange(IntPtr shellView,uint change);
    [PreserveSig] int IncludeObject(IntPtr shellView,IntPtr pidl);
    [PreserveSig] int Notify(IntPtr shellView,uint type);
    [PreserveSig] int GetDefaultMenuText(IntPtr shellView,IntPtr text,int length);
    [PreserveSig] int GetViewFlags(out uint flags);
    [PreserveSig] int OnColumnClicked(IntPtr shellView,int column);
    [PreserveSig] int GetCurrentFilter(IntPtr text,int length);
    [PreserveSig] int OnPreViewCreated(IntPtr shellView);
}

[ComVisible(true),Guid("6D5140C1-7436-11CE-8034-00AA006009FA"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellServiceProvider
{
    [PreserveSig] int QueryService(ref Guid service,ref Guid iid,out IntPtr result);
}

[ComVisible(true),Guid("E07010EC-BC17-44C0-97B0-46C7C95B9EDC"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerPaneVisibility
{
    [PreserveSig] int GetPaneState(ref Guid pane,out uint state);
}

[ComImport,Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFolderView
{
    void GetCurrentViewMode(out uint mode);
    void SetCurrentViewMode(uint mode);
    void GetFolder(ref Guid iid,out IntPtr folder);
    void Item(int index,out IntPtr pidl);
    void ItemCount(uint flags,out int count);
    [PreserveSig] int Items(uint flags,ref Guid iid,out IntPtr items);
}

[ComImport,Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemArray
{
    void BindToHandler(IntPtr bindContext,ref Guid handler,ref Guid iid,out IntPtr result);
    void GetPropertyStore(int flags,ref Guid iid,out IntPtr store);
    void GetPropertyDescriptionList(IntPtr key,ref Guid iid,out IntPtr description);
    void GetAttributes(uint attributes,uint mask,out uint result);
    void GetCount(out uint count);
    void GetItemAt(uint index,out IShellItem item);
}

[ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(IntPtr bindContext,ref Guid handler,ref Guid iid,out IntPtr result);
    void GetParent(out IShellItem parent);
    void GetDisplayName(uint nameType,out IntPtr name);
}

// IFolderView2 inherits IFolderView. The base slots are included for vtable order.
[ComImport,Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFolderView2Size
{
    void GetCurrentViewMode(out uint mode);void SetCurrentViewMode(uint mode);void GetFolder(ref Guid iid,out IntPtr folder);
    void Item(int index,out IntPtr pidl);void ItemCount(uint flags,out int count);void Items(uint flags,ref Guid iid,out IntPtr items);
    void GetSelectionMarkedItem(out int index);void GetFocusedItem(out int index);void GetItemPosition(IntPtr pidl,out Point point);
    void GetSpacing(ref Point point);void GetDefaultSpacing(out Point point);void GetAutoArrange();
    void SelectItem(int index,uint flags);void SelectAndPositionItems(uint count,IntPtr pidls,IntPtr points,uint flags);
    void SetGroupBy(IntPtr key,bool ascending);void GetGroupBy(out IntPtr key,out bool ascending);void SetViewProperty(IntPtr pidl,IntPtr key,IntPtr value);
    void GetViewProperty(IntPtr pidl,IntPtr key,out IntPtr value);void SetTileViewProperties(IntPtr pidl,string properties);void SetExtendedTileViewProperties(IntPtr pidl,string properties);
    void SetText(int type,string text);void SetCurrentFolderFlags(uint mask,uint flags);void GetCurrentFolderFlags(out uint flags);
    void GetSortColumnCount(out int count);void SetSortColumns(IntPtr columns,int count);void GetSortColumns(IntPtr columns,int count);
    void GetItem(int index,ref Guid iid,out IntPtr item);void GetVisibleItem(int start,bool previous,out int index);void GetSelectedItem(int start,out int index);
    void GetSelection(bool noneImpliesFolder,out IntPtr items);void GetSelectionState(IntPtr pidl,out uint state);
    void InvokeVerbOnSelection(string verb);void SetViewModeAndIconSize(uint mode,uint size);
    void GetViewModeAndIconSize(out uint mode,out int size);
}
