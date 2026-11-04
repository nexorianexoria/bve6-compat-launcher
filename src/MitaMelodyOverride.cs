using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

// Strictly scoped compatibility experiment for the user's TKK3020/3121
// vehicle on a Toei Mita Line scenario. Never modify the original vehicle,
// sound, ATS, BveEX or route data.
internal static class MitaMelodyOverride {
    internal static bool IsCandidate(string scenario,string vehicle) {
        if(String.IsNullOrWhiteSpace(vehicle)||!File.Exists(vehicle) ||
           !Path.GetFileName(vehicle).Equals("3121.txt",StringComparison.OrdinalIgnoreCase) ||
           !Path.GetFileName(Path.GetDirectoryName(vehicle)).Equals("TKK3020",StringComparison.OrdinalIgnoreCase))
            return false;
        var routeTitle=Program.Value(Program.Read(scenario),"RouteTitle");
        return String.Equals(routeTitle,"地下鉄三田線",StringComparison.Ordinal);
    }
    internal static string StageVehicle(string vehicle,string run,List<string> warnings){
        string dir=Path.GetDirectoryName(Path.GetFullPath(vehicle));
        string[] original=Program.Read(vehicle);
        string originalSound=Program.Value(original,"Sound");
        if(String.IsNullOrWhiteSpace(originalSound))
            throw new InvalidDataException("車両に音声設定がありません。");
        string soundPath=Program.Full(dir,originalSound);
        string stagedSound=StageSound(soundPath,run,warnings);
        var result=new List<string>{"BveTs Vehicle 2.00:shift_jis"};
        var pathKeys=new HashSet<string>(new[]{
            "Ats","Ats32","Ats64","PerformanceCurve","Parameters",
            "Panel","Sound","MotorNoise"},StringComparer.OrdinalIgnoreCase);
        foreach(string raw in original.Skip(1)){
            var pair=Program.Pair(raw);
            if(pair.Key.Equals("Sound",StringComparison.OrdinalIgnoreCase)){
                result.Add("Sound = "+stagedSound);
            }else if(pathKeys.Contains(pair.Key)&&!String.IsNullOrWhiteSpace(pair.Value)){
                result.Add(pair.Key+" = "+Program.Full(dir,pair.Value));
            }else result.Add(raw);
        }
        string staged=Path.Combine(run,"vehicle.txt");
        File.WriteAllLines(staged,result,Encoding.GetEncoding(932));
        foreach(string file in new[]{"VehiclePluginUsing.xml",
            Path.GetFileNameWithoutExtension(vehicle)+".VehiclePluginUsing.xml"}){
            string source=Path.Combine(dir,file);
            if(!File.Exists(source))continue;
            var xml=XDocument.Load(source);
            foreach(var attribute in xml.Descendants().Attributes("Path")){
                if(!String.IsNullOrWhiteSpace(attribute.Value))
                    attribute.Value=Program.Full(dir,attribute.Value);
            }
            xml.Save(Path.Combine(run,file));
        }
        return staged;
    }
    internal static string StageSound(string original,string run,List<string> warnings){
        string[] lines=Program.Read(original);
        var source=new Dictionary<int,string>();
        string section="";
        foreach(var raw in lines){
            string trimmed=raw.Trim();
            if(trimmed.StartsWith("[")&&trimmed.EndsWith("]")){
                section=trimmed.Substring(1,trimmed.Length-2);
                continue;
            }
            if(!section.Equals("Ats",StringComparison.OrdinalIgnoreCase))continue;
            var pair=Program.Pair(raw);
            int number;
            if(int.TryParse(pair.Key,out number) && number>=200 && number<=205)
                source[number]=pair.Value;
        }
        foreach(int number in new[]{200,201,202,203,204,205}){
            if(!source.ContainsKey(number))
                throw new InvalidDataException("この車両の都営用音声番号が確認できません: "+number);
        }
        // An explicit original-file annotation is required: do not repurpose
        // arbitrary sound channels from unrelated stock or plugin versions.
        foreach(int number in new[]{204,205}){
            string line=lines.FirstOrDefault(x=>
                x.TrimStart().StartsWith(number.ToString()+" ",StringComparison.Ordinal));
            if(line==null||!line.Contains("都営乗降促進"))
                throw new InvalidDataException("都営用音声の登録内容を確認できません。");
        }
        string sound=Program.ConvertSound(original,run,warnings);
        var converted=Program.Read(sound);
        var replacements=new Dictionary<int,int>{{200,204},{201,205},{202,204},{203,205}};
        var changed=new HashSet<int>();
        section="";
        for(int index=0;index<converted.Length;index++){
            string trimmed=converted[index].Trim();
            if(trimmed.StartsWith("[")&&trimmed.EndsWith("]")){
                section=trimmed.Substring(1,trimmed.Length-2);
                continue;
            }
            if(!section.Equals("Ats",StringComparison.OrdinalIgnoreCase))continue;
            var pair=Program.Pair(converted[index]);
            int number,target;
            if(int.TryParse(pair.Key,out number) && replacements.TryGetValue(number,out target)){
                string replacement=Program.Pair(converted.First(x=>
                    Program.Pair(x).Key==target.ToString())).Value;
                if(!File.Exists(replacement))
                    throw new FileNotFoundException("都営用音声がありません。",replacement);
                converted[index]=number+" = "+replacement;
                changed.Add(number);
            }
        }
        if(changed.Count!=4)throw new InvalidDataException("メロディーの割り当てを更新できませんでした。");
        File.WriteAllLines(sound,converted,Encoding.GetEncoding(932));
        File.WriteAllText(Path.Combine(run,"三田線の音声調整.txt"),
            "三田線用の一時的な音声割り当てです。車両の元データは変更していません。"+
            Environment.NewLine+
            "[Ats] 200→204、201→205、202→204、203→205"+
            Environment.NewLine+
            "G/Hキーが実際に参照する音声番号は未確認です。実機で確認してください。",
            new UTF8Encoding(false));
        return sound;
    }
}
