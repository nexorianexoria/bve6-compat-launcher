using System;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading;
internal static class Previews {
 public static string Resolve(string definition,bool vehicle){
  if(String.IsNullOrEmpty(definition))return null;
  return Resolve(definition,vehicle,Program.Read(definition));
 }
 public static string Resolve(string definition,bool vehicle,string[] lines){
  return Resolve(definition,vehicle,lines,null);
 }
 // Optional per-catalog cache: identical panel definitions can be referenced
 // by many vehicle variants. Do not retain this cache across catalog scans.
 internal static string Resolve(string definition,bool vehicle,string[] lines,ConcurrentDictionary<string,Lazy<string>> panelCache){
  if(String.IsNullOrEmpty(definition))return null;
  string dir=Path.GetDirectoryName(definition);
  if(!vehicle){string scenarioImage=Program.Value(lines,"Image");return Existing(dir,scenarioImage);}
  // Without a Panel entry there is no vehicle-specific panel to preview.
  // Do not borrow a different vehicle's panel*.txt from the same folder.
  string panelSpec=Program.Value(lines,"Panel");
  if(String.IsNullOrWhiteSpace(panelSpec))return null;
  string panel=Existing(dir,panelSpec);
  string image=CachedPanelImage(panel,panelCache);if(image!=null)return image;
  // Search only sibling panel definitions, never another vehicle's directory.
  foreach(string candidate in Directory.GetFiles(panel==null?dir:Path.GetDirectoryName(panel),"panel*.txt")){
   if(String.Equals(candidate,panel,StringComparison.OrdinalIgnoreCase))continue;
   image=CachedPanelImage(candidate,panelCache);if(image!=null)return image;
  }
  return null;
 }
 static string CachedPanelImage(string panel,ConcurrentDictionary<string,Lazy<string>> cache){
  if(panel==null)return null;
  if(cache==null)return PanelImage(panel);
  return cache.GetOrAdd(panel,p=>new Lazy<string>(()=>PanelImage(p),
   LazyThreadSafetyMode.ExecutionAndPublication)).Value;
 }
 static string PanelImage(string panel){
  if(panel==null)return null;
  try{
   // Read the panel definition once. The fallback below previously re-read it.
   string[] lines=Program.Read(panel);
   string panelDir=Path.GetDirectoryName(panel);
   bool main=false;string night=null;
   foreach(string raw in lines){
    string line=raw.Trim();
    if(line.StartsWith("[")&&line.EndsWith("]")){
     main=line.Equals("[This]",StringComparison.OrdinalIgnoreCase);continue;
    }
    var pair=Program.Pair(line);
    if(!main)continue;
    if(pair.Key.Equals("DaytimeImage",StringComparison.OrdinalIgnoreCase)){
     string day=Existing(panelDir,pair.Value);if(day!=null)return day;
    }
    if(pair.Key.Equals("NighttimeImage",StringComparison.OrdinalIgnoreCase))night=Existing(panelDir,pair.Value);
   }
   if(night!=null)return night;

   // For a panel without a [This] image, inspect only image dimensions first.
   // Decoding every full-size candidate as a Bitmap is very expensive for
   // large train panels. Decode the largest candidate to verify it is usable;
   // if it is corrupt, continue in descending area order. Stable ties preserve
   // the old first-largest selection rule.
   var candidates=new System.Collections.Generic.List<Tuple<string,long,int>>();
   int sequence=0;string section="";
   var block=new System.Collections.Generic.List<string>();
   Action flush=()=>{
    if(!section.Equals("Pilotlamp",StringComparison.OrdinalIgnoreCase)||
       !String.Equals(Program.Value(block.ToArray(),"Subject"),"true",StringComparison.OrdinalIgnoreCase))return;
    string file=Existing(panelDir,Program.Value(block.ToArray(),"DaytimeImage"))??
                Existing(panelDir,Program.Value(block.ToArray(),"NighttimeImage"));
    if(file==null)return;
    try{
     using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
     using(var image=Image.FromStream(stream,false,false)){
      long pixels=(long)image.Width*image.Height;
      if(pixels>0)candidates.Add(Tuple.Create(file,pixels,sequence++));
     }
    }catch{}
   };
   foreach(string raw in lines){
    string text=raw.Trim();
    if(text.StartsWith("[")&&text.EndsWith("]")){
     flush();block.Clear();section=text.Substring(1,text.Length-2);
    }else block.Add(raw);
   }
   flush();
   foreach(var candidate in candidates.OrderByDescending(c=>c.Item2).ThenBy(c=>c.Item3)){
    try{using(var verified=Load(candidate.Item1)){
     if(verified!=null)return candidate.Item1;
    }}catch{}
   }
   return null;
  }catch(Exception ex){Diagnostics.Write("PREVIEW_PANEL",panel+" : "+ex.Message);return null;}
 }
 static string Existing(string dir,string value){if(String.IsNullOrWhiteSpace(value))return null;string file=Program.Full(dir,value);return File.Exists(file)?file:null;}
 public static Image Load(string path){if(path==null)return null;using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){using(var image=Image.FromStream(stream)){return new Bitmap(image);}}}
}
