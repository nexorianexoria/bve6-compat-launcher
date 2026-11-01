using System;
using System.Drawing;
using System.Windows.Forms;

// Update notes and post-update launch choice. This form only presents trusted data;
// downloading and installation remain in the existing updater implementation.
internal static class UpdatePrompt {
    internal static string Notes(string value){
        if(String.IsNullOrWhiteSpace(value))return "更新内容が登録されていません。";
        string clean=System.Text.RegularExpressions.Regex.Replace(value.Trim(),@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]","");
        return clean.Length<=10000?clean:clean.Substring(0,10000)+"\r\n…（以降省略）";
    }
    internal static bool Show(IWin32Window owner,InstallEngine.Manifest update,bool installed,out bool launchAfterUpdate){
        launchAfterUpdate=false;
        using(var dialog=new Form{Text="アップデートのお知らせ",StartPosition=FormStartPosition.CenterParent,Size=new Size(700,530),MinimumSize=new Size(510,420),Font=new Font("Yu Gothic UI",10),BackColor=Color.FromArgb(245,248,252),ShowIcon=false}){
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22,18,22,16),ColumnCount=1,RowCount=5};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,43));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,67));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,51));
            dialog.Controls.Add(layout);
            layout.Controls.Add(new Label{Text="新しいバージョンが利用できます",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=new Font("Yu Gothic UI",15,FontStyle.Bold),ForeColor=Color.FromArgb(34,62,96)},0,0);
            layout.Controls.Add(new Label{Text="現在  Ver."+Release.Display+"       →       最新  Ver."+update.displayVersion,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=new Font("Yu Gothic UI",11,FontStyle.Bold),ForeColor=Color.FromArgb(33,104,168)},0,1);
            var notesBox=new Panel{Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(12),Margin=new Padding(0,3,0,6)};
            var noteArea=new TextBox{Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,WordWrap=true,BorderStyle=BorderStyle.None,BackColor=Color.White,ForeColor=Color.FromArgb(39,57,77),Font=new Font("Yu Gothic UI",10),Text="更新内容\r\n\r\n"+Notes(update.notes),TabStop=false};
            notesBox.Controls.Add(noteArea);layout.Controls.Add(notesBox,0,2);
            var selection=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=new Padding(0,3,0,0)};
            selection.RowStyles.Add(new RowStyle(SizeType.Absolute,30));selection.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
            var autoLaunch=new CheckBox{Text="更新完了後にランチャーを自動起動する",Checked=false,Enabled=installed,Dock=DockStyle.Fill,ForeColor=Color.FromArgb(39,57,77)};
            selection.Controls.Add(autoLaunch,0,0);
            selection.Controls.Add(new Label{Text=installed?"更新中はランチャーを終了します。チェックを外した場合は完了後に起動しません。":"フォルダーから直接起動中のため、更新は配布ページから行ってください。",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.FromArgb(105,117,132),AutoEllipsis=true},0,1);
            layout.Controls.Add(selection,0,3);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};
            var accept=new Button{Text=installed?"更新する":"配布ページを開く",Width=156,Height=38,BackColor=Color.FromArgb(32,104,178),ForeColor=Color.White,FlatStyle=FlatStyle.Flat,DialogResult=DialogResult.OK};accept.FlatAppearance.BorderSize=0;
            var dismiss=new Button{Text="今はしない",Width=124,Height=38,DialogResult=DialogResult.Cancel};
            buttons.Controls.Add(accept);buttons.Controls.Add(dismiss);layout.Controls.Add(buttons,0,4);
            dialog.AcceptButton=accept;dialog.CancelButton=dismiss;
            bool confirmed=dialog.ShowDialog(owner)==DialogResult.OK;
            launchAfterUpdate=confirmed&&installed&&autoLaunch.Checked;
            return confirmed;
        }
    }
}
