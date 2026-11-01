using System;
using System.IO;
using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Win32;

internal static class BveLocator {
    static string Settings {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BVE6互換ランチャー","bve-locations.xml");}}
    internal static bool IsValid(string path,int version){return Check(path,version,false);}
    internal static bool IsManualValid(string path,int version){return Check(path,version,true);}
    static bool Check(string path,int version,bool allowUnknown){
        try{if(String.IsNullOrWhiteSpace(path)||!File.Exists(path)||!Path.GetFileName(path).Equals("BveTs.exe",StringComparison.OrdinalIgnoreCase))return false;
            int major=FileVersionInfo.GetVersionInfo(path).FileMajorPart;return major==version||(allowUnknown&&major==0);
        }catch{return false;}
    }
    internal static string Resolve(int version){
        if(version!=5&&version!=6)throw new ArgumentOutOfRangeException("version");
        try{if(File.Exists(Settings)){var doc=XDocument.Load(Settings);var node=doc.Root==null?null:doc.Root.Element("BVE"+version);string saved=node==null?null:(string)node.Attribute("path");if(IsManualValid(saved,version))return Path.GetFullPath(saved);}}catch(Exception){}
        string name="BveTs"+version;string first=Environment.GetFolderPath(version==5?Environment.SpecialFolder.ProgramFilesX86:Environment.SpecialFolder.ProgramFiles);string second=Environment.GetFolderPath(version==5?Environment.SpecialFolder.ProgramFiles:Environment.SpecialFolder.ProgramFilesX86);
        foreach(string parent in new[]{first,second}){if(String.IsNullOrWhiteSpace(parent))continue;string candidate=Path.Combine(parent,"mackoy",name,"BveTs.exe");if(IsValid(candidate,version))return candidate;}
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}){
            try{using(var baseKey=RegistryKey.OpenBaseKey(hive,view))using(var uninstall=baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")){
                if(uninstall==null)continue;foreach(string item in uninstall.GetSubKeyNames())using(var key=uninstall.OpenSubKey(item)){
                    if(key==null)continue;string title=key.GetValue("DisplayName") as string;if(String.IsNullOrWhiteSpace(title)||title.IndexOf("BVE",StringComparison.OrdinalIgnoreCase)<0||title.IndexOf("trainsim",StringComparison.OrdinalIgnoreCase)<0)continue;
                    foreach(string value in new[]{key.GetValue("InstallLocation") as string,key.GetValue("DisplayIcon") as string}){
                        if(String.IsNullOrWhiteSpace(value))continue;string p=value.Trim().Trim('"');int comma=p.LastIndexOf(',');if(comma>=0)p=p.Substring(0,comma).Trim().Trim('"');
                        if(Directory.Exists(p))p=Path.Combine(p,"BveTs.exe");if(IsValid(p,version))return Path.GetFullPath(p);
                    }
                }
            }}catch(Exception){}
        }
        return null;
    }
    internal static void Remember(int version,string path){
        if(!IsManualValid(path,version))throw new InvalidDataException("BVE"+version+"のBveTs.exeを選んでください。");
        string dir=Path.GetDirectoryName(Settings);Directory.CreateDirectory(dir);
        XDocument doc=File.Exists(Settings)?XDocument.Load(Settings):new XDocument(new XElement("BveLocations"));
        if(doc.Root==null||doc.Root.Name!="BveLocations")throw new InvalidDataException("BVE本体の保存設定を読み込めません。");
        string key="BVE"+version;var node=doc.Root.Element(key);if(node==null){node=new XElement(key);doc.Root.Add(node);}node.SetAttributeValue("path",Path.GetFullPath(path));
        string temp=Settings+"."+Guid.NewGuid().ToString("N")+".tmp";try{doc.Save(temp);if(File.Exists(Settings))File.Replace(temp,Settings,null);else File.Move(temp,Settings);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
