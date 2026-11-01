using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Drawing;

// The label always identifies its source. A name, a readme and an ATS DLL have
// different meanings; none is a promise that a whole route runs in that version.
internal sealed class DataVersionHint {
 const string Token=@"(?<![A-Za-z0-9])bve(?:\s*(?:trainsim|ts|ver(?:sion)?))?[\s_.-]*([56])(?:\.\d+)?(?!\d)";
 static readonly Regex versions=new Regex(Token,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
 static readonly Regex joint=new Regex(@"(?<![A-Za-z0-9])bve(?:\s*(?:trainsim|ts))?[\s_.-]*(?:5(?:\.\d+)?\s*(?:[/・&、,]|または|および|及び|と|or)\s*(?:bve\s*)?6|6(?:\.\d+)?\s*(?:[/・&、,]|または|および|及び|と|or)\s*(?:bve\s*)?5)(?:\.\d+)?(?!\d)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
 static readonly Regex negative=new Regex(@"非対応|未対応|対応予定|未実装|未検証|未確認|対応(?:してい|でき)ません|対象外|動作(?:不可|しません|しない|しなかった|できません)|サポート(?:外|対象外|しません|には.{0,12}応じません)",RegexOptions.IgnoreCase);
 static readonly Regex positive=new Regex(@"動作確認|動作環境|対応|向け|専用|(?<!使)用|動作対象|推奨|必須",RegexOptions.IgnoreCase);
 readonly string root;readonly Dictionary<string,int> directoryCache=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
 public DataVersionHint(string root){this.root=Path.GetFullPath(root).TrimEnd('\\','/');}
 static int Versions(string value){if(String.IsNullOrWhiteSpace(value))return 0;int mask=joint.IsMatch(value)?3:0;foreach(Match m in versions.Matches(value))mask|=m.Groups[1].Value=="5"?1:2;return mask;}
 static string Label(int mask,string source){return mask==1?"BVE5の記載（"+source+"）":mask==2?"BVE6の記載（"+source+"）":mask==3?"BVE5・6の記載（"+source+"）":null;}
 public static string Display(string hint){
  if(String.IsNullOrWhiteSpace(hint))return "版は不明";
  if(hint.StartsWith("BVE5・6の記載"))return "BVE5 / BVE6";
  if(hint.StartsWith("BVE5の記載"))return "BVE5";
  if(hint.StartsWith("BVE6の記載"))return "BVE6";
  if(hint=="説明書の記載が混在")return "不明";
  return "不明";
 }
 public static Color DisplayColor(string hint){
  string text=Display(hint);
  if(text.Contains("BVE5・6")||text.Contains("BVE5 / BVE6"))return Color.FromArgb(110,65,158);
  if(text.Contains("BVE5"))return Color.FromArgb(29,92,182);
  if(text.Contains("BVE6"))return Color.FromArgb(13,125,93);
  return Color.DimGray;
 }
 public static string Quick(string path,bool vehicle,string support){
  string named=Label(Versions(Path.GetFileNameWithoutExtension(path??"")),"ファイル名");if(named!=null)return named;
  string prefix=vehicle?"ATS: ":"指定車両のATS: ";
  if(support=="BVE5・BVE6互換候補")return prefix+"BVE5向け (32bit)";
  if(support=="BVE6候補")return prefix+"BVE6向け (64bit)";
  if(support=="BVE5・BVE6候補")return prefix+"BVE5・6向け";
  if(support=="BVE5・BVE6候補（ATSなし）")return vehicle?"ATSなし（版は未記載）":"指定車両にATSなし";
  return "対応版: 記載なし";
 }
 public string Describe(string path,string title,bool vehicle,string support,string[] lines){
  string named=Label(Versions(Path.GetFileNameWithoutExtension(path??"")),"ファイル名");if(named!=null)return named;
  named=Label(Versions(title),"タイトル");if(named!=null)return named;
  int evidence=0;
  if(vehicle)evidence=FindDocumentation(Path.GetDirectoryName(path),true,2);
  else if(lines!=null){
   try{string value=Program.Value(lines,"Route");if(!String.IsNullOrWhiteSpace(value)){var dirs=ScenarioChoices.Parse(Path.GetDirectoryName(path),value).Select(x=>Path.GetDirectoryName(x.Path)).Distinct(StringComparer.OrdinalIgnoreCase);foreach(string dir in dirs){int found=FindDocumentation(dir,false,4);if(found==4||(evidence!=0&&found!=0&&evidence!=found)){evidence=4;break;}if(found!=0)evidence=found;}}}
   catch(Exception ex){if(!(ex is ArgumentException)&&!(ex is IOException)&&!(ex is UnauthorizedAccessException))throw;}
   if(evidence==0)evidence=FindDocumentation(Path.GetDirectoryName(path),false,2);
  }
  if(evidence==4)return "説明書の記載が混在";
  var documented=Label(evidence,"説明書");if(documented!=null)return documented;
  string quick=Quick(path,vehicle,support);if(quick!="対応版: 記載なし")return quick;
  if(!vehicle&&lines!=null&&lines.Length>0){var header=Regex.Match(lines[0]??"",@"BveTs\s+Scenario\s+(\d+\.\d+)",RegexOptions.IgnoreCase);if(header.Success)return "シナリオ形式: "+header.Groups[1].Value+"（版は未記載）";}
  return quick;
 }
 int FindDocumentation(string dir,bool vehicle,int levels){
  for(int i=0;i<levels&&!String.IsNullOrEmpty(dir);i++){
   dir=Path.GetFullPath(dir).TrimEnd('\\','/');if(dir.Equals(root,StringComparison.OrdinalIgnoreCase)||!dir.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))break;
   int result=ReadDirectory(dir,vehicle);if(result!=0)return result;dir=Path.GetDirectoryName(dir);
  }return 0;
 }
 int ReadDirectory(string dir,bool vehicle){
  string key=(vehicle?"V|":"R|")+dir;int result;if(directoryCache.TryGetValue(key,out result))return result;
  result=0;try{
   foreach(string file in Directory.EnumerateFiles(dir).Where(x=>{string n=Path.GetFileName(x);return (n.EndsWith(".txt",StringComparison.OrdinalIgnoreCase)||n.EndsWith(".md",StringComparison.OrdinalIgnoreCase))&&(n.IndexOf("readme",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("説明書",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("はじめに",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("お読み",StringComparison.OrdinalIgnoreCase)>=0);}).Take(12)){
    try{if(new FileInfo(file).Length>256*1024)continue;string[] lines=Program.Read(file);string header=String.Join("\n",lines.Take(30).ToArray());
     bool scope=vehicle?Regex.IsMatch(header,@"車両|編成|vehicle|train",RegexOptions.IgnoreCase):Regex.IsMatch(header,@"路線|線|シナリオ|ダイヤ|マップ|map|route|パッケージ",RegexOptions.IgnoreCase);
     if(!scope)continue;int mask=0;
     for(int i=0;i<Math.Min(lines.Length,50);i++){string line=lines[i]??"";if(negative.IsMatch(line))continue;int found=Versions(line);if(found==0)continue;
      bool unrelated=Regex.IsMatch(line,Token+@"\s*(?:(?:用|向け)(?:の)?.{0,16}|対応(?:の)?\s*)(?:プラグイン|ATS|ストラクチャ|音声|サウンド)",RegexOptions.IgnoreCase)&&!Regex.IsMatch(line,@"車両データ|車両用|シナリオデータ|路線データ|パッケージ",RegexOptions.IgnoreCase);
      bool title=i<8&&(vehicle?Regex.IsMatch(line,@"車両|編成|vehicle|train|[0-9]+系",RegexOptions.IgnoreCase):Regex.IsMatch(line,@"路線|線|シナリオ|ダイヤ|マップ|map|route|パッケージ",RegexOptions.IgnoreCase));
      if(!unrelated&&(title||positive.IsMatch(line)))mask|=found;
     }
     if(mask!=0){if(result!=0&&result!=mask){result=4;break;}result=mask;}
    }catch(IOException){}catch(UnauthorizedAccessException){}catch(System.Text.DecoderFallbackException){}
   }
  }catch(IOException){}catch(UnauthorizedAccessException){}
  directoryCache[key]=result;return result;
 }
}
