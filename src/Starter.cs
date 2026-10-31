using System;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;
internal static class Starter {
    [STAThread] static void Main(){
        try{
            string root=AppDomain.CurrentDomain.BaseDirectory;
            string target=Path.GetFullPath(File.ReadAllText(Path.Combine(root,"現在.txt")).Trim());
            string versions=Path.GetFullPath(Path.Combine(root,"versions"))+Path.DirectorySeparatorChar;
            if(!target.StartsWith(versions,StringComparison.OrdinalIgnoreCase)||!File.Exists(target)||!Path.GetFileName(target).Equals("BVE6互換ランチャー.exe",StringComparison.OrdinalIgnoreCase))throw new IOException("ランチャー本体が見つかりません。再インストールしてください。");
            Process.Start(new ProcessStartInfo(target){WorkingDirectory=Path.GetDirectoryName(target),UseShellExecute=true});
        }catch(Exception e){MessageBox.Show(e.Message,"起動できません",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
}
