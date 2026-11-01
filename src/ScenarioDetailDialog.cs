using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

// An opt-in preview of the candidates actually listed in the scenario.
// Each side can be left at the original setting, independently of the other.
internal sealed class ScenarioDetailDialog : Form {
    internal sealed class Item {
        internal readonly string Path;
        internal readonly double Weight;
        internal readonly string Label;
        internal readonly bool IsRoute;
        internal Item(string path,double weight,string label,bool isRoute=false){Path=path;Weight=weight;Label=label;IsRoute=isRoute;}
        public override string ToString(){
            if(!IsRoute)return Label;
            string original=System.IO.Path.GetFileNameWithoutExtension(Path);
            string shown=RouteDisplayNames.Display(Path);
            return Label.StartsWith(original,StringComparison.Ordinal)?shown+Label.Substring(original.Length):shown+"  "+Label;
        }
    }

    internal string SelectedRoute {get;private set;}
    internal string SelectedVehicle {get;private set;}
    internal int LaunchMode {get;private set;}
    private readonly ListBox routeList=new ListBox();
    // Same image-backed, selectable card view as the main vehicle selection.
    private readonly CatalogView vehicleList=new CatalogView{ShowVehicleLocation=true};
    private readonly Label details=new Label();
    private readonly TextBox routeName=new TextBox();
    private readonly Button saveRouteName=new Button();
    private readonly Label routeEditorInfo=new Label();
    private readonly Button clearVehicle=new Button();
    private TableLayoutPanel routeColumn,routeEditorPanel;
    private readonly Button[] startButtons=new Button[3];
    private readonly string scenario;

    internal static List<Item> Available(string scenario,string value,bool vehicle){
        string directory=Path.GetDirectoryName(Path.GetFullPath(scenario));
        var items=new List<Item>();
        if(String.IsNullOrWhiteSpace(value))return items;
        foreach(var choice in ScenarioChoices.Parse(directory,value)){
            if(!File.Exists(choice.Path))continue;
            if(vehicle){
                try {
                    var lines=Program.Read(choice.Path);
                    if(lines.Length==0||!Program.VehicleHeader(lines[0]))continue;
                }catch(IOException){continue;}
                catch(UnauthorizedAccessException){continue;}
            }
            string name=Path.GetFileNameWithoutExtension(choice.Path);
            string parent=Path.GetFileName(Path.GetDirectoryName(choice.Path));
            string label=name+"  ("+parent+")";
            items.Add(new Item(choice.Path,choice.Weight,label,!vehicle));
        }
        return items;
    }

    internal ScenarioDetailDialog(string path) {
        scenario=Path.GetFullPath(path);
        var source=Program.Read(scenario);
        if(source.Length==0||!Program.ScenarioHeader(source[0]))
            throw new InvalidDataException("このファイルはBVEシナリオではありません。");
        string routeSpec=Program.Value(source,"Route"),vehicleSpec=Program.Value(source,"Vehicle");
        var routes=Available(scenario,routeSpec,false);
        var vehicles=Available(scenario,vehicleSpec,true);

        Text="路線・車両の詳細選択 | BVE 起動ランチャー";
        Size=new Size(1000,760);MinimumSize=new Size(730,620);
        StartPosition=FormStartPosition.CenterParent;
        Font=new Font("Yu Gothic UI",10);
        BackColor=Color.FromArgb(245,248,252);
        var main=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1,Padding=new Padding(18,14,18,16)};
        main.RowStyles.Add(new RowStyle(SizeType.Absolute,76));
        main.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute,58));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute,55));
        Controls.Add(main);
        main.Controls.Add(new Label{
            Text="路線と車両の候補を詳しく選ぶ\n選択しなければ元のシナリオの指定どおり（ランダムの場合はランダム）で起動します。",
            Dock=DockStyle.Fill,Font=new Font("Yu Gothic UI",11),ForeColor=Color.FromArgb(34,62,96)
        },0,0);

        var columns=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        main.Controls.Add(columns,0,1);
        routeColumn=BuildColumn(columns,routeList,0,"路線・マップの候補",routeSpec,routes,String.IsNullOrWhiteSpace(routeSpec)?0:ScenarioChoices.Parse(Path.GetDirectoryName(scenario),routeSpec).Count);
        BuildVehicleColumn(columns,vehicles,vehicleSpec);
        BuildRouteEditor(routeColumn);
        routeList.SelectedIndexChanged+=(s,e)=>{UpdateRouteEditor();RefreshStatus();};
        vehicleList.SelectionChanged+=(s,e)=>RefreshStatus();
        details.Dock=DockStyle.Fill;details.ForeColor=Color.FromArgb(68,85,105);
        details.TextAlign=ContentAlignment.MiddleLeft;
        details.AutoEllipsis=true;main.Controls.Add(details,0,2);
        var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1};
        for(int i=0;i<3;i++)actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,29));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,13));
        main.Controls.Add(actions,0,3);
        string[] labels={"BVE6 互換起動","BVE6 通常起動","BVE5 通常起動"};
        for(int i=0;i<3;i++){
            int mode=i;
            var button=new Button{Text=labels[i],Dock=DockStyle.Fill,Margin=new Padding(3),ForeColor=Color.White,BackColor=i==0?Color.FromArgb(18,119,103):Color.FromArgb(31,102,181),FlatStyle=FlatStyle.Flat};
            button.FlatAppearance.BorderSize=0;
            button.Click+=(s,e)=>Accept(mode);
            startButtons[i]=button;actions.Controls.Add(button,i,0);
        }
        var cancel=new Button{Text="戻る",DialogResult=DialogResult.Cancel,Dock=DockStyle.Fill,Margin=new Padding(3)};
        actions.Controls.Add(cancel,3,0);
        CancelButton=cancel;
        UpdateRouteEditor();
        RefreshStatus();
    }

    private static TableLayoutPanel BuildColumn(TableLayoutPanel parent,ListBox list,int column,string title,string raw,List<Item> available,int total){
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1,Margin=new Padding(3)};
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,186));
        parent.Controls.Add(panel,column,0);
        panel.Controls.Add(new Label{Text=title,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.FromArgb(27,48,77),Font=new Font("Yu Gothic UI",11,FontStyle.Bold)},0,0);
        list.Dock=DockStyle.Fill;list.IntegralHeight=false;list.HorizontalScrollbar=true;
        list.Items.Add(ScenarioChoices.IsRandom(raw)?"指定しない（ランダム）":"指定しない（元の設定どおり）");
        foreach(var item in available)list.Items.Add(item);
        list.SelectedIndex=0;
        panel.Controls.Add(list,0,1);
        // Show missing candidates without offering a broken launch choice.
        panel.Controls.Add(new Label{
            Text="候補 "+available.Count+"件"+(total>available.Count?" ／利用不可 "+(total-available.Count)+"件":"")+
                 "\n候補名の右は保存先フォルダー名",
            Dock=DockStyle.Fill,ForeColor=Color.FromArgb(105,117,132),Font=new Font("Yu Gothic UI",9)
        },0,2);
        return panel;
    }

    private void BuildVehicleColumn(TableLayoutPanel parent,List<Item> available,string raw){
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1,Margin=new Padding(3)};
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
        parent.Controls.Add(panel,1,0);
        panel.Controls.Add(new Label{
            Text="車両の候補",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,
            ForeColor=Color.FromArgb(27,48,77),Font=new Font("Yu Gothic UI",11,FontStyle.Bold)
        },0,0);
        clearVehicle.Text=ScenarioChoices.IsRandom(raw)?"指定しない（ランダム）":"指定しない（シナリオの設定どおり）";
        clearVehicle.Dock=DockStyle.Fill;
        clearVehicle.Click+=(s,e)=>vehicleList.ClearSelection();
        panel.Controls.Add(clearVehicle,0,1);

        // Use the exact same CatalogView and metadata/thumbnail loader as the
        // regular vehicle list. Only scenario-listed, existing candidates
        // are supplied; the user's vehicle files are never changed.
        var entries=available.Select(v=>new Program.Entry{
            Path=v.Path,Title=Path.GetFileNameWithoutExtension(v.Path)
        }).ToArray();
        var cards=CatalogData.Build(entries,Path.GetDirectoryName(scenario),true);
        vehicleList.Dock=DockStyle.Fill;
        vehicleList.AccessibleName="車両のランダム候補（画像付き）";
        vehicleList.EmptyText="選択できる車両がありません";
        vehicleList.SetItems(cards);
        panel.Controls.Add(vehicleList,0,2);
        panel.Controls.Add(new Label{
            Text="候補 "+available.Count+"件",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,
            ForeColor=Color.FromArgb(105,117,132),Font=new Font("Yu Gothic UI",9)
        },0,3);
    }

    private void BuildRouteEditor(TableLayoutPanel parent){
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(8),RowCount=4,ColumnCount=1,BackColor=Color.WhiteSmoke};
        routeEditorPanel=panel;
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,43));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        parent.Controls.Add(panel,0,3);
        panel.Controls.Add(new Label{Text="自分用のルート名",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=new Font("Yu Gothic UI",10,FontStyle.Bold)},0,0);
        routeName.Dock=DockStyle.Fill;routeName.MaxLength=48;routeName.AccessibleName="自分用のルート名";
        panel.Controls.Add(routeName,0,1);
        saveRouteName.Text="この名前を保存";saveRouteName.Dock=DockStyle.Fill;saveRouteName.Click+=(s,e)=>SaveRouteName();
        panel.Controls.Add(saveRouteName,0,2);
        routeEditorInfo.Dock=DockStyle.Fill;routeEditorInfo.Font=new Font("Yu Gothic UI",9);routeEditorInfo.ForeColor=Color.DimGray;routeEditorInfo.TextAlign=ContentAlignment.MiddleLeft;
        panel.Controls.Add(routeEditorInfo,0,3);
        routeName.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;SaveRouteName();}};
    }
    private void UpdateRouteEditor(){
        bool enabled=RouteDisplayNames.Enabled;
        var selected=routeList.SelectedItem as Item;
        bool show=enabled&&selected!=null;
        routeColumn.RowStyles[3].Height=show?186:0;
        routeEditorPanel.Visible=show;
        routeName.Enabled=enabled&&selected!=null;
        saveRouteName.Enabled=enabled&&selected!=null;
        routeName.Text=selected==null?"":(RouteDisplayNames.GetName(selected.Path)??"");
        routeEditorInfo.Text="空欄で保存すると自分用の名前を解除します。";
    }
    private void SaveRouteName(){
        var item=routeList.SelectedItem as Item;
        if(item==null||!RouteDisplayNames.Enabled)return;
        try{
            RouteDisplayNames.SetName(item.Path,routeName.Text);
            int selected=routeList.SelectedIndex;
            routeList.Items[selected]=new Item(item.Path,item.Weight,item.Label,true);
            routeList.SelectedIndex=selected;
            routeEditorInfo.Text="保存しました。次回もこの名前で表示します。";
        }catch(Exception ex){
            Diagnostics.Write("ROUTE_ALIAS_SAVE",ex.Message);
            MessageBox.Show(this,"ルート名を保存できませんでした。\n"+ex.Message,"ルート名の保存",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
    }
    private void RefreshStatus(){
        var r=routeList.SelectedItem as Item;
        var v=vehicleList.SelectedItem;
        string route=r==null?"シナリオ設定どおり":RouteDisplayNames.Display(r.Path);
        string vehicle=v==null?"シナリオ設定どおり":Path.GetFileName(v.Entry.Path);
        details.Text="起動時の選択: 路線 "+route+" ／ 車両 "+vehicle;
    }

    private void Accept(int mode){
        SelectedRoute=(routeList.SelectedItem as Item)==null?null:((Item)routeList.SelectedItem).Path;
        SelectedVehicle=vehicleList.SelectedItem==null?null:vehicleList.SelectedItem.Entry.Path;
        LaunchMode=mode;
        DialogResult=DialogResult.OK;
        Close();
    }
}
