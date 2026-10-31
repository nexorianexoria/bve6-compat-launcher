using System;
using System.IO;
using System.Security.Cryptography;

internal static class DependencyCheck {
    internal const string BveExUrl="https://bveex.okaoka-depot.com/";
    internal const string MotorMasterUrl="https://sites.google.com/view/fwchbve/top/bve%E3%83%87%E3%83%BC%E3%82%BF%E3%83%BC%E5%85%AC%E9%96%8B%E4%B8%80%E8%A6%A7/MotorMaster";
    const string Fixed="1BF64D124A12E7C03CDE99F9FF2AE242B195B7890D8DABA5D03A29E70FBBE7E3";
    internal static bool BveExPresent(string bveFolder){
        try{
            return BveExLocation.Resolve(Path.Combine(bveFolder,"BveTs.exe"))!=null;
        }catch(Exception){return false;}
    }
    internal static bool MotorMasterRegistered(){
        try{string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BVE6互換ランチャー","作業データ","compat","MotorMaster-"+Fixed+".dll");
            if(!File.Exists(file))return false;using(var hash=SHA256.Create())using(var stream=File.OpenRead(file))return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").Equals(Fixed,StringComparison.OrdinalIgnoreCase);
        }catch(Exception){return false;}
    }
}
