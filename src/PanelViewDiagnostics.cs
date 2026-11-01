using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

// Read-only, one-shot diagnostics: never load the third-party plug-in or change its state.
// In particular, do not attempt to 'resume' PanelViewEX by injecting code into BVE.
internal static class PanelViewDiagnostics {
    private delegate bool EnumWindowCallback(IntPtr hwnd, IntPtr lparam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback,IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd,StringBuilder buffer,int capacity);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd,StringBuilder buffer,int capacity);

    internal static bool TryParseLaunch(string message,out int pid,out string executable){
        pid=0;executable=null;
        if(String.IsNullOrWhiteSpace(message) || !message.StartsWith("pid=",StringComparison.Ordinal))return false;
        int separator=message.IndexOf(" exe=",StringComparison.Ordinal);
        return separator>4 && Int32.TryParse(message.Substring(4,separator-4),out pid)
            && pid>0 && !String.IsNullOrWhiteSpace(executable=message.Substring(separator+5).Trim());
    }

    internal static string FindExtension(string executable) {
        string bveEx=BveExLocation.Resolve(executable);
        if(String.IsNullOrEmpty(bveEx))return null;
        // BveEX 2.1 still uses the historical 2.0 directory for normal extensions.
        string plugin=Path.Combine(bveEx,"2.0","Extensions","PanelViewEX.dll");
        return File.Exists(plugin)?plugin:null;
    }

    internal static void Schedule(string launchMessage,string run) {
        try {
            int pid;string executable;
            if(!TryParseLaunch(launchMessage,out pid,out executable) ||
               String.IsNullOrWhiteSpace(run) || !Directory.Exists(run))return;
            string plugin=FindExtension(executable);
            if(plugin==null)return;
            // The route may need minutes to load. Observe only one small log
            // file, then give the native UI time to create its window.
            DateTime startedUtc=DateTime.UtcNow;
            string extensionLog=Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PanelViewEX","Logs","PanelViewEX-"+pid+".log");
            Task.Run(async ()=>{
                bool logged=false;
                for(int attempt=0;attempt<36;attempt++){
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    try {
                        if(File.Exists(extensionLog)&&
                           File.GetLastWriteTimeUtc(extensionLog)>=startedUtc.AddSeconds(-5)){
                            logged=true;break;
                        }
                        using(var process=Process.GetProcessById(pid))
                            if(process.HasExited || !String.Equals(process.ProcessName,"BveTs",StringComparison.OrdinalIgnoreCase))
                                break;
                    }catch(ArgumentException){break;}
                    catch(System.ComponentModel.Win32Exception){break;}
                }
                if(logged)await Task.Delay(TimeSpan.FromSeconds(45));
                try { Capture(pid,plugin,run); }
                catch(Exception error) { Diagnostics.Write("PANELVIEW_DIAG_ERROR",error.Message); }
            });
        }catch(Exception ex){Diagnostics.Write("PANELVIEW_DIAG_ERROR",ex.Message);}
    }

    internal static string Capture(int pid,string plugin,string run) {
        var sb=new StringBuilder();
        sb.AppendLine("PanelViewEX 読み込み・ウィンドウ診断（読み取り専用）");
        sb.AppendLine("記録日時: "+DateTimeOffset.Now.ToString("o"));
        sb.AppendLine("BVE PID: "+pid);
        sb.AppendLine("PanelViewEX: "+plugin);
        string root=Path.GetDirectoryName(Path.GetDirectoryName(plugin));
        string settings=Path.Combine(root,"LoadedExtensions.xml");
        try {
            var extension=XDocument.Load(settings).Descendants("Extension").FirstOrDefault(e=>
                String.Equals((string)e.Attribute("Path"),"PanelViewEX.dll",StringComparison.OrdinalIgnoreCase));
            sb.AppendLine("BveEX設定上の有効状態: "+(extension==null?"不明":(string)extension.Attribute("IsEnabled")));
        }catch(Exception ex){sb.AppendLine("BveEX設定読込: "+ex.Message);}
        Process p=null;
        try {p=Process.GetProcessById(pid);if(p.HasExited || !String.Equals(p.ProcessName,"BveTs",StringComparison.OrdinalIgnoreCase)){sb.AppendLine("BVE終了済み、またはPID再利用: 表示の判定不可");return Save(run,sb.ToString());}}
        catch(ArgumentException){sb.AppendLine("BVE終了済み: 表示の判定不可");return Save(run,sb.ToString());}
        try {
            try {
                string[] modules=p.Modules.Cast<ProcessModule>()
                    .Where(m=>m.ModuleName.IndexOf("PanelView",StringComparison.OrdinalIgnoreCase)>=0)
                    .Select(m=>m.ModuleName).ToArray();
                sb.AppendLine("PanelView関連のネイティブDLL: "+(modules.Length==0?"検出なし":String.Join(", ",modules)));
            }catch(Exception ex){sb.AppendLine("モジュール照会: "+ex.Message);}
            var windows=new System.Collections.Generic.List<string>();
            var callback=new EnumWindowCallback((hwnd,context)=>{
                uint owner;GetWindowThreadProcessId(hwnd,out owner);
                if(owner!=(uint)pid || !IsWindowVisible(hwnd))return true;
                var title=new StringBuilder(512);GetWindowText(hwnd,title,title.Capacity);
                var cls=new StringBuilder(256);GetClassName(hwnd,cls,cls.Capacity);
                windows.Add("クラス="+cls+" タイトル="+title);
                return true;
            });
            EnumWindows(callback,IntPtr.Zero);
            GC.KeepAlive(callback);
            sb.AppendLine("BVEプロセスの可視トップレベルウィンドウ数: "+windows.Count);
            foreach(string entry in windows)sb.AppendLine("  "+entry);
            sb.AppendLine("※ウィンドウ一覧だけでは表示成功・失敗を断定できません。");
        }finally {if(p!=null)p.Dispose();}
        return Save(run,sb.ToString());
    }

    private static string Save(string run,string content){
        string path=Path.Combine(run,"PanelViewEX診断.txt");
        File.WriteAllText(path,content,new UTF8Encoding(false));
        Diagnostics.Write("PANELVIEW_DIAG","読み取り専用のウィンドウ観測を記録しました。");
        return path;
    }
}
