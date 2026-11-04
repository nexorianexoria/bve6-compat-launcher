using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        var display=Read(version);
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
                    IntPtr moved=IntPtr.Zero;
                    // Some versions recreate the main window after loading a
                    // scenario. Reposition new handles only, never repeatedly
                    // drag a window the user has already moved by hand.
                    for(int i=0;i<100;i++){
                        if(bve.HasExited)return;
                        bve.Refresh();
                        var handle=bve.MainWindowHandle;
                        if(handle!=IntPtr.Zero && handle!=moved && IsWindowVisible(handle)){
                            Rectangle area=screen.WorkingArea;
                            int width=Math.Min(preferences.Width,area.Width);
                            int height=Math.Min(preferences.Height,area.Height);
                            int x=area.Left+(area.Width-width)/2;
                            int y=area.Top+(area.Height-height)/2;
                            if(MoveWindow(handle,area,width,height)){
                                moved=handle;
                                Diagnostics.Write("BVE_MONITOR","pid="+pid+" monitor="+display+
                                    " bounds="+x+","+y+","+width+","+height);
                            }else Diagnostics.Write("BVE_MONITOR",
                                "移動できませんでした。Windows error="+Marshal.GetLastWin32Error());
                        }
                        Thread.Sleep(500);
                    }
                    if(moved==IntPtr.Zero)
                        Diagnostics.Write("BVE_MONITOR","起動したBVEのウィンドウを確認できませんでした。");
                }
            }catch(Exception ex){Diagnostics.Write("BVE_MONITOR",ex.Message);}
        });
    }
}
