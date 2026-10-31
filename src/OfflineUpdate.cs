using System;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;

internal static class OfflineUpdate {
 const string Expected="C44D9A8D6A3CF3782DB01D7A112E41625BD8C03A7A6CB5E99911657102EB1699";
 [STAThread] static int Main(string[] args){
  Application.EnableVisualStyles();
  try{
   string zip=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BVE6Launcher-package.zip");
   if(args.Length==2&&args[0]=="--apply"){
    string root=InstallEngine.Root(args[1]);
    var manifest=new InstallEngine.Manifest{version=Release.Number,displayVersion=Release.Display,url=Release.BaseUrl+"downloads/"+Release.Display+"/BVE6Launcher-package.zip",sha256=Expected};
    InstallEngine.Install(root,zip,manifest,null);
    try{InstallShell.Shortcut(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),root);InstallShell.Register(root);}catch(Exception ex){MessageBox.Show("更新は完了しましたが、スタートメニュー登録を確認してください。\n"+ex.Message);}
    MessageBox.Show("0.29a4へ更新しました。スタートメニューから起動してください。","更新完了");return 0;
   }
   if(args.Length!=0)throw new InvalidDataException("起動方法が正しくありません。");
   string existing=InstallShell.FindExistingRoot();if(existing==null)throw new InvalidOperationException("既存のインストール先を検出できません。更新は行っていません。");
   if(!File.Exists(zip)||!InstallEngine.Hash(zip).Equals(Expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("同じフォルダーの更新パッケージが見つからないか、内容が一致しません。");
   if(MessageBox.Show("次の場所を0.29a4へ更新します。旧版と設定は保持します。\n\n"+existing,"更新の確認",MessageBoxButtons.YesNo)!=DialogResult.Yes)return 0;
   using(var p=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--apply \""+existing+"\""){UseShellExecute=true,Verb="runas"})){if(p==null)throw new IOException("更新を開始できませんでした。");p.WaitForExit();return p.ExitCode;}
  }catch(Exception ex){MessageBox.Show(ex.Message,"更新できませんでした",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
 }
}
