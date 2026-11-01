using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

// User-only labels. Never modify a BVE route, scenario or vehicle file.
internal static class RouteDisplayNames {
    static readonly object Gate=new object();
    static Dictionary<string,string> labels;
    static bool enabled;
    // Tests point this at a temporary directory; normal use is always LocalAppData.
    internal static string TestFilePath=null;
    static string FilePath {get{return TestFilePath??Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BVE6互換ランチャー","route-display-names.xml");}}
    static string Key(string file){return Path.GetFullPath(file).TrimEnd(Path.DirectorySeparatorChar);}

    static void EnsureLoaded(){
        if(labels!=null)return;
        var found=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        bool setting=false;
        if(File.Exists(FilePath)){
            var document=XDocument.Load(FilePath);
            var root=document.Root;
            if(root!=null && root.Name.LocalName=="RouteDisplayNames"){
                setting=String.Equals((string)root.Attribute("enabled"),"true",StringComparison.OrdinalIgnoreCase);
                foreach(var item in root.Elements("Route")){
                    var path=(string)item.Attribute("path");
                    var name=(string)item.Attribute("name");
                    if(String.IsNullOrWhiteSpace(path)||String.IsNullOrWhiteSpace(name))continue;
                    try{found[Key(path)]=name;}catch(ArgumentException){}catch(NotSupportedException){}
                }
            }
        }
        labels=found;
        enabled=setting;
    }
    internal static bool Enabled {
        get {lock(Gate){EnsureLoaded();return enabled;}}
    }
    internal static string GetName(string file){
        if(String.IsNullOrWhiteSpace(file))return null;
        lock(Gate){
            EnsureLoaded();
            string name;
            return labels.TryGetValue(Key(file),out name)?name:null;
        }
    }
    internal static string Display(string file){
        string original=Path.GetFileNameWithoutExtension(file);
        if(!Enabled)return original;
        string name=GetName(file);
        return String.IsNullOrWhiteSpace(name)?original:name+"（"+original+"）";
    }
    internal static void SetEnabled(bool value){
        lock(Gate){
            EnsureLoaded();
            if(enabled==value)return;
            Persist(labels,value);
            enabled=value;
        }
    }
    internal static void SetName(string file,string value){
        if(String.IsNullOrWhiteSpace(file))throw new ArgumentException("ルートが選択されていません。");
        string name=(value??"").Trim();
        if(name.Length>48)throw new InvalidDataException("ルート名は48文字以内にしてください。");
        if(name.Any(Char.IsControl))throw new InvalidDataException("ルート名に改行や制御文字は使えません。");
        lock(Gate){
            EnsureLoaded();
            var updated=new Dictionary<string,string>(labels,StringComparer.OrdinalIgnoreCase);
            string key=Key(file);
            if(name.Length==0)updated.Remove(key);else updated[key]=name;
            Persist(updated,enabled);
            labels=updated;
        }
    }
    static void Persist(Dictionary<string,string> aliases,bool on){
        string file=FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            var root=new XElement("RouteDisplayNames",new XAttribute("enabled",on?"true":"false"),
                aliases.OrderBy(pair=>pair.Key,StringComparer.OrdinalIgnoreCase)
                    .Select(pair=>new XElement("Route",new XAttribute("path",pair.Key),
                                                      new XAttribute("name",pair.Value))));
            new XDocument(root).Save(temp);
            if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    // Called only by isolated tests to simulate a new application launch.
    internal static void ReloadForTest(){lock(Gate){labels=null;enabled=false;}}
}
