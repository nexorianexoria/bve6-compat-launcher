using System;
using System.IO;
using System.Drawing;
internal static class Previews {
 public static string Resolve(string definition,bool vehicle){
  if(String.IsNullOrEmpty(definition))return null;
  return Resolve(definition,vehicle,Program.Read(definition));
 }
 public static string Resolve(string definition,bool vehicle,string[] lines){
  if(String.IsNullOrEmpty(definition))return null;
  string dir=Path.GetDirectoryName(definition);
  if(!vehicle){string scenarioImage=Program.Value(lines,"Image");return Existing(dir,scenarioImage);}
  string panel=Existing(dir,Program.Value(lines,"Panel"));
  string image=PanelImage(panel);if(image!=null)return image;
  // Search only sibling panel definitions, never another vehicle's directory.
  foreach(string candidate in Directory.GetFiles(panel==null?dir:Path.GetDirectoryName(panel),"panel*.txt")){
   if(String.Equals(candidate,panel,StringComparison.OrdinalIgnoreCase))continue;
   image=PanelImage(candidate);if(image!=null)return image;
  }
  return null;
 }
 static string PanelImage(string panel){
  if(panel==null)return null;
  try{bool main=false;string night=null;foreach(string raw in Program.Read(panel)){string line=raw.Trim();if(line.StartsWith("[")&&line.EndsWith("]")){main=line.Equals("[This]",StringComparison.OrdinalIgnoreCase);continue;}var pair=Program.Pair(line);if(!main)continue;if(pair.Key.Equals("DaytimeImage",StringComparison.OrdinalIgnoreCase)){string day=Existing(Path.GetDirectoryName(panel),pair.Value);if(day!=null)return day;}if(pair.Key.Equals("NighttimeImage",StringComparison.OrdinalIgnoreCase))night=Existing(Path.GetDirectoryName(panel),pair.Value);}if(night!=null)return night;string best=null;long area=0;string section="";var block=new System.Collections.Generic.List<string>();Action flush=()=>{if(!section.Equals("Pilotlamp",StringComparison.OrdinalIgnoreCase)||!String.Equals(Program.Value(block.ToArray(),"Subject"),"true",StringComparison.OrdinalIgnoreCase))return;string file=Existing(Path.GetDirectoryName(panel),Program.Value(block.ToArray(),"DaytimeImage"))??Existing(Path.GetDirectoryName(panel),Program.Value(block.ToArray(),"NighttimeImage"));if(file==null)return;try{using(var bitmap=Load(file)){long size=(long)bitmap.Width*bitmap.Height;if(size>area){best=file;area=size;}}}catch{}};foreach(string raw in Program.Read(panel)){string text=raw.Trim();if(text.StartsWith("[")&&text.EndsWith("]")){flush();block.Clear();section=text.Substring(1,text.Length-2);}else block.Add(raw);}flush();return best;}
  catch(Exception ex){Diagnostics.Write("PREVIEW_PANEL",panel+" : "+ex.Message);return null;}
 }
 static string Existing(string dir,string value){if(String.IsNullOrWhiteSpace(value))return null;string file=Program.Full(dir,value);return File.Exists(file)?file:null;}
 public static Image Load(string path){if(path==null)return null;using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){using(var image=Image.FromStream(stream)){return new Bitmap(image);}}}
}
