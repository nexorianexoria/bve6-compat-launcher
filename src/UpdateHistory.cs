using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

// Offline summaries of published versions. Full release notes are available
// from the official GitHub releases page; do not invent entries for drafts.
internal static class UpdateHistory {
    internal sealed class Entry {
        internal readonly string Version,Date,Summary;
        internal Entry(string version,string date,string summary){
            Version=version;Date=date;Summary=summary;
        }
        public override string ToString(){return "Ver."+Version+"  ("+Date+")";}
    }

    internal static readonly Entry[] Published={
        new Entry("0.29b15","2026/11/13",
            "指定した起動モニターにBVEの運転画面が配置されても、シナリオ読み込み中の再最大化によって別のモニターへ戻る問題に対応しました。\r\n"+
            "最大化状態とフォーカスを維持しながら、起動時にメイン画面の配置を再確認します。"),
        new Entry("0.29b14","2026/11/08",
            "BVE5・BVE6で別ウィンドウ操作中も音声を維持するオプションを追加しました（初期設定はオフ）。\r\n"+
            "元のBVE本体を変更せず、DSOALを検証用コピーに配置して起動します。\r\n"+
            "一部の路線・ATS・TsHudについては動作未確認です。"),
        new Entry("0.29b13","2026/11/07",
            "BVE運転画面の最前面固定を追加しました（ウィンドウ表示のみ、初期設定はオフ）。\r\n"+
            "路線・車両の一覧表示用パネル画像処理と32ビットATS互換ブリッジを軽量化しました。\r\n"+
            "バックグラウンド音声の切り替えは未対応です。"),
        new Entry("0.29b11","2026/11/04",
            "BVE起動前に画面サイズ・起動モニター・描画負荷と追加入力デバイスを設定可能に。\r\n"+
            "Windows登録のモニター名・解像度・Hzを表示し、NumerousControllerInterfaceのプロパティー設定に対応。"),
        new Entry("0.29b9","2026/11/01",
            "更新によるタスクバー表示の変化を抑えるため、ランチャーのアプリ識別子を固定。\r\n"+
            "ルート設定に「他の車両から選ぶ」を追加し、通常の車両一覧から選択可能に。"),
        new Entry("0.29b8","2026/11/01",
            "b7の配布パッケージに古い実行ファイルが含まれていた問題を修正。\r\n"+
            "「ルート設定」、画像付き車両候補、ルート名保存などの修正を正しく反映。"),
        new Entry("0.29b7","2026/11/01",
            "詳細選択のボタンを「ルート設定」に変更。\r\n"+
            "車両候補を通常の車両選択画面と同じ画像付きカード一覧で表示。\r\n"+
            "ルートごとの自分用の表示名を保存できる機能を追加。\r\n"+
            "候補一覧ではランダム選択の「重み」表示を省略。"),
        new Entry("0.29b6","2026/11/01",
            "ルート・車両のランダム候補をそれぞれ確認して選べる詳細選択画面を追加。\r\n"+
            "路線のみ、車両のみ、両方、どちらも指定しない起動方法に対応。\r\n"+
            "設定画面に配布サイト・更新履歴・手動の更新確認を追加。\r\n"+
            "詳細選択ボタンはシナリオ選択時だけ有効になります。"),
        new Entry("0.29b5","2026/11/01",
            "BVE運転中のランチャーによる一覧走査や画像読み込みを抑制。\r\n"+
            "32ビットATS互換処理のメモリ処理やログ書き込みを軽量化。\r\n"+
            "インストーラーと起動時の更新確認を復旧。\r\n\r\n"+
            "※実際のFPS向上や、すべてのATSプラグインの動作は未保証です。"),
        new Entry("0.29b4","2026/10/31",
            "運転中のランチャー負荷を軽減する試験版。\r\n"+
            "路線・車両の一覧走査と画像読み込みを抑制し、BVE終了後に再開。\r\n"+
            "ZIPで公開した試験版です。"),
        new Entry("0.29b3","2026/10/31",
            "路線と車両の一覧を整理し、対応バージョンを分かりやすく表示。\r\n"+
            "通常起動ボタンの優先表示を改善。\r\n"+
            "アップデート内容の専用画面と、更新後の自動起動オプションを追加。"),
        new Entry("0.29b2","2026/10/31","軽微な修正と機能の改善。"),
        new Entry("0.29b1","2026/10/31","利用規約の更新と機能の改善。")
    };

    internal static void OpenOfficial(IWin32Window owner,string url) {
        try {Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}
        catch(Exception ex){MessageBox.Show(owner,"ブラウザーでページを開けませんでした。\n"+ex.Message,"ページを開けません",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }

    internal static Form CreateForm() {
        var dialog=new Form{Text="更新履歴 | BVE 起動ランチャー",Size=new Size(760,580),MinimumSize=new Size(560,460),StartPosition=FormStartPosition.CenterParent,Font=new Font("Yu Gothic UI",10),BackColor=Color.FromArgb(245,248,252)};
        var outer=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(18,16,18,14)};
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute,58));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        dialog.Controls.Add(outer);
        outer.Controls.Add(new Label{Text="これまでの更新内容",Dock=DockStyle.Fill,Font=new Font("Yu Gothic UI",14,FontStyle.Bold),ForeColor=Color.FromArgb(27,48,77),TextAlign=ContentAlignment.MiddleLeft},0,0);
        var split=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,225));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        outer.Controls.Add(split,0,1);
        var versions=new ListBox{Dock=DockStyle.Fill,Font=new Font("Yu Gothic UI",10),IntegralHeight=false};
        foreach(var entry in Published)versions.Items.Add(entry);
        split.Controls.Add(versions,0,0);
        var details=new TextBox{Dock=DockStyle.Fill,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.White,Font=new Font("Yu Gothic UI",10),Margin=new Padding(10,0,0,0)};
        split.Controls.Add(details,1,0);
        versions.SelectedIndexChanged+=(s,e)=>{
            var entry=versions.SelectedItem as Entry;
            details.Text=entry==null?"":"Ver."+entry.Version+"　　公開日: "+entry.Date+"\r\n\r\n"+entry.Summary;
        };
        versions.SelectedIndex=0;
        var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,125));
        outer.Controls.Add(actions,0,2);
        var all=new Button{Text="すべての更新履歴を配布サイトで見る",Dock=DockStyle.Fill,Margin=new Padding(0,6,10,3)};
        all.Click+=(s,e)=>OpenOfficial(dialog,Release.HistoryUrl);
        actions.Controls.Add(all,0,0);
        var close=new Button{Text="閉じる",Dock=DockStyle.Fill,Margin=new Padding(0,6,0,3),DialogResult=DialogResult.OK};
        actions.Controls.Add(close,1,0);dialog.AcceptButton=close;
        return dialog;
    }

    internal static void Show(IWin32Window owner){using(var dialog=CreateForm())dialog.ShowDialog(owner);}
}
