using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;

// Setup and updater share an explicit, version-pinned path. No encoded commands.
internal static class Downloader {
    [STAThread] static int Main(string[] args){
        Application.EnableVisualStyles();
        try{
            if(args.Length==3&&args[0]=="--set-bveex"){
                try{BveExLocation.Save(args[1],args[2]);return 0;}catch(Exception ex){MessageBox.Show("BveEXの場所を変更できませんでした。\n"+ex.Message,"BveEXの設定",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
            }
            if(args.Length==2&&args[0]=="--cleanup-root"){
                string cleanupRoot=InstallEngine.Root(args[1]);
                for(int i=0;i<180;i++){try{if(!InstallEngine.CleanupOld(cleanupRoot))return 0;}catch(IOException){}catch(UnauthorizedAccessException){return 1;}System.Threading.Thread.Sleep(1000);}
                return 1;
            }
            bool update=args.Length>=1&&args.Length<=2&&args[0]=="--apply"&&(args.Length==1||args[1]=="--launch"||args[1]=="--no-launch");
            bool? launchAfterUpdate=args.Length==2&&update?(bool?)(args[1]=="--launch"):null;
            bool elevated=args.Length==2&&(args[0]=="--install-root"||args[0]=="--update-root");
            if(args.Length!=0&&!update&&!elevated)throw new InvalidDataException("起動方法が正しくありません。");
            if(elevated){DownloadAndInstall(InstallEngine.Root(args[1]),args[0]=="--update-root");return 0;}
            if(update){string root=InstallEngine.DetectRoot(AppDomain.CurrentDomain.BaseDirectory);if(root==null)throw new InvalidDataException("インストールしたアプリから更新してください。");RunElevated(root,true);
                bool launch=launchAfterUpdate??(MessageBox.Show("更新が完了しました。ランチャーを起動しますか？","BVE6互換ランチャー",MessageBoxButtons.YesNo,MessageBoxIcon.Information)==DialogResult.Yes);
                if(launch){
                    try{string updatedStarter=Path.Combine(root,"LauncherStarter.exe");if(!File.Exists(updatedStarter))throw new FileNotFoundException("起動用ファイルが見つかりません。");var launched=Process.Start(new ProcessStartInfo(updatedStarter){UseShellExecute=true,WorkingDirectory=root});if(launched==null)throw new IOException("起動用ファイルを開けませんでした。");launched.Dispose();}
                    catch(Exception ex){MessageBox.Show("更新は完了しましたが、自動的に起動できませんでした。\nランチャーのショートカットから開いてください。\n"+ex.Message,"起動できませんでした",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
                }else MessageBox.Show("更新が完了しました。\nランチャーは起動していません。","BVE 起動ランチャー",MessageBoxButtons.OK,MessageBoxIcon.Information);
                return 0;}
            var options=InstallerOptions.Choose();if(options==null)return 0;
            string destination=InstallEngine.Root(options.InstallRoot);RunElevated(destination,false);
            string starter=Path.Combine(destination,"LauncherStarter.exe");
            if(options.DesktopShortcut)try{InstallShell.Shortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),destination);}catch(Exception e){MessageBox.Show("本体の導入は完了しましたが、デスクトップへの登録ができませんでした。\n"+e.Message,"導入完了");}
            if(options.LaunchAfterInstall)Process.Start(new ProcessStartInfo(starter){UseShellExecute=true,WorkingDirectory=destination});
            MessageBox.Show("Ver."+Release.Display+" の導入が完了しました。","BVE6互換ランチャー");return 0;
        }catch(System.ComponentModel.Win32Exception e){MessageBox.Show("管理者権限の確認または起動を完了できませんでした。\n"+e.Message,"導入・更新を中止しました");return 1;}
        catch(Exception e){MessageBox.Show(e.Message,"導入・更新を中止しました",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
    }
    static void RunElevated(string root,bool update){
        if(root.IndexOf('"')>=0)throw new InvalidDataException("インストール先が不正です。");
        using(var process=Process.Start(new ProcessStartInfo(Application.ExecutablePath,(update?"--update-root":"--install-root")+" \""+root+"\""){UseShellExecute=true,Verb="runas"})){
            if(process==null)throw new IOException("インストールを開始できませんでした。");
            using(var progress=new Form{Text="BVE6互換ランチャー Ver."+Release.Display,Size=new Size(520,160),StartPosition=FormStartPosition.CenterScreen,ControlBox=false}){
                progress.Controls.Add(new ProgressBar{Dock=DockStyle.Bottom,Height=22,Style=ProgressBarStyle.Marquee});progress.Controls.Add(new Label{Text="GitHubからファイルを取得し、確認してインストールしています。",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter});
                progress.Show();while(!process.WaitForExit(100))Application.DoEvents();
            }
            if(process.ExitCode!=0)throw new IOException("インストールは完了していません。表示されたエラーをご確認ください。");
        }
    }
    static void DownloadAndInstall(string root,bool update){
        var manifest=InstallEngine.ParseManifest(SecureHttp.Text(update?Release.UpdateUrl:Release.PackageManifestUrl),new Version(Release.Number),update?null:Release.Number);
        string previous=InstallEngine.InstalledVersion(root);
        string work=Path.Combine(Path.GetTempPath(),"BVE6Launcher-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        try{
            string zip=Path.Combine(work,"package.zip");SecureHttp.File(manifest.url,zip);string installed=InstallEngine.Install(root,zip,manifest,null);
            if(previous!=null){try{string helper=Path.Combine(Path.GetDirectoryName(installed),"導入・更新.exe");var cleanup=Process.Start(new ProcessStartInfo(helper,"--cleanup-root \""+root+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(installed)});if(cleanup==null)throw new IOException("旧版の整理を開始できませんでした。");cleanup.Dispose();}catch(Exception e){MessageBox.Show("更新は完了しましたが、旧版の自動整理を開始できませんでした。\n"+e.Message,"更新完了",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
            try{InstallShell.Shortcut(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),root);InstallShell.Register(root);}catch(Exception e){MessageBox.Show("本体の導入は完了しましたが、Windowsへの登録を完了できませんでした。\n"+e.Message+"\n起動場所: "+Path.Combine(root,"LauncherStarter.exe"),"導入完了");}
        }finally{if(Directory.Exists(work)){InstallEngine.RejectLinks(work);Directory.Delete(work,true);}}
    }
}
