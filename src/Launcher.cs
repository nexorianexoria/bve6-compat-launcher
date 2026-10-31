using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Threading;
using System.Xml.Linq;

internal static class Program {
    public static string Home = AppDomain.CurrentDomain.BaseDirectory;
    public static string Data = File.Exists(Path.Combine(Home,"installed.marker")) || Home.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),StringComparison.OrdinalIgnoreCase) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BVE6互換ランチャー","作業データ") : Path.Combine(Home,"作業データ");
    [STAThread] static int Main(string[] args) {
        try {
            Directory.CreateDirectory(Data); Diagnostics.Initialize(); Application.ThreadException+=(sender,eventArgs)=>{Diagnostics.Error("UI_ERROR",eventArgs.Exception);MessageBox.Show(eventArgs.Exception.Message,"エラーが発生しました。ログ・バグ報告から記録を確認できます。");}; AppDomain.CurrentDomain.UnhandledException+=(sender,eventArgs)=>Diagnostics.Write("UNHANDLED",eventArgs.ExceptionObject.ToString());
            if(args.Length == 3 && args[0] == "--prepare") { Console.WriteLine(Prepare(args[1], args[2]).Scenario); return 0; }
            Application.EnableVisualStyles(); Application.Run(new Launcher()); return 0;
        } catch(Exception e) { Diagnostics.Error("FATAL",e); File.WriteAllText(Path.Combine(Data,"error.txt"),e.ToString()); MessageBox.Show(e.Message,"準備できませんでした"); return 1; }
    }
    public sealed class Entry { public string Path; public string Title; public override string ToString(){return Title;} }
    public sealed class ScanResult { public List<Entry> Scenarios, Vehicles; }
    public sealed class Prepared { public string Scenario, Ats, Work, Mode, Log; public readonly List<string> Warnings=new List<string>(); }
    public static string[] Read(string path) {
        using(var r=new StreamReader(path,Encoding.ASCII,true)){ string h=r.ReadLine()??"";
            Encoding enc=h.IndexOf("utf-8",StringComparison.OrdinalIgnoreCase)>=0 ? new UTF8Encoding(false,true) : h.IndexOf("shift",StringComparison.OrdinalIgnoreCase)>=0 ? Encoding.GetEncoding(932) : new UTF8Encoding(false,true);
            try { return File.ReadAllLines(path,enc); } catch(DecoderFallbackException){return File.ReadAllLines(path,Encoding.GetEncoding(932));}
        }
    }
    public static bool VehicleHeader(string h){h=h.TrimStart('\uFEFF').Trim();return new[]{"BveTs Vehicle 0.03","BveTs Vehicle 1.00","BveTs Vehicle 2.00"}.Any(x=>h.Equals(x,StringComparison.OrdinalIgnoreCase)||(h.StartsWith(x,StringComparison.OrdinalIgnoreCase)&&h.Substring(x.Length).TrimStart().StartsWith(":")));}
    public static bool ScenarioHeader(string h){h=h.TrimStart('\uFEFF').Trim();return new[]{"BveTs Scenario 1.00","BveTs Scenario 2.00"}.Any(x=>h.StartsWith(x,StringComparison.OrdinalIgnoreCase)&&(h.Substring(x.Length).Trim().Length==0||h.Substring(x.Length).TrimStart().StartsWith(":")));}
    public static KeyValuePair<string,string> Pair(string s) {int comment=s.IndexOfAny(new[]{';','#'}); if(comment>=0)s=s.Substring(0,comment); int n=s.IndexOf('='); return n<0 ? new KeyValuePair<string,string>("","") : new KeyValuePair<string,string>(s.Substring(0,n).Trim(),s.Substring(n+1).Trim());}
    public static string Value(string[] lines,string key){return lines.Select(Pair).Where(p=>p.Key.Equals(key,StringComparison.OrdinalIgnoreCase)).Select(p=>p.Value).LastOrDefault();}
    public static string Full(string dir,string path){return System.IO.Path.GetFullPath(System.IO.Path.Combine(dir,path.Trim()));}
    public static ushort Machine(string path) {using(var r=new BinaryReader(File.OpenRead(path))){if(r.ReadUInt16()!=0x5a4d)throw new InvalidDataException("ATSがWindows DLLではありません: "+path); r.BaseStream.Position=0x3c; int offset=r.ReadInt32(); if(offset<0||offset>r.BaseStream.Length-6)throw new InvalidDataException("ATSのファイルが壊れています。"); r.BaseStream.Position=offset;if(r.ReadUInt32()!=0x4550)throw new InvalidDataException("ATS形式が不正です。");return r.ReadUInt16();}}
    public static string FirstPath(string dir,string value){return ScenarioChoices.Pick(dir,value,false);}
    public static string DefaultVehicle(string scenario){
        return DefaultVehicle(scenario,Read(scenario));
    }
    public static string DefaultVehicle(string scenario,string[] lines){
        string specified=Value(lines,"Vehicle");
        if(String.IsNullOrWhiteSpace(specified))return null;
        try{return ScenarioChoices.Pick(Path.GetDirectoryName(scenario),specified,true);}
        catch(FileNotFoundException){return null;}
    }
    public static string RequireVehicle(string scenario,string selected){
        if(!String.IsNullOrWhiteSpace(selected)){
            if(!File.Exists(selected))throw new InvalidDataException("選択した車両ファイルを確認できません。別の車両を選んでください。");
            var definition=Read(selected);if(definition.Length==0||!VehicleHeader(definition[0]))throw new InvalidDataException("選択した車両ファイルを確認できません。別の車両を選んでください。");
            return Path.GetFullPath(selected);
        }
        string normal=DefaultVehicle(scenario);
        if(normal==null)throw new InvalidOperationException("このシナリオの車両が見つかりません。右の一覧から使用する車両を選んでください。");
        return normal;
    }
    public static Prepared PrepareNormal(string scenario,string selected,bool bve5){
        scenario=Path.GetFullPath(scenario);var lines=Read(scenario);
        if(lines.Length==0||!ScenarioHeader(lines[0]))throw new NotSupportedException("BVE5・BVE6形式のシナリオを選んでください。");
        string sd=Path.GetDirectoryName(scenario),routeSpec=Value(lines,"Route"),vehicleSpec=Value(lines,"Vehicle");
        var result=new Prepared{Scenario=scenario,Work=sd,Mode=bve5?"BVE5通常起動":"BVE6通常起動"};
        if(String.IsNullOrWhiteSpace(routeSpec))throw new InvalidOperationException("このシナリオの路線が指定されていません。");
        if(String.IsNullOrWhiteSpace(selected)){
            if(String.IsNullOrWhiteSpace(vehicleSpec))throw new InvalidOperationException("このシナリオの車両が指定されていません。右の一覧から使用する車両を選んでください。");
            if(!ScenarioChoices.IsRandom(vehicleSpec))RequireVehicle(scenario,null);
            return result;
        }
        string vehicle=RequireVehicle(scenario,selected),route=ScenarioChoices.AbsoluteExpression(sd,routeSpec);
        string run=Path.Combine(Data,"run-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));Directory.CreateDirectory(run);
        var output=new List<string>{lines[0].TrimStart('\uFEFF').StartsWith("BveTs Scenario 1.00",StringComparison.OrdinalIgnoreCase)?"BveTs Scenario 1.00:shift_jis":"BveTs Scenario 2.00:shift_jis"};
        foreach(string line in lines.Skip(1)){var pair=Pair(line);
            if(pair.Key.Equals("Vehicle",StringComparison.OrdinalIgnoreCase))output.Add("Vehicle = "+vehicle);
            else if(pair.Key.Equals("Route",StringComparison.OrdinalIgnoreCase))output.Add("Route = "+route);
            else if(pair.Key.Equals("Image",StringComparison.OrdinalIgnoreCase)&&!String.IsNullOrWhiteSpace(pair.Value)){
                try {output.Add("Image = "+FirstPath(sd,pair.Value));}catch(FileNotFoundException){output.Add("; 画像の参照先が見つからないため省略しました。");}
            }else output.Add(line);
        }
        result.Scenario=Path.Combine(run,"scenario.txt");File.WriteAllLines(result.Scenario,output,Encoding.GetEncoding(932));
        File.WriteAllLines(Path.Combine(run,"準備記録.txt"),new[]{"元シナリオ: "+scenario,"選択した車両: "+vehicle,"方式: "+result.Mode,"元の路線・車両ファイルは変更していません。"},Encoding.UTF8);
        return result;
    }
    public static Prepared Prepare(string scenario,string vehicle) {
        scenario=System.IO.Path.GetFullPath(scenario); var sl=Read(scenario); if(sl.Length==0||!ScenarioHeader(sl[0]))throw new NotSupportedException("BVE5・BVE6形式のシナリオを選んでください。");
        string sd=System.IO.Path.GetDirectoryName(scenario); vehicle=RequireVehicle(scenario,vehicle);
        vehicle=System.IO.Path.GetFullPath(vehicle); var vl=Read(vehicle); if(!VehicleHeader(vl[0]))throw new NotSupportedException("車両定義ファイルではありません。");
        string vd=System.IO.Path.GetDirectoryName(vehicle); string ats64=Value(vl,"Ats64"), ats32=Value(vl,"Ats32"), ats=Value(vl,"Ats");
        var missing=vl.Select(Pair).Where(p=>new[]{"PerformanceCurve","Parameters","Panel","Sound","MotorNoise"}.Contains(p.Key,StringComparer.OrdinalIgnoreCase)&&!String.IsNullOrWhiteSpace(p.Value)).Where(p=>!File.Exists(Full(vd,p.Value))).Select(p=>p.Key+": "+Full(vd,p.Value)).ToList();
        string fallbackSpec=!String.IsNullOrWhiteSpace(ats32)?ats32:ats;
        bool has64=!String.IsNullOrWhiteSpace(ats64)&&File.Exists(Full(vd,ats64));
        if(!has64&&!String.IsNullOrWhiteSpace(fallbackSpec)&&!File.Exists(Full(vd,fallbackSpec)))missing.Add("ATS: "+Full(vd,fallbackSpec));
        if(!has64&&String.IsNullOrWhiteSpace(fallbackSpec)&&!String.IsNullOrWhiteSpace(ats64))missing.Add("Ats64: "+Full(vd,ats64));
        if(missing.Count>0)throw new FileNotFoundException("車両に必要なファイルが見つかりません。車両データの配布元・展開先を確認してください。\n\n"+String.Join("\n",missing.ToArray()));
        string target=null; bool bridge=false;
        if(!String.IsNullOrWhiteSpace(ats64) && File.Exists(Full(vd,ats64)) && Machine(Full(vd,ats64))==0x8664)target=Full(vd,ats64);
        else { string fallback=!String.IsNullOrWhiteSpace(ats32)?ats32:ats; if(!String.IsNullOrWhiteSpace(fallback)){target=Full(vd,fallback); var m=Machine(target);if(m!=0x14c && m!=0x8664)throw new NotSupportedException("このATSのCPU形式には対応していません。");bridge=m==0x14c;} else if(!String.IsNullOrWhiteSpace(ats64))throw new FileNotFoundException("指定された64bit ATSが見つからず、32bit ATSも指定されていません。"); }
        string run=System.IO.Path.Combine(Data,"run-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));Directory.CreateDirectory(run);
        string originalAts=target;
        if(bridge){target=StageManager(target,run);target=MotorMasterFix.Apply(target,run);}
        var result=new Prepared{Ats=target,Work=target==null?vd:System.IO.Path.GetDirectoryName(target),Mode=target==null?"ATSなし":bridge?"32bit ATSを橋渡し":"64bit ATSを直接使用",Log=System.IO.Path.Combine(run,"bridge.log")};
        string frontend=System.IO.Path.Combine(Home,"bridge","Bve5VehicleCompatibility_.dll"); if(bridge && (!File.Exists(frontend)||!File.Exists(System.IO.Path.Combine(Home,"bridge","Bve5VehicleCompatibility.exe"))))throw new FileNotFoundException("橋渡し用ファイルが不足しています。配布フォルダ全体を展開してください。");
        var output=new List<string>{"BveTs Vehicle 2.00:shift_jis"};
        foreach(var line in vl.Skip(1)){var p=Pair(line); if(new[]{"Ats","Ats32","Ats64"}.Contains(p.Key,StringComparer.OrdinalIgnoreCase))continue;
            if(new[]{"PerformanceCurve","Parameters","Panel","Sound","MotorNoise"}.Contains(p.Key,StringComparer.OrdinalIgnoreCase)&&!String.IsNullOrWhiteSpace(p.Value)){
                string asset=Full(vd,p.Value);if(!File.Exists(asset))throw new FileNotFoundException(p.Key+"の参照先がありません: "+asset);
                if(p.Key.Equals("Sound",StringComparison.OrdinalIgnoreCase))asset=ConvertSound(asset,run,result.Warnings);
                output.Add(p.Key+" = "+asset);
            } else output.Add(line);
        }
        if(target!=null){if(bridge)output.Add("Ats32 = "+target);output.Add("Ats64 = "+(bridge?frontend:target));}
        string shadow=System.IO.Path.Combine(run,"vehicle.txt"); File.WriteAllLines(shadow,output,Encoding.GetEncoding(932));
        var copiedBveEx=new List<string>();
        foreach(var config in new[]{new[]{System.IO.Path.Combine(vd,"VehiclePluginUsing.xml"),"VehiclePluginUsing.xml"},new[]{System.IO.Path.ChangeExtension(vehicle,".VehiclePluginUsing.xml"),"vehicle.VehiclePluginUsing.xml"}}){
            if(!File.Exists(config[0]))continue;
            var xml=XDocument.Load(config[0]);foreach(var attribute in xml.Descendants().Attributes("Path")){if(String.IsNullOrWhiteSpace(attribute.Value))continue;string resolved=Full(System.IO.Path.GetDirectoryName(config[0]),attribute.Value);if(!File.Exists(resolved))throw new FileNotFoundException("車両付属のBveEXプラグインが見つかりません: "+resolved);attribute.Value=resolved;}
            xml.Save(System.IO.Path.Combine(run,config[1]));copiedBveEx.Add(config[1]);
        }
        string selectedRoute=FirstPath(sd,Value(sl,"Route"));var so=new List<string>{"BveTs Scenario 2.00:shift_jis"}; foreach(string line in sl.Skip(1)){var p=Pair(line);if(p.Key.Equals("Vehicle",StringComparison.OrdinalIgnoreCase))so.Add("Vehicle = "+shadow);else if(p.Key.Equals("Route",StringComparison.OrdinalIgnoreCase))so.Add("Route = "+selectedRoute);else if(p.Key.Equals("Image",StringComparison.OrdinalIgnoreCase)&&!String.IsNullOrWhiteSpace(p.Value)){try{so.Add("Image = "+FirstPath(sd,p.Value));}catch(FileNotFoundException){result.Warnings.Add("シナリオ画像: "+p.Value);so.Add("; 見つからないシナリオ画像を省略しました。");}}else so.Add(line);}
        result.Scenario=System.IO.Path.Combine(run,"scenario.txt");File.WriteAllLines(result.Scenario,so,Encoding.GetEncoding(932));
        File.WriteAllLines(System.IO.Path.Combine(run,"準備記録.txt"),new[]{"元シナリオ: "+scenario,"抽選後の路線: "+selectedRoute,"元車両: "+vehicle,"元ATS: "+originalAts,"実行ATS: "+target,"方式: "+result.Mode,"付属BveEX設定: "+(copiedBveEx.Count>0?String.Join(", ",copiedBveEx.ToArray()):"なし"),"注意: 走行・音声・ATSの実機動作は未確認です。"},Encoding.UTF8);return result;
    }
    static string StageManager(string ats,string run){
        if(!System.IO.Path.GetFileName(ats).Equals("DetailManager.dll",StringComparison.OrdinalIgnoreCase))return ats;
        string manager=System.IO.Path.GetDirectoryName(ats),config=System.IO.Path.Combine(manager,"detailmodules.txt");if(!File.Exists(config))return ats;var lines=Read(config);var helpers=lines.Where(x=>x.Trim().Replace('/','\\').EndsWith(@"Plugin_Common\NoBveEx\NoBveEx.dll",StringComparison.OrdinalIgnoreCase)).ToArray();if(helpers.Length==0)return ats;
        // The helper checks the current process for BveEX; the x86 host is a separate process.
        // Keep all functional modules and remove only this presence check from a private copy.
        string common=System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(Full(manager,helpers[0])));string parent=System.IO.Path.GetDirectoryName(common);if(!manager.StartsWith(parent+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new NotSupportedException("DetailManagerの関連フォルダを安全に複製できません。");string relative=manager.Substring(parent.Length+1);string vehicleRoot=System.IO.Path.Combine(parent,relative.Split(System.IO.Path.DirectorySeparatorChar)[0]);string staging=System.IO.Path.Combine(run,"ats");CopyTree(vehicleRoot,System.IO.Path.Combine(staging,System.IO.Path.GetFileName(vehicleRoot)));CopyTree(common,System.IO.Path.Combine(staging,System.IO.Path.GetFileName(common)));string staged=System.IO.Path.Combine(staging,relative);Directory.CreateDirectory(staged);File.WriteAllLines(System.IO.Path.Combine(staged,"detailmodules.txt"),lines.Except(helpers),Encoding.GetEncoding(932));File.WriteAllText(System.IO.Path.Combine(run,"ATS作業コピーの変更.txt"),"別プロセスではBveEXの存在確認が成立しないため、作業コピーのdetailmodules.txtからNoBveEx.dllのみを除外しました。その他の機能モジュールと元データは保持します。実機動作は未確認です。",Encoding.UTF8);return System.IO.Path.Combine(staged,System.IO.Path.GetFileName(ats));
    }
    public static void CopyTree(string from,string to){if((File.GetAttributes(from)&FileAttributes.ReparsePoint)!=0)throw new IOException("リンク先の自動複製は行いません: "+from);Directory.CreateDirectory(to);foreach(var file in Directory.GetFiles(from))File.Copy(file,System.IO.Path.Combine(to,System.IO.Path.GetFileName(file)),true);foreach(var dir in Directory.GetDirectories(from))CopyTree(dir,System.IO.Path.Combine(to,System.IO.Path.GetFileName(dir)));}
    static string ConvertSound(string source,string run,List<string> warnings){var lines=Read(source);string dir=System.IO.Path.GetDirectoryName(source);var result=new List<string>(); foreach(var raw in lines){string line=raw; string section=line.Trim(); if(section.StartsWith("[") && section.EndsWith("]")){string name=section.Substring(1,section.Length-2); if(new[]{"Brake Handle","Master Controller","Pilot Lamp","Air Spring"}.Contains(name,StringComparer.OrdinalIgnoreCase))line="["+name.Replace(" ","")+"]";} if(line.StartsWith("BveTs Vehicle Sound",StringComparison.OrdinalIgnoreCase))line=line.Split(':')[0]+":shift_jis";var p=Pair(line); if(p.Key.Length>0 && p.Value.Length>0){string key=p.Key; if(new[]{"Open Left","Open Right","Close Left","Close Right"}.Contains(key,StringComparer.OrdinalIgnoreCase))key=key.Replace(" ",""); string asset=Full(dir,p.Value);if(!File.Exists(asset)){warnings.Add("音声 ["+key+"]: "+asset);result.Add("; 見つからない音声を作業コピーから省略: "+key);continue;}result.Add(key+" = "+asset);}else result.Add(line);}string path=System.IO.Path.Combine(run,"sound.txt");File.WriteAllLines(path,result,Encoding.GetEncoding(932));return path;}
    public static List<Entry> Scan(string root,bool vehicle){var list=new List<Entry>();var pending=new Stack<string>();pending.Push(root);while(pending.Count>0){string dir=pending.Pop();try{foreach(var sub in Directory.GetDirectories(dir)){if((File.GetAttributes(sub)&FileAttributes.ReparsePoint)==0)pending.Push(sub);}foreach(var f in Directory.GetFiles(dir,"*.txt")){try{using(var r=new StreamReader(f,Encoding.ASCII,true)){string h=r.ReadLine()??"";if(vehicle?VehicleHeader(h):ScenarioHeader(h))list.Add(new Entry{Path=f,Title=System.IO.Path.GetFileNameWithoutExtension(f)+"  —  "+f.Substring(root.Length).TrimStart('\\')});}}catch(IOException){}}}catch(UnauthorizedAccessException){}catch(IOException){}}return list.OrderBy(x=>x.Title).ToList();}
    public static ScanResult ScanBoth(string root,CancellationToken token,int scanWorkers=4){
        root=Path.GetFullPath(root);var scenarios=new System.Collections.Concurrent.ConcurrentBag<Entry>();var vehicles=new System.Collections.Concurrent.ConcurrentBag<Entry>();
        var seen=new System.Collections.Concurrent.ConcurrentBag<string>();var index=ScanIndex.Load(root);int reused=0,read=0;
        using(var queue=new System.Collections.Concurrent.BlockingCollection<FileInfo>(256)){
            var workers=Enumerable.Range(0,Math.Max(1,Math.Min(scanWorkers,4))).Select(_=>Task.Run(()=>{
                foreach(var file in queue.GetConsumingEnumerable(token)){
                    try{
                        int kind;if(index.TryGet(file,out kind))Interlocked.Increment(ref reused);else{
                            string header;using(var reader=new StreamReader(file.FullName,Encoding.ASCII,true))header=reader.ReadLine()??"";
                            kind=ScenarioHeader(header)?1:VehicleHeader(header)?2:0;index.Put(file,kind);Interlocked.Increment(ref read);
                        }
                        seen.Add(file.FullName);if(kind==0)continue;
                        var entry=new Entry{Path=file.FullName,Title=Path.GetFileNameWithoutExtension(file.Name)+"  —  "+file.FullName.Substring(root.Length).TrimStart('\\')};
                        if(kind==1)scenarios.Add(entry);else vehicles.Add(entry);
                    }catch(IOException){}catch(UnauthorizedAccessException){}
                }
            },token)).ToArray();
            try{
                var pending=new Stack<string>();pending.Push(root);
                while(pending.Count>0){token.ThrowIfCancellationRequested();string dir=pending.Pop();
                    try{foreach(FileSystemInfo item in new DirectoryInfo(dir).EnumerateFileSystemInfos()){
                        token.ThrowIfCancellationRequested();try{
                            if((item.Attributes&FileAttributes.ReparsePoint)!=0)continue;
                            if((item.Attributes&FileAttributes.Directory)!=0){pending.Push(item.FullName);continue;}
                            if(new[]{".txt",".cfg",".dat",".ini",".bve"}.Contains(item.Extension,StringComparer.OrdinalIgnoreCase))queue.Add((FileInfo)item,token);
                        }catch(IOException){}catch(UnauthorizedAccessException){}
                    }}catch(UnauthorizedAccessException){}catch(IOException){}
                }
            }finally{queue.CompleteAdding();try{Task.WaitAll(workers);}catch(AggregateException){token.ThrowIfCancellationRequested();throw;}}
        }
        token.ThrowIfCancellationRequested();
        var knownVehicles=new HashSet<string>(vehicles.Select(x=>x.Path),StringComparer.OrdinalIgnoreCase);
        foreach(var scenario in scenarios){token.ThrowIfCancellationRequested();try{string value=Value(Read(scenario.Path),"Vehicle");if(String.IsNullOrWhiteSpace(value))continue;foreach(var choice in ScenarioChoices.Parse(Path.GetDirectoryName(scenario.Path),value)){if(knownVehicles.Contains(choice.Path)||!File.Exists(choice.Path))continue;using(var reader=new StreamReader(choice.Path,Encoding.ASCII,true)){if(!VehicleHeader(reader.ReadLine()??""))continue;}vehicles.Add(new Entry{Path=choice.Path,Title=Path.GetFileNameWithoutExtension(choice.Path)});knownVehicles.Add(choice.Path);}}catch(Exception ex){if(ex is OperationCanceledException)throw;Diagnostics.Write("VEHICLE_REFERENCE",scenario.Path+" : "+ex.Message);}}
        index.Save(seen);Diagnostics.Write("SCAN_INDEX","reused="+reused+" read="+read);
        return new ScanResult{Scenarios=scenarios.OrderBy(x=>x.Title).ToList(),Vehicles=vehicles.OrderBy(x=>x.Title).ToList()};
    }
}

internal sealed class Launcher:ModernWindow {
 TextBox root=new TextBox(),search=new TextBox(),vehicleSearch=new TextBox(),exe=new TextBox(),exe5=new TextBox();CatalogView scenarios=new CatalogView(),vehicles=new CatalogView();DetailTextView scenarioInfo=new DetailTextView(),vehicleInfo=new DetailTextView();PictureBox scenarioPicture=new PictureBox(),vehiclePicture=new PictureBox();Label status=new Label(),scenarioCount=new Label(),vehicleCount=new Label();ProgressBar loadingBar=new ProgressBar();TableLayoutPanel launchLayout;Button[] launchButtons=new Button[3];List<CatalogItem> sc=new List<CatalogItem>(),vc=new List<CatalogItem>();int scanGeneration;CancellationTokenSource scanCancellation;bool filtering;int activeBveProcesses;bool gameQuietMode,pendingScanAfterGame;readonly BveGameplayActivity bveActivity=new BveGameplayActivity();
 public Launcher(bool startServices=true){Icon=new Icon(Path.Combine(Program.Home,"launcher.ico"));Text="BVE 起動ランチャー Ver."+Release.Display+"（b4開発版）";Size=new Size(1600,990);MinimumSize=new Size(960,680);Font=new Font("Yu Gothic UI",10);StartPosition=FormStartPosition.CenterScreen;
  BackColor=Color.FromArgb(245,248,252);var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(17,10,17,12),BackColor=Color.FromArgb(245,248,252)};Controls.Add(layout);foreach(int h in new[]{56,55,52})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));foreach(int h in new[]{140,132})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
  var header=new Panel{Dock=DockStyle.Fill,BackColor=Color.White,Margin=new Padding(0,0,0,7)};var appIcon=new PictureBox{Image=LoadHeaderLogo(),SizeMode=PictureBoxSizeMode.Zoom,Location=new Point(12,4),Size=new Size(34,34)};header.Controls.Add(appIcon);header.Controls.Add(new Label{Text="BVE 起動ランチャー",Font=new Font(Font.FontFamily,15,FontStyle.Bold),ForeColor=Color.FromArgb(27,48,77),Location=new Point(56,5),AutoSize=true});header.Controls.Add(new Label{Text="Ver."+Release.Display+" 開発版",ForeColor=Color.FromArgb(105,120,139),Location=new Point(265,10),AutoSize=true});Caption(header);layout.Controls.Add(header);
  var folder=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4};folder.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,130));folder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));folder.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));folder.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));folder.Controls.Add(new Label{Text="シナリオフォルダ",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);root.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"BveTs","Scenarios");root.Dock=DockStyle.Fill;folder.Controls.Add(root,1,0);var browse=new ModernButton{Text="フォルダを選ぶ",Symbol="folder",Dock=DockStyle.Fill};folder.Controls.Add(browse,2,0);var reload=new ModernButton{Text="更新",Symbol="refresh",Dock=DockStyle.Fill};folder.Controls.Add(reload,3,0);reload.Click+=(s,e)=>LoadData();layout.Controls.Add(folder);browse.Click+=(s,e)=>{using(var d=new FolderBrowserDialog()){d.SelectedPath=root.Text;if(d.ShowDialog()==DialogResult.OK){root.Text=d.SelectedPath;LoadData();}}};
  var searches=Columns();searches.Controls.Add(SearchBox("路線を検索",search));searches.Controls.Add(SearchBox("車両を検索",vehicleSearch));layout.Controls.Add(searches);search.TextChanged+=(s,e)=>Filter();vehicleSearch.TextChanged+=(s,e)=>Filter();
  var lists=Columns();scenarios.Dock=vehicles.Dock=DockStyle.Fill;vehicles.ShowVehicleLocation=true;scenarios.AccessibleName="路線・シナリオ一覧";vehicles.AccessibleName="車両・編成一覧";lists.Controls.Add(Group("路線・シナリオ一覧",scenarios));lists.Controls.Add(Group("車両・編成一覧（パネル画像）",vehicles));layout.Controls.Add(lists);
  var details=Columns();foreach(var box in new[]{scenarioInfo,vehicleInfo}){box.Dock=DockStyle.Fill;box.BackColor=Color.White;}details.Controls.Add(Group("選択したシナリオ",DetailPanel(scenarioPicture,scenarioInfo)));details.Controls.Add(Group("使用する車両",DetailPanel(vehiclePicture,vehicleInfo)));layout.Controls.Add(details);
  scenarios.SelectionChanged+=(s,e)=>{if(!filtering)vehicles.ClearSelection();RefreshSelection();};vehicles.SelectionChanged+=(s,e)=>{if(!filtering)RefreshSelection();};
  bveActivity.Changed+=(running)=>SetGameQuietMode(running);exe.Text=BveLocator.Resolve(6)??"";exe5.Text=BveLocator.Resolve(5)??"";var statusPanel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,BackColor=Color.White,Margin=new Padding(4,3,2,3),Padding=new Padding(10,4,10,4)};statusPanel.RowStyles.Add(new RowStyle(SizeType.Percent,100));statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,250));status.Dock=DockStyle.Fill;status.Text="一覧を読み込みます。";status.TextAlign=ContentAlignment.MiddleLeft;statusPanel.Controls.Add(status,0,0);loadingBar.Dock=DockStyle.Fill;loadingBar.Style=ProgressBarStyle.Marquee;loadingBar.MarqueeAnimationSpeed=25;loadingBar.AccessibleName="読み込み進捗";loadingBar.Visible=false;statusPanel.Controls.Add(loadingBar,1,0);
  var bottom=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Margin=new Padding(0)};bottom.RowStyles.Add(new RowStyle(SizeType.Absolute,59));bottom.RowStyles.Add(new RowStyle(SizeType.Percent,100));bottom.RowStyles.Add(new RowStyle(SizeType.Absolute,44));var modes=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,Margin=new Padding(0,3,0,3)};launchLayout=modes;for(int i=0;i<3;i++)modes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));string[] names={"BVE6 互換起動","BVE6 通常起動","BVE5 通常起動"};for(int i=0;i<3;i++){int selectedMode=i;var button=new ModernButton{Text=names[i],Symbol="play",Emphasized=false,Dock=DockStyle.Fill,Font=new Font(Font,FontStyle.Bold),BackColor=i==0?Color.FromArgb(18,119,103):Color.FromArgb(31,102,181),ForeColor=Color.White,Margin=new Padding(4),Cursor=Cursors.Hand};button.FlatAppearance.BorderSize=0;button.Click+=(s,e)=>Start(selectedMode);launchButtons[i]=button;modes.Controls.Add(button,i,0);}bottom.Controls.Add(modes,0,0);var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=5,RowCount=1,Margin=new Padding(0)};actions.RowStyles.Add(new RowStyle(SizeType.Percent,100));foreach(int w in new[]{175,246,210,120})actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,w));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));var clear=new ModernButton{Text="車両選択を解除",Symbol="document",Dock=DockStyle.Fill};clear.Click+=(s,e)=>{vehicles.ClearSelection();RefreshSelection();};actions.Controls.Add(clear);var register=new ModernButton{Text="MotorMasterファイルを選ぶ",Symbol="image",Dock=DockStyle.Fill};register.Click+=(s,e)=>RegisterExtra();actions.Controls.Add(register);var motorState=new Label{AutoSize=true,Padding=new Padding(8,7,8,0)};actions.Controls.Add(motorState);var settings=new ModernButton{Text="設定",Symbol="dots",Dock=DockStyle.Fill};settings.Click+=(s,e)=>ShowSettings();actions.Controls.Add(settings);statusPanel.ColumnStyles[1].Width=70;actions.Controls.Add(statusPanel,4,0);bottom.Controls.Add(actions,0,2);layout.Controls.Add(bottom);if(startServices)Shown+=(s,e)=>{bveActivity.Start();LoadData();};FormClosed+=(s,e)=>{bveActivity.Dispose();if(scanCancellation!=null)scanCancellation.Cancel();if(scenarioPicture.Image!=null)scenarioPicture.Image.Dispose();if(vehiclePicture.Image!=null)vehiclePicture.Image.Dispose();if(appIcon.Image!=null)appIcon.Image.Dispose();};RefreshSelection();Action refreshMotor=()=>{motorState.Text=MotorMasterFix.IsRegistered()?"MotorMaster: 読み込み済み ✓":"MotorMaster: 未登録";motorState.ForeColor=MotorMasterFix.IsRegistered()?Color.FromArgb(18,115,79):Color.FromArgb(140,82,38);};refreshMotor();register.Click+=(s,e)=>refreshMotor();
 }
 static Image LoadHeaderLogo(){using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("launcher-icon.png")){if(stream==null)return null;using(var image=Image.FromStream(stream))return new Bitmap(image);}}
 TableLayoutPanel Columns(){var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0)};panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));return panel;}
 Control Group(string title,Control child){var card=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(224,231,242),Padding=new Padding(1),Margin=new Padding(3)};child.Dock=DockStyle.Fill;card.Controls.Add(child);var heading=new TableLayoutPanel{Dock=DockStyle.Top,Height=34,ColumnCount=2,RowCount=1,BackColor=Color.FromArgb(238,244,251),Margin=new Padding(0)};heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,70));heading.RowStyles.Add(new RowStyle(SizeType.Percent,100));var logo=UiDrawing.Train();var label=new Label{Text="      "+title,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Image=logo,ImageAlign=ContentAlignment.MiddleLeft,ForeColor=Color.FromArgb(38,62,94),Font=new Font(Font,FontStyle.Bold)};heading.Controls.Add(label,0,0);Label count=child==scenarios?scenarioCount:child==vehicles?vehicleCount:null;if(count!=null){count.Dock=DockStyle.Fill;count.TextAlign=ContentAlignment.MiddleRight;count.ForeColor=Color.SlateGray;heading.Controls.Add(count,1,0);}card.Controls.Add(heading);card.Disposed+=(s,e)=>logo.Dispose();return card;}
 Control DetailPanel(PictureBox picture,DetailTextView description){var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Padding=new Padding(12,8,12,8),BackColor=Color.White};panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));picture.Dock=DockStyle.Fill;picture.SizeMode=PictureBoxSizeMode.Zoom;picture.BackColor=Color.FromArgb(240,244,248);panel.Controls.Add(picture,0,0);panel.Controls.Add(description,1,0);return panel;}
 async void SetPreview(PictureBox picture,string path){
  if(String.Equals(picture.Tag as string,path,StringComparison.OrdinalIgnoreCase))return;picture.Tag=path;var old=picture.Image;picture.Image=null;if(old!=null)old.Dispose();if(String.IsNullOrWhiteSpace(path))return;if(gameQuietMode){picture.Tag=null;return;}
  Image preview=null;try{preview=await Task.Run(()=>{using(var source=Previews.Load(path)){if(source==null)return null;var result=new Bitmap(120,72);using(var g=Graphics.FromImage(result)){g.Clear(Color.White);float scale=Math.Min(120f/source.Width,72f/source.Height);int w=Math.Max(1,(int)(source.Width*scale)),h=Math.Max(1,(int)(source.Height*scale));g.DrawImage(source,new Rectangle((120-w)/2,(72-h)/2,w,h));}return (Image)result;}});}catch(Exception ex){Diagnostics.Write("PREVIEW_ERROR",ex.Message);}
  if(IsDisposed||picture.IsDisposed||!String.Equals(picture.Tag as string,path,StringComparison.OrdinalIgnoreCase)){if(preview!=null)preview.Dispose();return;}var previous=picture.Image;picture.Image=preview;if(previous!=null)previous.Dispose();
 }
 Control ExecutablePicker(string title,TextBox path,int version,IWin32Window owner){var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=new Padding(2)};panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,130));panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,48));panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));panel.Controls.Add(new Label{Text=title,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);path.Dock=DockStyle.Fill;panel.Controls.Add(path,1,0);var choose=new ModernButton{Text="",Symbol="dots",AccessibleName=title+"の場所を選ぶ",Dock=DockStyle.Fill};choose.Click+=(s,e)=>{using(var picker=new OpenFileDialog{Filter="BVE本体|BveTs.exe"})if(picker.ShowDialog(owner)==DialogResult.OK){if(!BveLocator.IsManualValid(picker.FileName,version)){MessageBox.Show(owner,"BVE"+version+"のBveTs.exeを選んでください。");return;}try{BveLocator.Remember(version,picker.FileName);path.Text=picker.FileName;}catch(Exception ex){MessageBox.Show(owner,"設定を保存できませんでした: "+ex.Message);}}};panel.Controls.Add(choose,2,0);return panel;}
 void RequestFirstCleanup(){
  try{string root=InstallEngine.DetectRoot(Program.Home);if(root==null||!InstallEngine.HasOlder(root))return;string marker=Path.Combine(Program.Data,"cleanup-request-"+Release.Display+".txt");if(File.Exists(marker))return;Directory.CreateDirectory(Program.Data);File.WriteAllText(marker,"requested",Encoding.UTF8);
   string helper=Path.Combine(Program.Home,"導入・更新.exe");if(!File.Exists(helper))throw new FileNotFoundException("旧版整理用のファイルが見つかりません。");var process=Process.Start(new ProcessStartInfo(helper,"--cleanup-root \""+root+"\""){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden});if(process==null)throw new IOException("旧版の整理を開始できませんでした。");process.Dispose();
  }catch(Exception ex){Diagnostics.Write("CLEANUP_START",ex.Message);}
 }
 void ShowSettings(){using(var dialog=CreateSettings()){dialog.ShowDialog(this);}}
 internal Form CreateSettings(){
  var dialog=new Form{Text="設定 | BVE 起動ランチャー",Size=new Size(850,570),MinimumSize=new Size(620,500),StartPosition=FormStartPosition.CenterParent,Font=new Font("Yu Gothic UI",10),BackColor=Color.FromArgb(245,248,252)};
  var table=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Padding=new Padding(22,18,22,16)};dialog.Controls.Add(table);
  table.RowStyles.Add(new RowStyle(SizeType.Absolute,45));for(int i=0;i<4;i++)table.RowStyles.Add(new RowStyle(SizeType.Absolute,59));table.RowStyles.Add(new RowStyle(SizeType.Percent,100));table.RowStyles.Add(new RowStyle(SizeType.Absolute,54));
  table.Controls.Add(new Label{Text="BVEとBveEXの場所",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(27,48,77),Font=new Font("Yu Gothic UI",13,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft},0,0);
  var p6=new TextBox{Text=exe.Text,ReadOnly=true};var p5=new TextBox{Text=exe5.Text,ReadOnly=true};var x6=new TextBox{ReadOnly=true};var x5=new TextBox{ReadOnly=true};
  Action refresh=()=>{x6.Text=BveExLocation.Resolve(p6.Text)??"未確認";x5.Text=BveExLocation.Resolve(p5.Text)??"未確認";};
  p6.TextChanged+=(s,e)=>{exe.Text=p6.Text;refresh();};p5.TextChanged+=(s,e)=>{exe5.Text=p5.Text;refresh();};
  table.Controls.Add(SettingsRow(ExecutablePicker("BVE6本体",p6,6,dialog)),0,1);table.Controls.Add(SettingsRow(BveExPicker("BveEX (BVE6)",p6,x6,dialog)),0,2);
  table.Controls.Add(SettingsRow(ExecutablePicker("BVE5本体",p5,5,dialog)),0,3);table.Controls.Add(SettingsRow(BveExPicker("BveEX (BVE5)",p5,x5,dialog)),0,4);refresh();
  table.Controls.Add(new Label{Text="BveEXの場所を選ぶと、そのBVE側の設定ファイルを変更します。管理者権限の確認が表示されます。\nBveEX本体の導入や有効化は、配布元の手順に従ってください。",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(100,115,135),TextAlign=ContentAlignment.MiddleLeft},0,5);
  var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.LeftToRight,WrapContents=false};table.Controls.Add(buttons,0,6);
  var logs=new Button{Text="ログを表示",AutoSize=true,Height=36};logs.Click+=(s,e)=>Diagnostics.Show(dialog);buttons.Controls.Add(logs);
  var terms=new Button{Text="利用規約",AutoSize=true,Height=36};terms.Click+=(s,e)=>ShowTerms(dialog);buttons.Controls.Add(terms);
  var close=new Button{Text="閉じる",AutoSize=true,Height=36,DialogResult=DialogResult.OK};buttons.Controls.Add(close);dialog.AcceptButton=close;return dialog;
 }
 Control SettingsRow(Control content){
  var panel=new Panel{Dock=DockStyle.Fill,Margin=new Padding(0,3,0,3),Padding=new Padding(10,6,10,6),BackColor=Color.White};
  content.Dock=DockStyle.Fill;panel.Controls.Add(content);return panel;
 }
 Control BveExPicker(string title,TextBox bve,TextBox folder,Form owner){
  var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=new Padding(2)};row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,130));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,80));
  row.Controls.Add(new Label{Text=title,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);folder.Dock=DockStyle.Fill;row.Controls.Add(folder,1,0);var choose=new ModernButton{Text="選ぶ",Dock=DockStyle.Fill};row.Controls.Add(choose,2,0);
  choose.Click+=(s,e)=>{try{if(!BveLocator.IsManualValid(bve.Text,5)&&!BveLocator.IsManualValid(bve.Text,6))throw new InvalidDataException("先にBVE本体を選んでください。");using(var picker=new FolderBrowserDialog{Description="BveEx.Launcher.dllのあるフォルダーを選んでください",ShowNewFolderButton=false}){if(Directory.Exists(folder.Text))picker.SelectedPath=folder.Text;if(picker.ShowDialog(owner)!=DialogResult.OK)return;string selected=picker.SelectedPath;if(!File.Exists(Path.Combine(selected,"BveEx.Launcher.dll")))throw new InvalidDataException("BveEx.Launcher.dllのあるフォルダーを選んでください。");string helper=Path.Combine(Program.Home,"導入・更新.exe");if(!File.Exists(helper))throw new FileNotFoundException("設定用プログラムが見つかりません。");using(var proc=Process.Start(new ProcessStartInfo(helper,"--set-bveex \""+bve.Text+"\" \""+selected+"\""){UseShellExecute=true,Verb="runas"})){if(proc==null)throw new IOException("設定を開始できませんでした。");proc.WaitForExit();if(proc.ExitCode!=0)return;}folder.Text=BveExLocation.Resolve(bve.Text)??"未確認";status.Text="BveEXの場所を設定しました。次のBVE起動時に使用します。";}}
   catch(Exception ex){MessageBox.Show(owner,"BveEXの設定を完了できませんでした。\n"+ex.Message,"BveEXの設定",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};return row;
 }
 Control SearchBox(string label,TextBox box){var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(3,1,3,1)};panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,112));panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));panel.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=new Font(Font,FontStyle.Bold)},0,0);panel.Controls.Add(new SearchField(box,label){Dock=DockStyle.Fill},1,0);return panel;}
 Program.Entry Selected(CatalogView view){return view.SelectedItem==null?null:view.SelectedItem.Entry;}
 void RefreshSelection(){vehicleInfo.BackColor=Color.White;var scenario=scenarios.SelectedItem;var vehicle=vehicles.SelectedItem;scenarioInfo.Text=scenario==null?"左の一覧からシナリオを選んでください。":scenario.Title+Environment.NewLine+DisplayVersion(scenario)+(String.IsNullOrWhiteSpace(scenario.DisplayGroup)?"":"  ·  "+scenario.DisplayGroup)+Environment.NewLine+Path.GetFileName(scenario.Entry.Path);string defaultVehicle=null;
  if(vehicle!=null)vehicleInfo.Text=vehicle.Title+Environment.NewLine+DisplayVersion(vehicle)+"  ·  "+Path.GetFileName(vehicle.Entry.Path)+Environment.NewLine+"保存場所: "+Path.GetDirectoryName(vehicle.Entry.Path);
  else if(scenario!=null){try{string raw=Program.Value(Program.Read(scenario.Entry.Path),"Vehicle");bool random=ScenarioChoices.IsRandom(raw);defaultVehicle=random?null:Program.DefaultVehicle(scenario.Entry.Path);vehicleInfo.Text=random?"車両ランダム（シナリオ内で指定された車両）":defaultVehicle==null?"この路線の指定車両は見つかりません。右の一覧から使用する車両を選んでください。":"シナリオ指定"+Environment.NewLine+Path.GetFileName(defaultVehicle)+Environment.NewLine+"保存場所: "+Path.GetDirectoryName(defaultVehicle);vehicleInfo.BackColor=defaultVehicle==null&&!random?Color.MistyRose:Color.White;}catch(Exception ex){vehicleInfo.Text="車両の確認に失敗しました："+ex.Message;}}
  else vehicleInfo.Text="車両を選ばなければ、シナリオに指定された車両を使います。";vehicles.SetDefault(defaultVehicle);
  UpdateLaunchOrder();SetPreview(scenarioPicture,scenario==null?null:scenario.ImagePath);var shownVehicle=vehicle??(defaultVehicle==null?null:vc.FirstOrDefault(x=>String.Equals(x.Entry.Path,defaultVehicle,StringComparison.OrdinalIgnoreCase)));SetPreview(vehiclePicture,shownVehicle==null?null:shownVehicle.ImagePath);
 }
 static string DisplayVersion(CatalogItem item){
  string v=DataVersionHint.Display(item.DataHint);
  return v=="BVE5"||v=="BVE6"||v=="BVE5 / BVE6"?v:"未判定";
 }
 // Button identity and Start(mode) handlers never change; only priority and presentation.
 void UpdateLaunchOrder(){
  if(launchLayout==null||launchButtons.Any(x=>x==null))return;
  string version=scenarios.SelectedItem==null?"未判定":DisplayVersion(scenarios.SelectedItem);
  int primary=version=="BVE5"?2:version=="BVE6"?1:-1;
  int[] positions=primary==2?new[]{2,0,1}:primary==1?new[]{1,0,2}:new[]{0,1,2};
  launchLayout.SuspendLayout();
  try{
   // Remove and re-add without disposing: delegates still point at the original mode IDs.
   foreach(var button in launchButtons)launchLayout.Controls.Remove(button);
   launchLayout.ColumnStyles.Clear();
   for(int i=0;i<3;i++)launchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,primary<0?100f/3:(i==0?48f:26f)));
   for(int i=0;i<positions.Length;i++){
    int mode=positions[i];var button=(ModernButton)launchButtons[mode];
    button.Emphasized=mode==primary;
    button.BackColor=mode==2?Color.FromArgb(35,98,179):Color.FromArgb(17,122,111);
    button.AccessibleDescription=mode==primary?"選択中のシナリオの優先起動方法":"別の起動方法";
    launchLayout.Controls.Add(button,i,0);button.Invalidate();
   }
  }finally{launchLayout.ResumeLayout(true);}
 }
 // Passive quiet mode: do not compete with BVE for disk/CPU while the train runs.
 // Do not change the BVE process, OS priority, frame limit or driver configuration.
 void SetGameQuietMode(bool gameRunning){
  bool pause=gameRunning || Interlocked.CompareExchange(ref activeBveProcesses,0,0)>0;
  scenarios.SetThumbnailLoadingPaused(pause);
  vehicles.SetThumbnailLoadingPaused(pause);
  if(gameQuietMode==pause)return;
  gameQuietMode=pause;
  Diagnostics.Write("GAME_QUIET_MODE",pause?"active":"inactive");
  if(pause){
   if(scanCancellation!=null){
    pendingScanAfterGame=true;
    scanGeneration++;
    scanCancellation.Cancel();
    Idle();
    status.Text="BVE運転中のため一覧更新を中断しました。終了後に自動で再開します。";
   }
  }else{
   if(IsDisposed)return;
   // Revisit previews deferred during play, once the game has exited.
   RefreshSelection();
   if(pendingScanAfterGame){pendingScanAfterGame=false;LoadData();}
  }
 }
 void SetLaunchEnabled(bool enabled){foreach(var button in launchButtons)if(button!=null)button.Enabled=enabled;}
 sealed class ScanProgress {public int Done,Total;public string Phase;}
 void Busy(string message){status.Text=message;loadingBar.Style=ProgressBarStyle.Marquee;loadingBar.MarqueeAnimationSpeed=25;loadingBar.Visible=true;}
 void Idle(){loadingBar.Visible=false;loadingBar.MarqueeAnimationSpeed=0;}
 async void LoadData(){
  if(gameQuietMode || bveActivity.IsRunning || Interlocked.CompareExchange(ref activeBveProcesses,0,0)>0){
   pendingScanAfterGame=true;
   status.Text="BVE運転中のため一覧更新を保留中です。BVE終了後に自動で実行します。";
   return;
  }
  int generation=++scanGeneration;if(scanCancellation!=null)scanCancellation.Cancel();var cancellation=new CancellationTokenSource();scanCancellation=cancellation;var token=cancellation.Token;
  string path=root.Text;List<CatalogItem> cachedSc,cachedVc;bool cacheUpgrade;bool hasCache=CatalogCache.TryRead(path,out cachedSc,out cachedVc,out cacheUpgrade);
  if(hasCache){sc=cachedSc;vc=cachedVc;Filter();SetLaunchEnabled(true);Busy("保存済みの一覧を表示しました。最新のファイルを確認しています…");}
  else{scenarios.EmptyText=vehicles.EmptyText="読み込み中です…";SetLaunchEnabled(false);Busy("シナリオと車両のファイルを探しています…");}
  Diagnostics.Write("SCAN",path+(hasCache?" cached":""));
  IProgress<ScanProgress> progress=new Progress<ScanProgress>(p=>{if(generation!=scanGeneration||IsDisposed||!loadingBar.Visible)return;if(p.Total>0){loadingBar.Style=ProgressBarStyle.Continuous;loadingBar.Minimum=0;loadingBar.Maximum=p.Total;loadingBar.Value=Math.Min(p.Total,p.Done);status.Text=String.Format("{0} {1}/{2}件",p.Phase,p.Done,p.Total);}else{loadingBar.Style=ProgressBarStyle.Marquee;status.Text=p.Phase;}});
  try{
   if(hasCache&&cacheUpgrade){try{status.Text="保存済みの一覧を表示しました。対応版の記載を確認しています…";var improved=await Task.Run(()=>new[]{CatalogData.RefreshCached(cachedSc,path,false,token),CatalogData.RefreshCached(cachedVc,path,true,token)},token);if(generation!=scanGeneration||IsDisposed)return;sc=improved[0];vc=improved[1];Filter();}catch(OperationCanceledException){throw;}catch(Exception ex){Diagnostics.Error("HINT_UPGRADE_ERROR",ex);}}
   bool gameplayOnRefresh=bveActivity.IsRunning || Interlocked.CompareExchange(ref activeBveProcesses,0,0)>0;
   if(gameplayOnRefresh)status.Text="BVE運転中のため、一覧を低負荷で読み込んでいます…";
   var work=Task.Run(()=>{
    if(!Directory.Exists(path))throw new DirectoryNotFoundException("シナリオフォルダが見つかりません。フォルダを選び直してください。");
    var watch=Stopwatch.StartNew();var files=Program.ScanBoth(path,token,gameplayOnRefresh?1:4);
    Diagnostics.Write("SCAN_FILES","routes="+files.Scenarios.Count+" vehicles="+files.Vehicles.Count+" elapsedMs="+watch.ElapsedMilliseconds);
    int total=files.Scenarios.Count+files.Vehicles.Count,done=0;
    progress.Report(new ScanProgress{Phase="一覧の情報を読み込み中",Done=0,Total=total});
    Action<int> advance=_=>{token.ThrowIfCancellationRequested();int value=Interlocked.Increment(ref done);if(value==total||value%8==0)progress.Report(new ScanProgress{Phase="一覧の情報を読み込み中",Done=value,Total=total});};
    List<CatalogItem>[] result;
    if(gameplayOnRefresh){
     result=new[]{CatalogData.Build(files.Scenarios,path,false,advance,token,1),CatalogData.Build(files.Vehicles,path,true,advance,token,1)};
    }else{
     var routeTask=Task.Run(()=>CatalogData.Build(files.Scenarios,path,false,advance,token),token);
     var vehicleTask=Task.Run(()=>CatalogData.Build(files.Vehicles,path,true,advance,token),token);
     Task.WaitAll(routeTask,vehicleTask);
     result=new[]{routeTask.Result,vehicleTask.Result};
    }
    Diagnostics.Write("SCAN_METADATA","elapsedMs="+watch.ElapsedMilliseconds);return result;
   },token);
   if(await Task.WhenAny(work,Task.Delay(gameplayOnRefresh?300000:120000))!=work){cancellation.Cancel();throw new TimeoutException("読み込みが2分を超えたため中止しました。フォルダの場所を確認し、『更新』でもう一度試してください。詳しい記録は『ログ・バグ報告』で見られます。");}
   var found=await work;if(generation!=scanGeneration||IsDisposed)return;sc=found[0];vc=found[1];Diagnostics.Write("SCAN_DONE","scenarios="+sc.Count+" vehicles="+vc.Count);Filter();status.Text=String.Format("読み込み完了：シナリオ {0}件／車両 {1}件。画像は表示順に読み込みます。",sc.Count,vc.Count);await Task.Run(()=>CatalogCache.Write(path,found[0],found[1]));
  }catch(OperationCanceledException){if(generation==scanGeneration&&!IsDisposed)status.Text="読み込みを中止しました。";}
  catch(Exception e){if(generation!=scanGeneration||IsDisposed)return;Diagnostics.Error("SCAN_ERROR",e);status.Text=e.Message;}
  finally{if(generation==scanGeneration&&!IsDisposed){scenarios.EmptyText=vehicles.EmptyText="該当する項目がありません";Idle();SetLaunchEnabled(true);}if(Object.ReferenceEquals(scanCancellation,cancellation))scanCancellation=null;cancellation.Dispose();}
 } void Filter(){filtering=true;try{scenarios.SetItems(sc.Where(x=>x.SearchText.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0));vehicles.SetItems(vc.Where(x=>x.SearchText.IndexOf(vehicleSearch.Text,StringComparison.OrdinalIgnoreCase)>=0));scenarioCount.Text=scenarios.ItemCount+" 件  ";vehicleCount.Text=vehicles.ItemCount+" 件  ";}finally{filtering=false;}RefreshSelection();}
 void ShowTerms(IWin32Window owner){
  try{string text;using(var stream=typeof(Launcher).Assembly.GetManifestResourceStream("terms.txt")){if(stream==null)throw new InvalidDataException("利用規約が見つかりません。");using(var reader=new StreamReader(stream,Encoding.UTF8,true))text=reader.ReadToEnd();}
   using(var dialog=new Form{Text="利用規約",Size=new Size(780,700),MinimumSize=new Size(500,400),StartPosition=FormStartPosition.CenterParent,Font=new Font("Yu Gothic UI",10)}){var close=new Button{Text="閉じる",Dock=DockStyle.Bottom,Height=42,DialogResult=DialogResult.OK};var box=new TextBox{Text=TermsPresentation.Format(text),Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,WordWrap=true,Dock=DockStyle.Fill};dialog.Controls.Add(box);dialog.Controls.Add(close);dialog.AcceptButton=close;dialog.ShowDialog(owner);}
  }catch(Exception ex){Diagnostics.Error("TERMS_ERROR",ex);MessageBox.Show(owner,"利用規約を表示できませんでした。\n"+ex.Message,"利用規約");}
 }
 void RegisterExtra(){
  var answer=MessageBox.Show(this,"MotorMaster.dllは同梱していません。作者の配布ページを開き、利用条件を確認してから入手してください。\n\n配布ページを開きますか？","MotorMasterの登録",MessageBoxButtons.YesNo,MessageBoxIcon.Information);
  if(answer==DialogResult.Yes)Process.Start(new ProcessStartInfo(MotorMasterFix.AuthorPage){UseShellExecute=true});
  using(var picker=new OpenFileDialog{Title="作者から入手したMotorMaster.dllを選択",Filter="DLLファイル (*.dll)|*.dll",FileName="MotorMaster.dll"}){
   if(picker.ShowDialog(this)!=DialogResult.OK)return;
   try{MotorMasterFix.Register(picker.FileName);status.Text="MotorMasterを確認して登録しました。";MessageBox.Show(this,"MotorMasterを登録しました。もう一度、起動を試してください。","登録完了");}
   catch(Exception ex){Diagnostics.Error("COMPAT_REGISTER_ERROR",ex);MessageBox.Show(this,ex.Message,"登録できませんでした");}
  }
 }
    internal static bool NeedsLegacy(string scenario){
      try{string vehicle=Program.DefaultVehicle(scenario);if(vehicle==null)return false;string dir=Path.GetDirectoryName(vehicle);foreach(string file in new[]{Path.Combine(dir,"VehiclePluginUsing.xml"),Path.ChangeExtension(vehicle,".VehiclePluginUsing.xml")}){if(File.Exists(file)&&XDocument.Load(file).Root.Name.LocalName=="AtsExPluginUsing")return true;}}catch(Exception ex){Diagnostics.Write("LEGACY_CHECK",ex.Message);}return false;
    }
    async void Start(int mode){if(scanCancellation!=null){scanCancellation.Cancel();scanGeneration++;}SetLaunchEnabled(false);try{var s=Selected(scenarios);var v=Selected(vehicles);if(s==null)throw new InvalidOperationException("左側の一覧からシナリオを選んでください。");bool bve5=mode==2; bool compat=mode==0; string executable=bve5?exe5.Text:exe.Text; if(!File.Exists(executable)||Program.Machine(executable)!=(bve5?0x14c:0x8664))throw new InvalidOperationException((bve5?"BVE5":"BVE6")+"の本体を指定してください。");if(Process.GetProcessesByName("BveTs").Any(p=>{try{return p.MainModule.FileName.Equals(executable,StringComparison.OrdinalIgnoreCase);}catch{return true;}}))throw new InvalidOperationException("起動中のBVEを終了してから、もう一度押してください。");Busy("起動用データを準備しています。必要なら作業コピーを作ります…");Diagnostics.Write("PREPARE","scenario="+s.Path+" vehicle="+(v==null?"(scenario default)":v.Path));var ready=await Task.Run(()=>(compat?Program.Prepare(s.Path,v==null?null:v.Path):Program.PrepareNormal(s.Path,v==null?null:v.Path,bve5)));if(ready.Warnings.Count>0){string warning=String.Join(Environment.NewLine,ready.Warnings.ToArray());File.WriteAllText(Path.Combine(Path.GetDirectoryName(ready.Scenario),"不足ファイル.txt"),warning,Encoding.UTF8);Diagnostics.Write("MISSING_OPTIONAL_FILES",warning);if(MessageBox.Show(this,"次のファイルが見つかりません。該当する音声・画像なしで起動しますか？\n元のデータは変更していません。\n\n"+String.Join("\n",ready.Warnings.Take(12).ToArray())+(ready.Warnings.Count>12?"\nほか "+(ready.Warnings.Count-12)+" 件。詳細は最新ログを確認してください。":""),"不足ファイルの確認",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;}Diagnostics.CurrentRun=Path.GetDirectoryName(ready.Scenario);Diagnostics.Write("PREPARED","scenario="+ready.Scenario+" ATS="+ready.Ats+" mode="+ready.Mode+" bridgeLog="+ready.Log);var start=new ProcessStartInfo(executable,"\""+ready.Scenario+"\""+(NeedsLegacy(ready.Scenario)?" /legacy":"")){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(executable)};start.EnvironmentVariables.Remove("BVE5COMPAT_ATS");start.EnvironmentVariables.Remove("BVE5COMPAT_LOG");start.EnvironmentVariables.Remove("BVE5COMPAT_WORKDIR");if(ready.Mode.StartsWith("32bit")){start.EnvironmentVariables["BVE5COMPAT_ATS"]=ready.Ats;start.EnvironmentVariables["BVE5COMPAT_LOG"]=ready.Log;start.EnvironmentVariables["BVE5COMPAT_WORKDIR"]=ready.Work;}var bveProcess=Process.Start(start);if(bveProcess==null)throw new IOException("BVEプロセスを開始できませんでした。");if(Interlocked.Increment(ref activeBveProcesses)==1)SetGameQuietMode(true);Diagnostics.Write("BVE_STARTED","pid="+bveProcess.Id+" exe="+start.FileName);bveProcess.Exited+=(sender,args)=>{try{Diagnostics.Write("BVE_EXIT","pid="+bveProcess.Id+" exitCode="+bveProcess.ExitCode+" bridgeLogExists="+File.Exists(ready.Log));}catch(Exception ex){Diagnostics.Error("EXIT_LOG_ERROR",ex);}finally{bveProcess.Dispose();if(Interlocked.Decrement(ref activeBveProcesses)==0){try{if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)(()=>{if(IsDisposed)return;bveActivity.Check();SetGameQuietMode(bveActivity.IsRunning);}));}catch(InvalidOperationException){}}}};bveProcess.EnableRaisingEvents=true;status.Text=(bve5?"BVE5":"BVE6")+"を起動しました。"+ready.Mode+"。実際の動作は未確認です。";}catch(Exception e){Diagnostics.Error("LAUNCH_ERROR",e);status.Text=e.Message;MessageBox.Show(this,e.Message,"起動準備を確認してください");}finally{Idle();SetLaunchEnabled(true);}}
}
