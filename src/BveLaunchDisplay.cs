using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

// BVE's own preferences store window size, but not a chosen monitor.
// Only the monitor choice is stored by the launcher. Never touch the Windows
// global primary display, resolution, refresh rate, or graphics driver.
internal static class BveLaunchDisplay {
    internal static string PreferencesFile {
        get{return Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
            "BVE6互換ランチャー","launch-displays.xml");}
    }
    internal static string Read(int version){
        return ReadFile(PreferencesFile,version);
    }
    internal static string ReadFile(string path,int version){
        if(!File.Exists(path))return "";
        var doc=XDocument.Load(path);
        var node=doc.Root==null?null:doc.Root.Elements("BVE")
            .FirstOrDefault(x=>(int?)x.Attribute("version")==version);
        return node==null?"":((string)node.Attribute("monitor")??"").Trim();
    }
    internal static void Save(int version,string device){
        SaveFile(PreferencesFile,version,device);
    }
    internal static void SaveFile(string path,int version,string device){
        if(version!=5 && version!=6)throw new ArgumentOutOfRangeException("version");
        if(device!=null && device.Length>128)throw new InvalidDataException("モニター名が不正です。");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var doc=File.Exists(path)?XDocument.Load(path):new XDocument(new XElement("LaunchDisplays"));
        if(doc.Root==null || doc.Root.Name!="LaunchDisplays")
            throw new InvalidDataException("ランチャーの画面設定が不正です。");
        var node=doc.Root.Elements("BVE").FirstOrDefault(x=>(int?)x.Attribute("version")==version);
        if(node==null){node=new XElement("BVE",new XAttribute("version",version));doc.Root.Add(node);}
        node.SetAttributeValue("monitor",device??"");
        string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            doc.Save(tmp);
            if(File.Exists(path))File.Replace(tmp,path,path+".bak");
            else File.Move(tmp,path);
        }finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    [DllImport("user32.dll",SetLastError=true)]
    private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll",SetLastError=true)]
    private static extern bool SetWindowPos(IntPtr hWnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowEnumerator callback,IntPtr context);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd,out uint processId);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern int GetWindowText(IntPtr hWnd,StringBuilder caption,int length);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd,out NativeRect rect);
    private delegate bool WindowEnumerator(IntPtr hWnd,IntPtr context);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect {internal int Left,Top,Right,Bottom;}
    internal sealed class WindowTarget {
        internal IntPtr Handle;
        internal bool ScenarioPicker;
        internal bool Maximized;
        internal Rectangle Bounds;
    }
    // BVE opens a separate modal scenario-selection dialog. Both that dialog
    // and the main BVE window must follow the requested monitor. Do not move
    // arbitrary BVE-owned plugin, HUD or error windows.
    internal static List<WindowTarget> FindBveWindows(int pid) {
        var found=new List<WindowTarget>();
        WindowEnumerator inspect=(handle,unused)=>{
            uint owner;
            GetWindowThreadProcessId(handle,out owner);
            if(owner!=(uint)pid || !IsWindowVisible(handle))return true;
            var title=new StringBuilder(256);
            GetWindowText(handle,title,title.Capacity);
            bool main=title.ToString().StartsWith("BVE Trainsim",StringComparison.OrdinalIgnoreCase);
            bool chooser=title.ToString()=="シナリオの選択";
            if(!main && !chooser)
                return true;
            NativeRect rect;
            if(!GetWindowRect(handle,out rect))return true;
            int width=rect.Right-rect.Left;
            int height=rect.Bottom-rect.Top;
            if(width<320 || height<200)return true;
            found.Add(new WindowTarget{Handle=handle,ScenarioPicker=chooser,
                Maximized=IsZoomed(handle),
                Bounds=new Rectangle(rect.Left,rect.Top,width,height)});
            return true;
        };
        EnumWindows(inspect,IntPtr.Zero);
        return found;
    }
    // Testable helper: move only this BVE process window, never a global monitor.
    internal static bool MoveWindow(IntPtr handle,Rectangle workingArea,int width,int height){
        if(handle==IntPtr.Zero || workingArea.Width<=0 || workingArea.Height<=0)return false;
        width=Math.Max(1,Math.Min(width,workingArea.Width));
        height=Math.Max(1,Math.Min(height,workingArea.Height));
        int x=workingArea.Left+(workingArea.Width-width)/2;
        int y=workingArea.Top+(workingArea.Height-height)/2;
        return SetWindowPos(handle,IntPtr.Zero,x,y,width,height,0x0004|0x0010);
    }
    // The center is the reliable monitor identity for both normal and
    // maximized windows, including monitors with negative desktop positions.
    internal static bool IsOnRequestedMonitor(Rectangle bounds,Rectangle monitor){
        return bounds.Width>0 && bounds.Height>0 && monitor.Width>0 &&
            monitor.Height>0 && monitor.Contains(new Point(
                bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2));
    }
    // A maximized WinForms window can be restored by BVE after the launcher's
    // first SetWindowPos. Keep the maximized state, its non-client frame, and
    // the NOACTIVATE flag when targeting a differently sized monitor.
    internal static bool MoveMaximizedWindow(IntPtr handle,Rectangle bounds,
        Rectangle sourceWorkArea,Rectangle targetWorkArea){
        if(handle==IntPtr.Zero || !IsZoomed(handle) || bounds.Width<=0 ||
           bounds.Height<=0 || sourceWorkArea.Width<=0 ||
           sourceWorkArea.Height<=0 || targetWorkArea.Width<=0 ||
           targetWorkArea.Height<=0)return false;
        // The native maximized frame normally extends beyond the work area.
        // Reject inconsistent geometry rather than applying a huge resize.
        int extraW=bounds.Width-sourceWorkArea.Width;
        int extraH=bounds.Height-sourceWorkArea.Height;
        if(extraW<0 || extraW>64 || extraH<0 || extraH>64)return false;
        int x=targetWorkArea.Left-extraW/2;
        int y=targetWorkArea.Top-extraH/2;
        const uint noZOrderNoActivateNoOwnerZOrder=0x0004|0x0010|0x0200;
        return SetWindowPos(handle,IntPtr.Zero,x,y,
            targetWorkArea.Width+extraW,targetWorkArea.Height+extraH,
            noZOrderNoActivateNoOwnerZOrder);
    }
    // The route may spend minutes in a separate loading form; monitoring only
    // the first 50 seconds leaves a successfully moved HWND free to jump back.
    private static bool IsLoadingScenario(int pid){
        bool loading=false;
        EnumWindows((handle,unused)=>{
            uint owner;
            GetWindowThreadProcessId(handle,out owner);
            if(owner!=(uint)pid || !IsWindowVisible(handle))return true;
            var title=new StringBuilder(256);
            GetWindowText(handle,title,title.Capacity);
            if(title.ToString().StartsWith("シナリオを読み込んでいます",
                StringComparison.Ordinal)){
                loading=true;
                return false;
            }
            return true;
        },IntPtr.Zero);
        return loading;
    }
    // Stop 15 seconds after loading ends when an already placed main window
    // remains visible. Otherwise, wait at most 90 seconds for a first/new
    // main HWND. Failed first placements must not extend this grace forever.
    // The outer 20-minute loop remains the hard bound for visible loading.
    internal static bool ShouldStopWatching(int iteration,int lastLoadingOrPicker,
        bool placedMainVisible,bool newMainVisible){
        if(lastLoadingOrPicker<0)return iteration>=180;
        int elapsed=iteration-lastLoadingOrPicker;
        if(elapsed>=180)return true;
        if(elapsed<30)return false;
        return placedMainVisible && !newMainVisible;
    }
    // Never override a previously positioned HWND outside a visible loading
    // phase: without that signal, a manual move cannot be distinguished from
    // an automatic re-maximize. A new HWND still gets its first placement.
    internal static bool ShouldArrangeDuringWatch(bool loadingOrPickerVisible,
        bool previouslyPlaced){
        return loadingOrPickerVisible || !previouslyPlaced;
    }

    internal static void ArrangeWhenReady(int pid,int version) {
        ArrangeWhenReady(pid,version,Read(version));
        BveWindowOptions.ArrangeWhenReady(pid,version);
    }
    // A caller-specified display is useful for isolated integration tests:
    // it does not save or alter the user's own monitor preference.
    internal static void ArrangeWhenReady(int pid,int version,string display) {
        if(String.IsNullOrWhiteSpace(display))return;
        Screen screen=Screen.AllScreens.FirstOrDefault(s=>
            String.Equals(s.DeviceName,display,StringComparison.OrdinalIgnoreCase));
        if(screen==null){Diagnostics.Write("BVE_MONITOR","選択したモニターが接続されていないため、BVEの既定値を使用します: "+display);return;}
        Task.Run(()=>{
            try {
                using(var bve=Process.GetProcessById(pid)){
                    var preferences=BvePreferencesService.Read(version);
                    if(!preferences.Windowed){
                        Diagnostics.Write("BVE_MONITOR","全画面表示のためモニターの移動は行いません。");
                        return;
                    }
                    // Picker and driving windows have independent lifetimes;
                    // a recycled picker HWND must not count as a placed main.
                    var movedMain=new HashSet<IntPtr>();
                    var movedPicker=new HashSet<IntPtr>();
                    // Keep retry delays independent too: a reused picker HWND
                    // must not delay the first placement of the main window.
                    var lastAttemptMain=new Dictionary<IntPtr,int>();
                    var lastAttemptPicker=new Dictionary<IntPtr,int>();
                    int lastLoadingOrPicker=-1;
                    // During startup, BVE can relocate or maximize the SAME
                    // HWND long after it first appeared. Follow the requested
                    // monitor until loading ends, then stop after a short grace
                    // period so later manual user movement is not overridden.
                    // A long-running picker/loading dialog extends the watch,
                    // bounded to 20 minutes for a stalled route.
                    for(int i=0;i<2400;i++){
                        if(bve.HasExited)return;
                        Screen current=Screen.AllScreens.FirstOrDefault(s=>
                            String.Equals(s.DeviceName,display,StringComparison.OrdinalIgnoreCase));
                        if(current==null){
                            Diagnostics.Write("BVE_MONITOR","選択したモニターが接続解除されました: "+display);
                            return;
                        }
                        var windows=FindBveWindows(pid);
                        bool loadingOrPicker=IsLoadingScenario(pid) ||
                            windows.Any(w=>w.ScenarioPicker);
                        if(loadingOrPicker)
                            lastLoadingOrPicker=i;
                        // A scenario picker is not the main driving window.
                        // Do not treat a placed picker as proof of main readiness.
                        bool placedMainVisible=windows.Any(w=>
                            !w.ScenarioPicker && movedMain.Contains(w.Handle));
                        bool newMainVisible=windows.Any(w=>
                            !w.ScenarioPicker && !movedMain.Contains(w.Handle));
                        if(ShouldStopWatching(i,lastLoadingOrPicker,
                            placedMainVisible,newMainVisible))break;
                        foreach(var target in windows.OrderBy(x=>x.ScenarioPicker)){
                            var handle=target.Handle;
                            var placementSet=target.ScenarioPicker?movedPicker:movedMain;
                            var lastAttempt=target.ScenarioPicker?lastAttemptPicker:lastAttemptMain;
                            // The picker is left alone after the first move;
                            // BVE's main window is rechecked while loading.
                            if(target.ScenarioPicker && placementSet.Contains(handle))continue;
                            if(IsOnRequestedMonitor(target.Bounds,current.Bounds)){
                                placementSet.Add(handle);
                                continue;
                            }
                            if(!ShouldArrangeDuringWatch(loadingOrPicker,
                                placementSet.Contains(handle)))continue;
                            int previous;
                            if(lastAttempt.TryGetValue(handle,out previous) && i-previous<4)
                                continue;
                            lastAttempt[handle]=i;
                            // The game may destroy/recreate a HWND while
                            // loading. Never reposition a recycled or
                            // already changed handle from stale enumeration.
                            var before=FindBveWindows(pid).FirstOrDefault(w=>
                                w.Handle==handle &&
                                w.ScenarioPicker==target.ScenarioPicker);
                            if(before==null || before.Bounds!=target.Bounds ||
                               before.Maximized!=target.Maximized)continue;
                            Rectangle area=current.WorkingArea;
                            int width=Math.Min(target.ScenarioPicker?
                                target.Bounds.Width:preferences.Width,area.Width);
                            int height=Math.Min(target.ScenarioPicker?
                                target.Bounds.Height:preferences.Height,area.Height);
                            int x=area.Left+(area.Width-width)/2;
                            int y=area.Top+(area.Height-height)/2;
                            bool placed;
                            if(target.Maximized && !target.ScenarioPicker){
                                Rectangle source=Screen.FromRectangle(target.Bounds).WorkingArea;
                                placed=MoveMaximizedWindow(handle,target.Bounds,source,area);
                            }else placed=MoveWindow(handle,area,width,height);
                            var after=FindBveWindows(pid).FirstOrDefault(w=>w.Handle==handle);
                            if(placed && after!=null &&
                               IsOnRequestedMonitor(after.Bounds,current.Bounds)){
                                placementSet.Add(handle);
                                Diagnostics.Write("BVE_MONITOR","pid="+pid+" monitor="+display+
                                    " window="+(target.ScenarioPicker?"scenario-picker":"main")+
                                    " bounds="+after.Bounds.X+","+after.Bounds.Y+","+
                                    after.Bounds.Width+","+after.Bounds.Height+
                                    " maximized="+after.Maximized);
                            }else Diagnostics.Write("BVE_MONITOR",
                                "移動を確認できませんでした。pid="+pid+
                                " hwnd="+handle.ToInt64()+" Windows error="+
                                Marshal.GetLastWin32Error());
                        }
                        // Observe new handles until the bounded stop condition,
                        // without chasing a manual move outside loading.
                        Thread.Sleep(500);
                    }
                    if(movedMain.Count==0 && movedPicker.Count==0)
                        Diagnostics.Write("BVE_MONITOR","起動したBVEのウィンドウを確認できませんでした。");
                }
            }catch(Exception ex){Diagnostics.Write("BVE_MONITOR",ex.Message);}
        });
    }
}
