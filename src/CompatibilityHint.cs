using System;
using System.IO;

internal static class CompatibilityHint {
 public static string ForVehicle(string vehicle){
  if(String.IsNullOrWhiteSpace(vehicle)||!File.Exists(vehicle))return "車両選択が必要";
  try {return ForVehicle(vehicle,Program.Read(vehicle));}
  catch(Exception ex){Diagnostics.Write("SUPPORT_HINT",vehicle+" : "+ex.Message);return "形式を判定できません";}
 }
 public static string ForVehicle(string vehicle,string[] lines){
  if(String.IsNullOrWhiteSpace(vehicle)||!File.Exists(vehicle))return "車両選択が必要";
  try {
   if(lines.Length==0||!Program.VehicleHeader(lines[0]))return "形式を判定できません";
   string dir=Path.GetDirectoryName(vehicle);
   string native=Program.Value(lines,"Ats64"),legacy=Program.Value(lines,"Ats32");
   if(String.IsNullOrWhiteSpace(legacy))legacy=Program.Value(lines,"Ats");
   bool hasNative=!String.IsNullOrWhiteSpace(native),hasLegacy=!String.IsNullOrWhiteSpace(legacy);
   if(!hasNative&&!hasLegacy)return "BVE5・BVE6候補（ATSなし）";
   bool bve6=hasNative&&ValidMachine(dir,native,0x8664);
   bool bve5=hasLegacy&&ValidMachine(dir,legacy,0x14c);
   if(bve5&&bve6)return "BVE5・BVE6候補";
   if(bve6)return "BVE6候補";
   if(bve5)return "BVE5・BVE6互換候補";
   return "ATS形式・参照先を確認";
  }catch(Exception ex){Diagnostics.Write("SUPPORT_HINT",vehicle+" : "+ex.Message);return "形式を判定できません";}
 }
 static bool ValidMachine(string dir,string value,ushort expected){
  try {string path=Program.Full(dir,value);return File.Exists(path)&&Program.Machine(path)==expected;}
  catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}catch(ArgumentException){return false;}
 }
}
