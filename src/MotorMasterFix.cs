using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
internal static class MotorMasterFix {
 const string Broken="D6D5F7FF22265EBA7E67D2590968393FB861F7A0EF2F8C2854920CF8BFCE89FA";
 const string Fixed="1BF64D124A12E7C03CDE99F9FF2AE242B195B7890D8DABA5D03A29E70FBBE7E3";
 public const string AuthorPage="https://sites.google.com/view/fwchbve/top/bve%E3%83%87%E3%83%BC%E3%82%BF%E3%83%BC%E5%85%AC%E9%96%8B%E4%B8%80%E8%A6%A7/MotorMaster";
 public static string Hash(string file){using(var sha=SHA256.Create())using(var stream=File.OpenRead(file))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
 public static bool IsRegistered(){try{string cache=Path.Combine(Program.Data,"compat","MotorMaster-"+Fixed+".dll");return File.Exists(cache)&&Hash(cache)==Fixed&&Program.Machine(cache)==0x14c;}catch{return false;}}
 public static void Register(string source){
  if(!File.Exists(source)||Hash(source)!=Fixed||Program.Machine(source)!=0x14c)throw new InvalidDataException("選んだファイルは、この対策で確認済みのMotorMaster.dllと一致しません。作者の配布ページから対応ファイルを入手してください。");
  string cache=Path.Combine(Program.Data,"compat","MotorMaster-"+Fixed+".dll");Directory.CreateDirectory(Path.GetDirectoryName(cache));File.Copy(source,cache,true);Diagnostics.Write("COMPAT_REGISTERED","user-selected file verified by SHA256 and x86 format");
 }
 public static string Apply(string ats,string run){
  if(!Path.GetFileName(ats).Equals("DetailManager.dll",StringComparison.OrdinalIgnoreCase))return ats;
  string manager=Path.GetDirectoryName(ats),motor=Path.Combine(manager,"MotorMaster.dll"),config=Path.Combine(manager,"DetailModules.txt");
  if(!File.Exists(motor)||!File.Exists(config)||!Array.Exists(Program.Read(config),line=>line.Trim().Equals("MotorMaster.dll",StringComparison.OrdinalIgnoreCase))||Hash(motor)!=Broken)return ats;
  // Only this verified binary and its known relative layout are eligible. Other versions are retained.
  string parameter=Path.Combine(manager,"MParameter.txt");if(!File.Exists(parameter))throw new IOException("MotorMasterの設定ファイルがありません。");
  string root=Path.GetDirectoryName(Path.GetDirectoryName(manager));if(String.IsNullOrEmpty(root)||Path.GetPathRoot(root)==root)throw new IOException("ATSの作業コピー範囲を決定できません。");
  foreach(string line in Program.Read(parameter)){var pair=Program.Pair(line);if(pair.Value.IndexOf('\\')>=0||pair.Value.IndexOf('/')>=0){string asset=Program.Full(manager,pair.Value);if(!asset.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(asset))throw new IOException("MotorMasterの参照先を作業コピー内に保持できません: "+asset);}}
  string cache=Path.Combine(Program.Data,"compat","MotorMaster-"+Fixed+".dll");Directory.CreateDirectory(Path.GetDirectoryName(cache));
  if(!IsRegistered())throw new IOException("この車両にはMotorMaster.dllが必要です。作者の配布ページから入手し、画面の『MotorMasterファイルを選ぶ』で選んでください。配布元: "+AuthorPage);
  string staged=Path.Combine(run,"motor-compat");Program.CopyTree(root,staged);string newManager=Path.Combine(staged,manager.Substring(root.Length+1));File.Copy(cache,Path.Combine(newManager,"MotorMaster.dll"),true);
  File.WriteAllText(Path.Combine(run,"MotorMaster対策.txt"),"確認済みの旧MotorMasterのみ、作業コピー内で利用者が登録したファイルへ置き換えました。元の車両データは変更していません。\r\n配布元案内: "+AuthorPage+"\r\n旧SHA256: "+Broken+"\r\n対策SHA256: "+Fixed+"\r\n初期化は検証済み。BVE6での走行・音声の確認は別途必要です。",Encoding.UTF8);Diagnostics.Write("COMPAT_APPLIED","MotorMaster source="+motor+" staged="+newManager);return Path.Combine(newManager,Path.GetFileName(ats));
 }
}
