using System;
using System.IO;
using System.Net;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

internal static class AutoUpdate {
    internal const string ManifestUrl=Release.UpdateUrl;
    internal static InstallEngine.Manifest FindNewer(string json,Version current){
        var update=new JavaScriptSerializer().Deserialize<InstallEngine.Manifest>(json);
        Version next;
        if(update==null||!Version.TryParse(update.version,out next)||next.Revision<0)throw new InvalidDataException("更新情報のバージョンを確認できません。");
        if(next<=current)return null;
        return InstallEngine.ParseManifest(json,current,null);
    }
    internal static string PromptText(InstallEngine.Manifest update,bool installed){
        string notes=String.IsNullOrWhiteSpace(update.notes)?"更新内容は配布ページで確認できます。":update.notes.Trim();
        notes=System.Text.RegularExpressions.Regex.Replace(notes,@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]","");
        if(notes.Length>600)notes=notes.Substring(0,600)+"…";
        string next="新しいバージョンがあります。\n\n現在: Ver."+Release.Display+"\n最新: Ver."+update.displayVersion+"\n\n【更新内容】\n"+notes+"\n\n";
        return next+(installed?"更新しますか？\n更新のためランチャーを終了します。完了後に起動するか選べます。":"ダウンロードページを開きますか？\nフォルダーから直接起動しているため、導入用EXEから更新してください。");
    }
    internal static async void Check(Form owner){
        try {
            string json=await Task.Run(()=>SecureHttp.Text(ManifestUrl));
            if(owner.IsDisposed)return;
            var current=new Version(Release.Number);
            var update=FindNewer(json,current);
            Diagnostics.Write("UPDATE_CHECK",update==null?"現在の版より新しい更新はありません。":"新しい版があります: "+update.displayVersion);
            if(update==null)return;
            string root=null;
            try{root=InstallEngine.DetectRoot(AppDomain.CurrentDomain.BaseDirectory);}catch(InvalidDataException){}catch(IOException){}
            bool launchAfterUpdate;
            if(!UpdatePrompt.Show(owner,update,root!=null,out launchAfterUpdate))return;
            if(root==null){Process.Start(new ProcessStartInfo("https://github.com/"+Release.Repository+"/releases/latest"){UseShellExecute=true});return;}
            string installer=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"導入・更新.exe");
            if(!File.Exists(installer)){MessageBox.Show(owner,"更新用ファイルが見つかりません。");return;}
            var process=Process.Start(new ProcessStartInfo(installer,launchAfterUpdate?"--apply --launch":"--apply --no-launch"){UseShellExecute=true});
            if(process==null)throw new IOException("更新用ファイルを起動できませんでした。");
            process.Dispose();
            owner.Close();
        }catch(Exception ex){Diagnostics.Write("UPDATE_CHECK_ERROR",ex.Message);}
    }
}
