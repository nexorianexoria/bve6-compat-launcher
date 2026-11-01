using System;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Xml.Linq;

internal sealed class ScanIndex {
    sealed class Record {internal long Time,Length;internal int Kind;}
    readonly ConcurrentDictionary<string,Record> records=new ConcurrentDictionary<string,Record>(StringComparer.OrdinalIgnoreCase);
    readonly string root; static string FilePath {get{return Path.Combine(Program.Data,"scan-index.xml");}}
    ScanIndex(string path){root=Path.GetFullPath(path);}
    internal static ScanIndex Load(string root){
        var result=new ScanIndex(root);
        try{
            if(!File.Exists(FilePath)||new FileInfo(FilePath).Length>128L*1024*1024)return result;
            var xml=XDocument.Load(FilePath).Root;
            if(xml==null||(string)xml.Attribute("version")!="3"||!String.Equals((string)xml.Attribute("root"),result.root,StringComparison.OrdinalIgnoreCase))return result;
            foreach(var x in xml.Elements("f")){
                string path=(string)x.Attribute("p");long time,length;int kind;
                if(String.IsNullOrWhiteSpace(path)||!Int64.TryParse((string)x.Attribute("t"),out time)||!Int64.TryParse((string)x.Attribute("s"),out length)||!Int32.TryParse((string)x.Attribute("k"),out kind)||kind<0||kind>2)continue;
                result.records[path]=new Record{Time=time,Length=length,Kind=kind};
            }
        }catch(Exception ex){Diagnostics.Write("SCAN_INDEX_READ",ex.Message);result.records.Clear();}
        return result;
    }
    internal bool TryGet(FileInfo file,out int kind){Record r;kind=0;if(!records.TryGetValue(file.FullName,out r))return false;if(r.Time!=file.LastWriteTimeUtc.Ticks||r.Length!=file.Length)return false;kind=r.Kind;return true;}
    internal void Put(FileInfo file,int kind){records[file.FullName]=new Record{Time=file.LastWriteTimeUtc.Ticks,Length=file.Length,Kind=kind};}
    internal void Save(IEnumerable<string> seen){
        string temp=null;
        try{
            Directory.CreateDirectory(Program.Data);var top=new XElement("scanIndex",new XAttribute("version","3"),new XAttribute("root",root));
            foreach(string path in seen){Record r;if(records.TryGetValue(path,out r))top.Add(new XElement("f",new XAttribute("p",path),new XAttribute("t",r.Time),new XAttribute("s",r.Length),new XAttribute("k",r.Kind)));}
            temp=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";new XDocument(top).Save(temp);
            if(File.Exists(FilePath))File.Replace(temp,FilePath,null);else File.Move(temp,FilePath);
        }catch(Exception ex){Diagnostics.Write("SCAN_INDEX_WRITE",ex.Message);}finally{if(temp!=null&&File.Exists(temp))File.Delete(temp);}
    }
}
