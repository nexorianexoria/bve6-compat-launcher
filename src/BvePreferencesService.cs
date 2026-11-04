using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// BVE owns these preferences. Edit only when ALL BVE instances have exited;
// preserve unknown settings and back up the original before any replacement.
internal sealed class BvePreferences {
    internal bool Windowed;
    internal int Width, Height, DrawDistance, BackDrawDistance, Anisotropy;
    internal List<string> InputPlugins = new List<string>();
}
internal static class BvePreferencesService {
    internal static string SettingsDirectory {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "BveTs","Settings"); }
    }
    internal static string PreferencesPath(int version) {
        if(version!=5 && version!=6)throw new ArgumentOutOfRangeException("version");
        return Path.Combine(SettingsDirectory,version==6?"BveTs6.Preferences.xml":"Preferences.xml");
    }
    internal static bool IsGameRunning() {
        var processes=Process.GetProcessesByName("BveTs");
        try{return processes.Length!=0;}finally{foreach(var process in processes)process.Dispose();}
    }
    internal static BvePreferences Read(int version){return ReadFile(PreferencesPath(version));}
    internal static BvePreferences ReadFile(string path){
        var xml=XDocument.Load(path,LoadOptions.PreserveWhitespace);
        XElement root=xml.Root;
        if(root==null || root.Name.LocalName!="Preferences")
            throw new InvalidDataException("BVEの設定ファイルではありません。");
        return new BvePreferences {
            Windowed=Value(root,"Windowed","true").Equals("true",StringComparison.OrdinalIgnoreCase),
            Width=Number(root,"WindowSize","Width",1280),
            Height=Number(root,"WindowSize","Height",720),
            DrawDistance=Number(root,"DrawDistance",800),
            BackDrawDistance=Number(root,"BackDrawDistance",50),
            Anisotropy=Number(root,"Anisotropy",16),
            InputPlugins=Get(root,"InputPlugins")==null?new List<string>():
                Get(root,"InputPlugins").Elements().Where(x=>x.Name.LocalName=="string")
                    .Select(x=>x.Value.Trim()).Where(x=>x.Length>0).ToList()
        };
    }
    private static XElement Get(XElement root,string name){
        return root.Elements().FirstOrDefault(x=>x.Name.LocalName==name);
    }
    private static string Value(XElement root,string name,string fallback) {
        var node=Get(root,name);return node==null?fallback:node.Value.Trim();
    }
    private static int Number(XElement root,string name,int fallback){
        int value;return Int32.TryParse(Value(root,name,""),out value)?value:fallback;
    }
    private static int Number(XElement root,string group,string name,int fallback){
        var child=Get(root,group);return child==null?fallback:Number(child,name,fallback);
    }
    private static XElement Required(XElement parent,string name){
        var node=Get(parent,name);
        if(node!=null)return node;
        node=new XElement(parent.GetDefaultNamespace()+name);parent.Add(node);return node;
    }
    private static void Set(XElement parent,string name,object value){
        Required(parent,name).Value=Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture);
    }
    internal static void Validate(BvePreferences s) {
        if(s.Width<640 || s.Width>7680 || s.Height<400 || s.Height>4320)
            throw new ArgumentOutOfRangeException("WindowSize","画面サイズを確認してください。");
        if(s.DrawDistance<100 || s.DrawDistance>10000 ||
           s.BackDrawDistance<0 || s.BackDrawDistance>5000 ||
           s.Anisotropy<1 || s.Anisotropy>16)
            throw new ArgumentOutOfRangeException("Graphics","描画設定の値を確認してください。");
        if(s.InputPlugins==null || s.InputPlugins.Any(x=>!Regex.IsMatch(x??"","^[A-Za-z0-9_.-]+$")))
            throw new InvalidDataException("入力デバイスの名称が不正です。");
    }
    internal static void Save(int version,BvePreferences s) {
        if(IsGameRunning())throw new InvalidOperationException(
            "BVEが起動中です。終了してから保存してください。");
        SaveFile(PreferencesPath(version),s);
    }
    internal static void SaveFile(string path,BvePreferences s) {
        Validate(s);
        var xml=XDocument.Load(path,LoadOptions.PreserveWhitespace);
        var root=xml.Root;
        if(root==null || root.Name.LocalName!="Preferences")
            throw new InvalidDataException("BVEの設定ファイルではありません。");
        Set(root,"Windowed",s.Windowed?"true":"false");
        var size=Required(root,"WindowSize");
        Set(size,"Width",s.Width);Set(size,"Height",s.Height);
        Set(root,"DrawDistance",s.DrawDistance);
        Set(root,"BackDrawDistance",s.BackDrawDistance);
        Set(root,"Anisotropy",s.Anisotropy);
        var plugins=Required(root,"InputPlugins");
        foreach(var node in plugins.Elements().Where(x=>x.Name.LocalName=="string").ToList())
            node.Remove();
        foreach(string name in s.InputPlugins.Distinct(StringComparer.OrdinalIgnoreCase))
            plugins.Add(new XElement(plugins.GetDefaultNamespace()+"string",name));
        SaveDocument(path,xml);
    }
    internal static void SaveDocument(string path,XDocument xml) {
        string dir=Path.GetDirectoryName(Path.GetFullPath(path));
        if(!Directory.Exists(dir))throw new DirectoryNotFoundException(dir);
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        string backup=path+".launcher-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+
            "-"+Guid.NewGuid().ToString("N").Substring(0,8)+".bak";
        try {
            xml.Save(temp,SaveOptions.DisableFormatting);
            if(!File.Exists(path))throw new FileNotFoundException("元の設定が見つかりません。",path);
            File.Replace(temp,path,backup);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    internal static List<string> DiscoverPlugins(string bveExecutable,IEnumerable<string> active) {
        var available=new HashSet<string>(active??Enumerable.Empty<string>(),StringComparer.OrdinalIgnoreCase);
        if(String.IsNullOrWhiteSpace(bveExecutable))return available.OrderBy(x=>x).ToList();
        string folder=Path.Combine(Path.GetDirectoryName(bveExecutable),"Input Devices");
        if(!Directory.Exists(folder))return available.OrderBy(x=>x).ToList();
        // Avoid enabling libraries and ATS helpers as input plugins.
        string[] known={"BveEx.Caller.InputDevice","Kusaanko.NumerousControllerInterface",
            "Mackoy.TsmcInterface","ZKNS_001interface","SanYingInput"};
        foreach(string name in known)
            if(File.Exists(Path.Combine(folder,name+".dll")))available.Add(name.ToLowerInvariant());
        return available.OrderBy(x=>x,StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

internal sealed class ControllerSwitch {
    internal string Name;
    internal bool Enabled;
}
internal sealed class ControllerProfile {
    internal string Name;
    internal string Profile;
}
internal static class NumerousControllerSettings {
    internal static string PathOnDisk{
        get{return Path.Combine(BvePreferencesService.SettingsDirectory,
            "Kusaanko.NumerousControllerInterface.xml");}
    }
    internal static List<ControllerSwitch> Read(string path){
        if(!File.Exists(path))return new List<ControllerSwitch>();
        var xml=XDocument.Load(path,LoadOptions.PreserveWhitespace);
        var enabled=xml.Descendants().FirstOrDefault(x=>x.Name.LocalName=="IsEnabled");
        if(enabled==null)return new List<ControllerSwitch>();
        return enabled.Elements().Select(row=>new ControllerSwitch{
            Name=row.Elements().FirstOrDefault(x=>x.Name.LocalName=="Key")?.Value,
            Enabled=String.Equals(row.Elements().FirstOrDefault(x=>x.Name.LocalName=="Value")?.Value,
                "true",StringComparison.OrdinalIgnoreCase)
        }).Where(x=>!String.IsNullOrWhiteSpace(x.Name)).ToList();
    }
    internal static void Save(string path,IEnumerable<ControllerSwitch> values){
        SaveAll(path,values,null);
    }
    internal static List<ControllerProfile> ReadProfiles(string path){
        if(!File.Exists(path))return new List<ControllerProfile>();
        var xml=XDocument.Load(path,LoadOptions.PreserveWhitespace);
        var map=xml.Descendants().FirstOrDefault(x=>x.Name.LocalName=="ProfileMap");
        if(map==null)return new List<ControllerProfile>();
        return map.Elements().Select(row=>new ControllerProfile{
            Name=row.Elements().FirstOrDefault(x=>x.Name.LocalName=="Key")?.Value,
            Profile=row.Elements().FirstOrDefault(x=>x.Name.LocalName=="Value")?.Value
        }).Where(x=>!String.IsNullOrWhiteSpace(x.Name) &&
            !String.IsNullOrWhiteSpace(x.Profile)).ToList();
    }
    internal static void SaveAll(string path,IEnumerable<ControllerSwitch> values,
        IEnumerable<ControllerProfile> profiles){
        var edits=values.ToDictionary(x=>x.Name,x=>x.Enabled,StringComparer.Ordinal);
        var xml=XDocument.Load(path,LoadOptions.PreserveWhitespace);
        var parent=xml.Descendants().FirstOrDefault(x=>x.Name.LocalName=="IsEnabled");
        if(parent==null)throw new InvalidDataException("マスコン設定が見つかりません。");
        foreach(var row in parent.Elements()){
            var key=row.Elements().FirstOrDefault(x=>x.Name.LocalName=="Key");
            var value=row.Elements().FirstOrDefault(x=>x.Name.LocalName=="Value");
            bool enabled;
            if(key!=null && value!=null && edits.TryGetValue(key.Value,out enabled))
                value.Value=enabled?"true":"false";
        }
        if(profiles!=null){
            var selected=profiles.ToDictionary(x=>x.Name,x=>x.Profile,StringComparer.Ordinal);
            var map=xml.Descendants().FirstOrDefault(x=>x.Name.LocalName=="ProfileMap");
            if(map==null)throw new InvalidDataException("プロファイル設定が見つかりません。");
            var allowed=new HashSet<string>(ReadProfiles(path).Select(x=>x.Profile),StringComparer.Ordinal);
            foreach(var row in map.Elements()){
                var key=row.Elements().FirstOrDefault(x=>x.Name.LocalName=="Key");
                var value=row.Elements().FirstOrDefault(x=>x.Name.LocalName=="Value");
                string profile;
                if(key!=null && value!=null && selected.TryGetValue(key.Value,out profile)){
                    if(!allowed.Contains(profile))
                        throw new InvalidDataException("未登録のプロファイルは指定できません: "+profile);
                    value.Value=profile;
                }
            }
        }
        BvePreferencesService.SaveDocument(path,xml);
    }
}
