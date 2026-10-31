using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

internal static class Diagnostics {
    public const string Version=Release.Display;
    static readonly object Gate=new object();
    static string session;
    public static string CurrentRun;
    public static void Initialize(){Directory.CreateDirectory(Path.Combine(Program.Data,"logs"));session=Path.Combine(Program.Data,"logs","launcher-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".log");Write("START","version="+Version+" OS="+Environment.OSVersion+" CLR="+Environment.Version+" process64="+Environment.Is64BitProcess);}
    public static void Write(string kind,string message){try{if(session==null)return;lock(Gate)File.AppendAllText(session,DateTimeOffset.Now.ToString("o")+" ["+kind+"] "+message+Environment.NewLine,Encoding.UTF8);}catch(IOException){}catch(UnauthorizedAccessException){}}
    public static void Error(string kind,Exception error){Write(kind,error.ToString());}
    public static string Redact(string text){return Regex.Replace(text,@"(?i)[a-z]:\\Users\\[^\\\r\n\""<>]+",@"<USERPROFILE>");}
    public static string Tail(string file){using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){bool truncated=stream.Length>262144;if(truncated){stream.Seek(-262144,SeekOrigin.End);}using(var reader=new StreamReader(stream,Encoding.UTF8,true)){if(truncated)reader.ReadLine();return (truncated?"[大きいログのため末尾256KBを表示]"+Environment.NewLine:"")+reader.ReadToEnd();}}}
    public static IEnumerable<string> Files(){var files=new List<string>();string logdir=Path.Combine(Program.Data,"logs");if(Directory.Exists(logdir))files.AddRange(Directory.GetFiles(logdir,"launcher-*.log"));if(Directory.Exists(Program.Data))foreach(var run in Directory.GetDirectories(Program.Data,"run-*")){foreach(var name in new[]{"bridge.log","準備記録.txt","ATS作業コピーの変更.txt","MotorMaster対策.txt"}){string path=Path.Combine(run,name);if(File.Exists(path))files.Add(path);}}string error=Path.Combine(Program.Data,"error.txt");if(File.Exists(error))files.Add(error);return files.OrderByDescending(File.GetLastWriteTimeUtc).ToArray();}
    public static string Report(string selected){var b=new StringBuilder();b.AppendLine("BVE6互換ランチャー 診断レポート");b.AppendLine("バージョン: "+Version);b.AppendLine("保存日時: "+DateTimeOffset.Now.ToString("o"));b.AppendLine("症状・再現手順: （ここに追記してください）");b.AppendLine("各ログは最大256KB。ユーザーフォルダ名は伏せ字化。ファイル名や路線名は含まれることがあります。");var files=new List<string>();if(session!=null && File.Exists(session))files.Add(session);if(selected!=null && File.Exists(selected)){files.Add(selected);string dir=Path.GetDirectoryName(selected);if(Path.GetFileName(dir).StartsWith("run-"))foreach(var name in new[]{"bridge.log","準備記録.txt","ATS作業コピーの変更.txt","MotorMaster対策.txt"}){string path=Path.Combine(dir,name);if(File.Exists(path))files.Add(path);}}
        foreach(var file in files.Distinct(StringComparer.OrdinalIgnoreCase)){b.AppendLine();b.AppendLine("===== "+Path.GetFileName(Path.GetDirectoryName(file))+"/"+Path.GetFileName(file)+" =====");try{b.AppendLine(Tail(file));}catch(Exception e){b.AppendLine("ログ読込エラー: "+e.Message);}}return Redact(b.ToString());}
    public static void Show(Form owner){
        using(var f=new Form{Text="ログ・バグ報告  "+Version,Size=new Size(1050,730),MinimumSize=new Size(800,500),StartPosition=FormStartPosition.CenterParent,Font=owner.Font}){
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(12)};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));f.Controls.Add(layout);
            layout.Controls.Add(new Label{Text="最新ログと過去のログを確認できます。診断レポートは共有前に内容を確認してください。",Dock=DockStyle.Fill});
            var split=new SplitContainer{Dock=DockStyle.Fill,SplitterDistance=320};layout.Controls.Add(split);
            var left=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};left.RowStyles.Add(new RowStyle(SizeType.Absolute,120));left.RowStyles.Add(new RowStyle(SizeType.Percent,100));split.Panel1.Controls.Add(left);
            var group=new GroupBox{Text="最新ログ",Dock=DockStyle.Fill,Padding=new Padding(8)};left.Controls.Add(group,0,0);
            var latestLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};latestLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));latestLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));group.Controls.Add(latestLayout);
            var latestName=new Label{Text="まだログがありません。",Dock=DockStyle.Fill,AutoEllipsis=true};latestLayout.Controls.Add(latestName,0,0);
            var latest=new Button{Text="最新ログを開く",Dock=DockStyle.Fill,Enabled=false};latestLayout.Controls.Add(latest,0,1);
            var history=new GroupBox{Text="過去のログ",Dock=DockStyle.Fill,Padding=new Padding(8)};left.Controls.Add(history,0,1);
            var list=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true};history.Controls.Add(list);
            var text=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Font=new Font("Consolas",10)};split.Panel2.Controls.Add(text);
            var bar=new FlowLayoutPanel{Dock=DockStyle.Fill};layout.Controls.Add(bar);
            var refresh=new Button{Text="更新",AutoSize=true};var export=new Button{Text="診断レポートを保存",AutoSize=true};var folder=new Button{Text="記録フォルダを開く",AutoSize=true};bar.Controls.Add(refresh);bar.Controls.Add(export);bar.Controls.Add(folder);
            string latestPath=null,selectedPath=null;bool updating=false;
            Action<string> show=path=>{selectedPath=path;try{text.Text=path==null?"まだログがありません。":Redact(Tail(path));}catch(Exception error){text.Text=error.Message;}};
            Action update=()=>{
                string previous=selectedPath;updating=true;
                try{
                    var files=Files().ToArray();latestPath=files.FirstOrDefault();
                    latest.Enabled=latestPath!=null;
                    latestName.Text=latestPath==null?"まだログがありません。":new LogEntry{Path=latestPath}.ToString()+Environment.NewLine+"更新日時: "+File.GetLastWriteTime(latestPath).ToString("yyyy/MM/dd HH:mm:ss");
                    list.Items.Clear();foreach(var file in files.Skip(1))list.Items.Add(new LogEntry{Path=file});
                    int selected=-1;for(int i=0;i<list.Items.Count;i++)if(String.Equals(((LogEntry)list.Items[i]).Path,previous,StringComparison.OrdinalIgnoreCase)){selected=i;break;}
                    if(selected>=0){list.SelectedIndex=selected;show(((LogEntry)list.Items[selected]).Path);}
                    else show(latestPath);
                }finally{updating=false;}
            };
            list.SelectedIndexChanged+=(s,e)=>{if(updating)return;var entry=list.SelectedItem as LogEntry;if(entry!=null)show(entry.Path);};
            latest.Click+=(s,e)=>{update();list.ClearSelected();show(latestPath);};refresh.Click+=(s,e)=>update();
            folder.Click+=(s,e)=>System.Diagnostics.Process.Start("explorer.exe",Program.Data);
            export.Click+=(s,e)=>{using(var picker=new SaveFileDialog{Filter="テキストファイル|*.txt",FileName="BVE6互換ランチャー_診断_"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt"})if(picker.ShowDialog(f)==DialogResult.OK){try{File.WriteAllText(picker.FileName,Report(selectedPath),Encoding.UTF8);MessageBox.Show(f,"診断レポートを保存しました。送信前に内容を確認してください。","保存完了");}catch(Exception error){MessageBox.Show(f,error.Message,"保存できませんでした");}}};
            update();f.Shown+=(s,e)=>split.SplitterDistance=Math.Min(360,split.Width/3);f.ShowDialog(owner);
        }
    }

    sealed class LogEntry{public string Path;public override string ToString(){return System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Path))+" / "+System.IO.Path.GetFileName(Path);}}
}
