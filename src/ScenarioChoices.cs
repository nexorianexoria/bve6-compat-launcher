using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Globalization;

internal static class ScenarioChoices {
 internal sealed class Choice { public string Path; public double Weight; }
 public static List<Choice> Parse(string directory,string value){
  if(String.IsNullOrWhiteSpace(value))throw new InvalidDataException("路線または車両の指定がありません。");
  var result=new List<Choice>();
  foreach(string part in value.Split('|')){
   string[] bits=part.Trim().Split('*');if(bits.Length>2||String.IsNullOrWhiteSpace(bits[0]))throw new InvalidDataException("ランダム指定の形式を確認してください: "+part);
   double weight=1;if(bits.Length==2&&(!double.TryParse(bits[1].Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out weight)||double.IsNaN(weight)||double.IsInfinity(weight)||weight<=0))throw new InvalidDataException("ランダム指定の重みは正の数にしてください: "+part);
   result.Add(new Choice{Path=Program.Full(directory,bits[0]),Weight=weight});
  }
  return result;
 }
 public static string Pick(string directory,string value,bool vehicle){
  var available=Parse(directory,value).Where(x=>File.Exists(x.Path)).ToList();
  if(vehicle)available=available.Where(x=>{try{using(var reader=new StreamReader(x.Path)){return Program.VehicleHeader(reader.ReadLine()??"");}}catch{return false;}}).ToList();
  if(available.Count==0)throw new FileNotFoundException("ランダム指定の参照先が見つかりません: "+value);
  if(available.Count==1)return available[0].Path;
  double total=available.Sum(x=>x.Weight);if(double.IsInfinity(total))throw new InvalidDataException("ランダム指定の重みが大きすぎます。");
  byte[] bytes=new byte[8];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);
  double roll=(BitConverter.ToUInt64(bytes,0)/(double)ulong.MaxValue)*total;
  foreach(var item in available){roll-=item.Weight;if(roll<0)return item.Path;}
  return available[available.Count-1].Path;
 }
 public static string AbsoluteExpression(string directory,string value){
  return String.Join(" | ",Parse(directory,value).Select(x=>x.Path+" * "+x.Weight.ToString("R",CultureInfo.InvariantCulture)).ToArray());
 }
 public static bool IsRandom(string value){return !String.IsNullOrWhiteSpace(value)&&value.Contains("|");}
}
