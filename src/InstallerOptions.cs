using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;

public sealed class InstallerOptions {
    public string InstallRoot {get;set;}
    public bool DesktopShortcut {get;set;}
    public bool LaunchAfterInstall {get;set;}
    internal static InstallerOptions Choose(){
        string terms;
        using(var resource=typeof(InstallerOptions).Assembly.GetManifestResourceStream("terms.txt")){
            if(resource==null)throw new InvalidDataException("利用規約が見つかりません。導入を中止します。");
            using(var reader=new StreamReader(resource,System.Text.Encoding.UTF8,true))terms=reader.ReadToEnd();
        }
        using(var f=new Form{Text="BVE6互換ランチャー Ver."+Release.Display+" の導入",Size=new Size(800,810),MinimumSize=new Size(660,700),StartPosition=FormStartPosition.CenterScreen,Font=new Font("Yu Gothic UI",10)}){
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Padding=new Padding(16)};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,138));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,95));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,45));f.Controls.Add(layout);
            layout.Controls.Add(new Label{Text="Ver."+Release.Display+" をダウンロードして導入します。利用規約をご確認ください",Dock=DockStyle.Fill,Font=new Font(f.Font,FontStyle.Bold)},0,0);
            layout.Controls.Add(new TextBox{Text=TermsPresentation.Format(terms),Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,WordWrap=true},0,1);
            var related=new GroupBox{Text="関連ソフトの確認（必要な車両で使用）",Dock=DockStyle.Fill,Padding=new Padding(8)};layout.Controls.Add(related,0,2);
            var checks=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=3};checks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));checks.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,145));checks.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,75));for(int i=0;i<3;i++)checks.RowStyles.Add(new RowStyle(SizeType.Percent,100f/3));related.Controls.Add(checks);
            var exeStatus=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};var bveStatus=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};var motorStatus=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};checks.Controls.Add(exeStatus,0,0);checks.Controls.Add(bveStatus,0,1);checks.Controls.Add(motorStatus,0,2);
            var bveLink=new LinkLabel{Text="BveEX公式ページ",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};var motorLink=new LinkLabel{Text="MotorMaster配布元",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};checks.Controls.Add(bveLink,1,1);checks.Controls.Add(motorLink,1,2);
            Action<string> open=url=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(f,"ページを開けませんでした。\n"+url+"\n"+ex.Message,"ページを開けません");}};
            bveLink.LinkClicked+=(s,e)=>open(DependencyCheck.BveExUrl);motorLink.LinkClicked+=(s,e)=>open(DependencyCheck.MotorMasterUrl);
            var details=new ToolTip();f.FormClosed+=(s,e)=>details.Dispose();
            Action refresh=()=>{string bve6=BveLocator.Resolve(6),bve5=BveLocator.Resolve(5);bool found6=bve6!=null&&DependencyCheck.BveExPresent(Path.GetDirectoryName(bve6)),found5=bve5!=null&&DependencyCheck.BveExPresent(Path.GetDirectoryName(bve5));bool registered=DependencyCheck.MotorMasterRegistered();exeStatus.Text="BVE本体: 5 "+(bve5!=null?"検出":"未検出")+" / 6 "+(bve6!=null?"検出":"未検出");details.SetToolTip(exeStatus,"BVE5: "+(bve5??"未検出")+"\nBVE6: "+(bve6??"未検出")+"\n未検出の場合はランチャーで指定できます。");bveStatus.Text="BveEX: BVE5 "+(found5?"確認":"未確認")+" / BVE6 "+(found6?"確認":"未確認");motorStatus.Text=registered?"MotorMaster: このランチャーに登録済み":"MotorMaster: このランチャーには未登録";bveLink.Visible=!(found6&&found5);motorLink.Visible=!registered;};
            var refreshButton=new Button{Text="再確認",Dock=DockStyle.Fill};checks.Controls.Add(refreshButton,2,0);checks.SetRowSpan(refreshButton,3);refreshButton.Click+=(s,e)=>refresh();refresh();
            layout.Controls.Add(new Label{Text="インストール先（選んだフォルダー内に専用フォルダーを作成）",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,3);
            var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,95));layout.Controls.Add(row,0,4);
            string existingRoot=InstallShell.FindExistingRoot();
            var parent=new TextBox{Text=existingRoot==null?Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles):Path.GetDirectoryName(existingRoot),Dock=DockStyle.Fill,ReadOnly=true};row.Controls.Add(parent,0,0);
            var browse=new Button{Text="選ぶ…",Dock=DockStyle.Fill};row.Controls.Add(browse,1,0);
            browse.Click+=(s,e)=>{using(var dialog=new FolderBrowserDialog{Description="インストール先の親フォルダーを選んでください",SelectedPath=parent.Text,ShowNewFolderButton=true})if(dialog.ShowDialog(f)==DialogResult.OK)parent.Text=dialog.SelectedPath;};
            var flags=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};layout.Controls.Add(flags,0,5);
            var desktop=new CheckBox{Text="デスクトップにショートカットを作る",AutoSize=true,Checked=true};flags.Controls.Add(desktop);
            var launch=new CheckBox{Text="インストール完了後にランチャーを起動する",AutoSize=true,Checked=true};flags.Controls.Add(launch);
            var agree=new CheckBox{Text="利用規約に同意します",AutoSize=true};flags.Controls.Add(agree);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill};layout.Controls.Add(buttons,0,6);
            var proceed=new Button{Text="同意してインストール",AutoSize=true,Enabled=false,DialogResult=DialogResult.OK};var cancel=new Button{Text="キャンセル",AutoSize=true,DialogResult=DialogResult.Cancel};buttons.Controls.Add(proceed);buttons.Controls.Add(cancel);
            agree.CheckedChanged+=(s,e)=>proceed.Enabled=agree.Checked;f.AcceptButton=proceed;f.CancelButton=cancel;
            if(f.ShowDialog()!=DialogResult.OK||!agree.Checked)return null;
            return new InstallerOptions{InstallRoot=Path.Combine(parent.Text,"BVE6互換ランチャー"),DesktopShortcut=desktop.Checked,LaunchAfterInstall=launch.Checked};
        }
    }
}
