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
                    var moved=new HashSet<IntPtr>();
                    // Some versions recreate the main window after loading a
                    // scenario. Reposition new handles only, never repeatedly
                    // drag a window the user has already moved by hand.
                    for(int i=0;i<100;i++){
                        if(bve.HasExited)return;
                        foreach(var target in FindBveWindows(pid).OrderBy(x=>x.ScenarioPicker)){
                            var handle=target.Handle;
                            if(moved.Contains(handle))continue;
                            Rectangle area=screen.WorkingArea;
                            int width=Math.Min(target.ScenarioPicker?
                                target.Bounds.Width:preferences.Width,area.Width);
                            int height=Math.Min(target.ScenarioPicker?
                                target.Bounds.Height:preferences.Height,area.Height);
                            int x=area.Left+(area.Width-width)/2;
                            int y=area.Top+(area.Height-height)/2;
                            if(MoveWindow(handle,area,width,height)){
                                moved.Add(handle);
                                Diagnostics.Write("BVE_MONITOR","pid="+pid+" monitor="+display+
                                    " window="+(target.ScenarioPicker?"scenario-picker":"main")+
                                    " bounds="+x+","+y+","+width+","+height);
                            }else Diagnostics.Write("BVE_MONITOR",
                                "移動できませんでした。Windows error="+Marshal.GetLastWin32Error());
                        }
                        Thread.Sleep(500);
                    }
                    if(moved.Count==0)
                        Diagnostics.Write("BVE_MONITOR","起動したBVEのウィンドウを確認できませんでした。");
                }
            }catch(Exception ex){Diagnostics.Write("BVE_MONITOR",ex.Message);}
        });
    }
}
