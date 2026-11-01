using System;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
internal static class InstallShell {
    const string Key=@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\BVE6CompatibilityLauncher";
    internal static string FindExistingRoot(){
        try{using(var key=Registry.LocalMachine.OpenSubKey(Key)){string p=key==null?null:(string)key.GetValue("InstallLocation");if(p!=null&&InstallEngine.ActiveTarget(p)!=null)return InstallEngine.Root(p);}}catch{}
        foreach(string dir in new[]{Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)}){
            object shell=null,shortcut=null;try{string link=Path.Combine(dir,"BVE6互換ランチャー.lnk");if(!File.Exists(link))continue;shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));dynamic sh=shell;shortcut=sh.CreateShortcut(link);dynamic s=shortcut;string target=(string)s.TargetPath;string p=Path.GetFileName(target).Equals("LauncherStarter.exe",StringComparison.OrdinalIgnoreCase)?Path.GetDirectoryName(target):InstallEngine.DetectRoot(Path.GetDirectoryName(target));if(p!=null&&InstallEngine.ActiveTarget(p)!=null)return InstallEngine.Root(p);}catch{}finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);if(shell!=null)Marshal.FinalReleaseComObject(shell);}
        }
        return null;
    }
    internal static void Shortcut(string directory,string root){
        root=InstallEngine.Root(root);Directory.CreateDirectory(directory);string target=Path.Combine(root,"LauncherStarter.exe"),link=Path.Combine(directory,"BVE6互換ランチャー.lnk");object shell=null,shortcut=null;
        try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));dynamic sh=shell;shortcut=sh.CreateShortcut(link);dynamic s=shortcut;
            if(File.Exists(link)){string previous=(string)s.TargetPath;string versions=Path.Combine(root,"versions")+Path.DirectorySeparatorChar;string standard=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"BVE6互換ランチャー","versions")+Path.DirectorySeparatorChar;bool legacy=(previous.StartsWith(versions,StringComparison.OrdinalIgnoreCase)||previous.StartsWith(standard,StringComparison.OrdinalIgnoreCase))&&Path.GetFileName(previous).Equals("BVE6互換ランチャー.exe",StringComparison.OrdinalIgnoreCase);if(!String.Equals(previous,target,StringComparison.OrdinalIgnoreCase)&&!legacy)throw new IOException("同名の別のショートカットがあるため変更しません: "+link);}
            s.TargetPath=target;s.Arguments="";s.WorkingDirectory=root;s.IconLocation=target;s.Description="BVE 起動ランチャー";s.Save();
            TaskbarIdentity.SetShortcutIdentity(link);
        }finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);if(shell!=null)Marshal.FinalReleaseComObject(shell);}
    }
    // Keep existing user pins intact, only set our stable identity on links
    // already pointing at the permanent launcher starter.
    internal static bool RefreshExistingTaskbarPin(string root){
        string directory=Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft","Internet Explorer","Quick Launch","User Pinned","TaskBar");
        return RefreshPinnedShortcutAt(directory,root);
    }
    internal static bool RefreshPinnedShortcutAt(string directory,string root){
        root=InstallEngine.Root(root);
        string link=Path.Combine(directory,"BVE6互換ランチャー.lnk");
        if(!File.Exists(link))return false;
        object shell=null,shortcut=null;
        try{
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic sh=shell;shortcut=sh.CreateShortcut(link);dynamic s=shortcut;
            string previous=(string)s.TargetPath;
            if(!String.Equals(Path.GetFullPath(previous),
                Path.Combine(root,"LauncherStarter.exe"),StringComparison.OrdinalIgnoreCase))return false;
        }finally{
            if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);
            if(shell!=null)Marshal.FinalReleaseComObject(shell);
        }
        TaskbarIdentity.SetShortcutIdentity(link);
        return true;
    }
    internal static void RemoveShortcut(string directory,string root){string link=Path.Combine(directory,"BVE6互換ランチャー.lnk");if(!File.Exists(link))return;object shell=null,shortcut=null;try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));dynamic sh=shell;shortcut=sh.CreateShortcut(link);dynamic s=shortcut;if(String.Equals((string)s.TargetPath,Path.Combine(root,"LauncherStarter.exe"),StringComparison.OrdinalIgnoreCase))File.Delete(link);}finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);if(shell!=null)Marshal.FinalReleaseComObject(shell);}}
    internal static void Register(string root){using(var key=Registry.LocalMachine.CreateSubKey(Key)){string uninstall=Path.Combine(Path.GetDirectoryName(InstallEngine.ActiveTarget(root)),"Uninstall.exe");key.SetValue("DisplayName","BVE 起動ランチャー");key.SetValue("DisplayVersion",Release.Display);key.SetValue("Publisher","@Aho11884845");key.SetValue("InstallLocation",root);key.SetValue("UninstallString","\""+uninstall+"\"");key.SetValue("DisplayIcon",Path.Combine(root,"LauncherStarter.exe"));key.SetValue("NoModify",1);key.SetValue("NoRepair",1);}}
    internal static void Unregister(string root){using(var key=Registry.LocalMachine.OpenSubKey(Key)){if(key==null||!String.Equals((string)key.GetValue("InstallLocation"),root,StringComparison.OrdinalIgnoreCase))return;}Registry.LocalMachine.DeleteSubKey(Key,false);}
}
