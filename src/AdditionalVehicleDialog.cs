using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// Use the same vehicle cards as the launcher, without changing the scenario.
internal sealed class AdditionalVehicleDialog : Form {
    private readonly List<CatalogItem> all;
    private readonly CatalogView gallery=new CatalogView{ShowVehicleLocation=true};
    private readonly TextBox search=new TextBox();
    private readonly Button choose=new Button();
    internal string VehiclePath {get;private set;}

    internal AdditionalVehicleDialog(IEnumerable<CatalogItem> vehicles){
        all=(vehicles??Enumerable.Empty<CatalogItem>())
            .Where(x=>x!=null&&x.Entry!=null&&!String.IsNullOrWhiteSpace(x.Entry.Path))
            .ToList();
        Text="他の車両から選ぶ | BVE 起動ランチャー";
        Size=new Size(860,690);
        MinimumSize=new Size(610,470);
        StartPosition=FormStartPosition.CenterParent;
        Font=new Font("Yu Gothic UI",10);
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,Padding=new Padding(16)};
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        Controls.Add(root);
        var searchRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        searchRow.Controls.Add(new Label{Text="車両を検索",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);
        search.Dock=DockStyle.Fill;
        search.AccessibleName="他の車両を検索";
        searchRow.Controls.Add(search,1,0);
        root.Controls.Add(searchRow,0,0);
        gallery.Dock=DockStyle.Fill;gallery.AccessibleName="すべての車両（画像付き）";
        gallery.EmptyText="車両が見つかりません。通常画面でシナリオフォルダーを確認してください。";
        root.Controls.Add(gallery,0,1);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        choose.Text="この車両を使用";choose.AutoSize=true;choose.Height=36;choose.Enabled=false;
        choose.Click+=(s,e)=>AcceptVehicle();
        buttons.Controls.Add(choose);
        var back=new Button{Text="戻る",AutoSize=true,Height=36,DialogResult=DialogResult.Cancel};
        buttons.Controls.Add(back);
        root.Controls.Add(buttons,0,2);
        CancelButton=back;
        search.TextChanged+=(s,e)=>ApplySearch();
        gallery.SelectionChanged+=(s,e)=>choose.Enabled=gallery.SelectedItem!=null;
        gallery.DoubleClick+=(s,e)=>{if(gallery.SelectedItem!=null)AcceptVehicle();};
        ApplySearch();
    }
    private void ApplySearch(){
        string term=search.Text.Trim();
        gallery.SetItems(String.IsNullOrWhiteSpace(term)?all:
            all.Where(x=>x.SearchText.IndexOf(term,StringComparison.CurrentCultureIgnoreCase)>=0));
    }
    private void AcceptVehicle(){
        var item=gallery.SelectedItem;
        if(item==null)return;
        if(!System.IO.File.Exists(item.Entry.Path)){
            MessageBox.Show(this,"この車両のファイルが見つかりません。","車両を確認してください");
            return;
        }
        VehiclePath=item.Entry.Path;
        DialogResult=DialogResult.OK;
        Close();
    }
}
