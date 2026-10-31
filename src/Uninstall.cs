using System;
using System.IO;
using System.Windows.Forms;
internal static class Uninstall {
    [STAThread] static int Main(){Application.EnableVisualStyles();try{string root=InstallEngine.DetectRoot(AppDomain.CurrentDomain.BaseDirectory);if(root==null)throw new IOException("インストール先を確認できません。");if(MessageBox.Show("ランチャーを削除しますか？ BVEとランチャーを終了してから進めてください。\nログ、MotorMaster登録、旧版、利用者が追加したファイルは残します。","BVE 起動ランチャーの削除",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return 0;var retained=InstallEngine.Uninstall(root);InstallShell.RemoveShortcut(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),root);InstallShell.RemoveShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),root);InstallShell.Unregister(root);MessageBox.Show("削除処理を終了しました。ログと設定は保持しています。\n実行中のUninstall.exeなど、保持したファイルがあります。この画面を閉じた後に必要に応じて確認してください。\n"+String.Join("\n",retained.ToArray()),"削除結果");return 0;}catch(Exception e){MessageBox.Show(e.Message,"削除を完了できませんでした");return 1;}}
}
