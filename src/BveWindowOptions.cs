using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

// Launcher-only, opt-in window controls. Does not edit BVE's own preferences.
internal static class BveWindowOptions {
    internal sealed class Settings {
        internal bool AlwaysOnTop,BackgroundAudio;
    }
    internal static string PreferencesFile {
        get{return Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
            "BVE6互換ランチャー","window-options.xml");}
    }
    internal static Settings Read(int version){return ReadFile(PreferencesFile,version);}
    internal static Settings ReadFile(string path,int version){
        if(version!=5 && version!=6)throw new ArgumentOutOfRangeException("version");
        var result=new Settings();
        if(!File.Exists(path))return result;
        var doc=XDocument.Load(path);
        if(doc.Root==null || doc.Root.Name!="WindowOptions")
            throw new InvalidDataException("ウィンドウ設定が不正です。");
        var node=doc.Root.Elements("BVE").FirstOrDefault(
            e=>(int?)e.Attribute("version")==version);
        if(node==null)return result;
        result.AlwaysOnTop=String.Equals((string)node.Attribute("topmost"),
            "true",StringComparison.OrdinalIgnoreCase);
        result.BackgroundAudio=String.Equals((string)node.Attribute("backgroundAudio"),
            "true",StringComparison.OrdinalIgnoreCase);
        return result;
    }
    internal static void Save(int version,Settings value){
        SaveFile(PreferencesFile,version,value);
    }
    internal static void SaveFile(string path,int version,Settings value){
        if(version!=5 && version!=6)throw new ArgumentOutOfRangeException("version");
        if(value==null)throw new ArgumentNullException("value");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        var doc=File.Exists(path)?XDocument.Load(path):
            new XDocument(new XElement("WindowOptions"));
        if(doc.Root==null || doc.Root.Name!="WindowOptions")
            throw new InvalidDataException("ウィンドウ設定が不正です。");
        var node=doc.Root.Elements("BVE").FirstOrDefault(
            e=>(int?)e.Attribute("version")==version);
        if(node==null){node=new XElement("BVE",new XAttribute("version",version));doc.Root.Add(node);}
        node.SetAttributeValue("topmost",value.AlwaysOnTop);
        node.SetAttributeValue("backgroundAudio",value.BackgroundAudio);
        string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            doc.Save(tmp);
            if(File.Exists(path))File.Replace(tmp,path,path+".bak");
            else File.Move(tmp,path);
        }finally{if(File.Exists(tmp))File.Delete(tmp);}
    }

    [DllImport("user32.dll",SetLastError=true)]
    private static extern bool SetWindowPos(IntPtr handle,IntPtr after,
        int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr handle,int index);
    internal static bool IsTopmost(IntPtr handle){
        return handle!=IntPtr.Zero && (GetWindowLongPtr(handle,-20).ToInt64() & 8)!=0;
    }
    internal static bool SetTopmost(IntPtr handle,bool topmost){
        // SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_NOOWNERZORDER.
        return handle!=IntPtr.Zero && SetWindowPos(handle,
            new IntPtr(topmost?-1:-2),0,0,0,0,0x0001|0x0002|0x0010|0x0200);
    }
    internal static void ArrangeWhenReady(int pid,int version){
        var options=Read(version);
        if(!options.AlwaysOnTop)return;
        Task.Run(()=>{
            try{
                using(var process=Process.GetProcessById(pid)){
                    var pinned=new HashSet<IntPtr>();
                    bool windowed=true;
                    if(options.AlwaysOnTop){
                        windowed=BvePreferencesService.Read(version).Windowed;
                        if(!windowed)Diagnostics.Write("BVE_TOPMOST",
                            "全画面表示では最前面固定を行いません。");
                    }
                    // Keep observing new window handles: the BVE main window
                    // can be recreated when a scenario is loaded.
                    while(!process.HasExited){
                        if(options.AlwaysOnTop && windowed){
                            var windows=BveLaunchDisplay.FindBveWindows(pid);
                            pinned.IntersectWith(windows.Select(w=>w.Handle));
                            foreach(var target in windows){
                                if(pinned.Contains(target.Handle))continue;
                                // Keep the BVE scenario chooser accessible too.
                                if(SetTopmost(target.Handle,true)){
                                    pinned.Add(target.Handle);
                                    Diagnostics.Write("BVE_TOPMOST",
                                        "pid="+pid+" window="+(target.ScenarioPicker?"scenario-picker":"main"));
                                }else Diagnostics.Write("BVE_TOPMOST","SetWindowPos failed: "+
                                    Marshal.GetLastWin32Error());
                            }
                        }
                        Thread.Sleep(1000);
                    }
                }
            }catch(Exception ex){Diagnostics.Write("BVE_WINDOW_OPTIONS",ex.Message);}
        });
    }
}
