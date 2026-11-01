using System;
using System.IO;
using System.Text;

internal static class BveExLocation {
 internal static string Resolve(string bve){
  if(String.IsNullOrWhiteSpace(bve))return null;
  try{string devices=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(bve)),"Input Devices");string caller=Path.Combine(devices,"BveEx.Caller.InputDevice.dll");if(!File.Exists(caller))return null;
   string config=Path.Combine(devices,"BveEx.Caller.InputDevice.txt");string path=File.Exists(config)?File.ReadAllText(config).Trim().Trim('"'):Path.Combine(devices,"BveEx");
   if(String.IsNullOrWhiteSpace(path))return null;if(!Path.IsPathRooted(path))path=Path.GetFullPath(Path.Combine(devices,path));
   return File.Exists(Path.Combine(path,"BveEx.Launcher.dll"))?Path.GetFullPath(path):null;
  }catch(Exception){return null;}
 }
 internal static void Save(string bve,string folder){
  if(!BveLocator.IsManualValid(bve,5)&&!BveLocator.IsManualValid(bve,6))throw new InvalidDataException("BVE5またはBVE6の本体を選んでください。");
  if(String.IsNullOrWhiteSpace(folder)||!Path.IsPathRooted(folder))throw new InvalidDataException("BveEXのフォルダーを選んでください。");
  folder=Path.GetFullPath(folder);InstallEngine.RejectLinks(bve);InstallEngine.RejectLinks(folder);InstallEngine.RejectLinks(Path.Combine(folder,"BveEx.Launcher.dll"));if(!File.Exists(Path.Combine(folder,"BveEx.Launcher.dll")))throw new FileNotFoundException("選んだフォルダーにBveEx.Launcher.dllがありません。");
  string devices=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(bve)),"Input Devices");if(!File.Exists(Path.Combine(devices,"BveEx.Caller.InputDevice.dll")))throw new FileNotFoundException("BVE側にBveEX Callerがありません。BveEXを導入してください。");
  string target=Path.Combine(devices,"BveEx.Caller.InputDevice.txt");InstallEngine.RejectLinks(devices);InstallEngine.RejectLinks(target);string current=Resolve(bve);if(String.Equals(current,folder,StringComparison.OrdinalIgnoreCase))return;
  string temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";string backup=target+"."+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".bak";
  try{using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var bytes=new UTF8Encoding(false).GetBytes(folder);output.Write(bytes,0,bytes.Length);output.Flush(true);}
   if(File.Exists(target))File.Replace(temp,target,backup);else File.Move(temp,target);
  }finally{if(File.Exists(temp))File.Delete(temp);}
 }
}
