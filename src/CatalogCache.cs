using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

internal static class CatalogCache {
 const string Version="6";
 static string FilePath {get{return Path.Combine(Program.Data,"catalog-cache.xml");}}
 public static bool TryRead(string root,out List<CatalogItem> scenarios,out List<CatalogItem> vehicles){
  bool upgrade;return TryRead(root,out scenarios,out vehicles,out upgrade);
 }
 public static bool TryRead(string root,out List<CatalogItem> scenarios,out List<CatalogItem> vehicles,out bool upgrade){
  scenarios=null;vehicles=null;
  upgrade=false;
  try{
   if(!File.Exists(FilePath)||new FileInfo(FilePath).Length>16*1024*1024)return false;
   var xml=XDocument.Load(FilePath);var top=xml.Root;
   string version=top==null?null:(string)top.Attribute("version");
   if(top==null||(version!=Version&&version!="5"&&version!="4")||!String.Equals((string)top.Attribute("root"),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase))return false;
   upgrade=version!=Version;scenarios=ReadGroup(top.Element("scenarios"),false,upgrade);vehicles=ReadGroup(top.Element("vehicles"),true,upgrade);return true;
  }catch(Exception ex){Diagnostics.Write("CACHE_READ_ERROR",ex.Message);scenarios=null;vehicles=null;return false;}
 }
 static List<CatalogItem> ReadGroup(XElement parent,bool vehicle,bool upgrade){if(parent==null)throw new InvalidDataException("一覧の保存ファイルが不完全です。");return parent.Elements("item").Select(x=>new CatalogItem{
  Entry=new Program.Entry{Path=(string)x.Attribute("path"),Title=(string)x.Attribute("entryTitle")},
  Title=(string)x.Attribute("title"),Group=(string)x.Attribute("group"),Folder=(string)x.Attribute("folder"),Author=(string)x.Attribute("author"),ImagePath=(string)x.Attribute("image"),SupportHint=(string)x.Attribute("support"),DataHint=upgrade?DataVersionHint.Quick((string)x.Attribute("path"),vehicle,(string)x.Attribute("support")):(string)x.Attribute("dataHint")??DataVersionHint.Quick((string)x.Attribute("path"),vehicle,(string)x.Attribute("support"))
 }).Where(x=>!String.IsNullOrWhiteSpace(x.Entry.Path)).ToList();}
 public static void Write(string root,List<CatalogItem> scenarios,List<CatalogItem> vehicles){
  string temp=null;try{
   Directory.CreateDirectory(Program.Data);var xml=new XDocument(new XElement("catalog",new XAttribute("version",Version),new XAttribute("root",Path.GetFullPath(root)),Group("scenarios",scenarios),Group("vehicles",vehicles)));
   temp=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";xml.Save(temp);
   if(File.Exists(FilePath))File.Replace(temp,FilePath,null);else File.Move(temp,FilePath);
  }catch(Exception ex){Diagnostics.Write("CACHE_WRITE_ERROR",ex.Message);}finally{if(temp!=null&&File.Exists(temp))File.Delete(temp);}
 }
 static XElement Group(string name,IEnumerable<CatalogItem> items){return new XElement(name,items.Select(x=>new XElement("item",
  new XAttribute("path",x.Entry.Path??""),new XAttribute("entryTitle",x.Entry.Title??""),new XAttribute("title",x.Title??""),new XAttribute("group",x.Group??""),new XAttribute("folder",x.Folder??""),new XAttribute("author",x.Author??""),new XAttribute("image",x.ImagePath??""),new XAttribute("support",x.SupportHint??""),new XAttribute("dataHint",x.DataHint??""))));}
}
