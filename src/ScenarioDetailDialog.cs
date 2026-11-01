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
        internal Item(string path,double weight,string label){Path=path;Weight=weight;Label=label;}
        public override string ToString(){return Label;}
    }

    internal string SelectedRoute {get;private set;}
    internal string SelectedVehicle {get;private set;}
    internal int LaunchMode {get;private set;}
    private readonly ListBox routeList=new ListBox(),vehicleList=new ListBox();
    private readonly Label details=new Label();
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
            if(ScenarioChoices.IsRandom(value))
                label+="  [重み "+choice.Weight.ToString("G",System.Globalization.CultureInfo.InvariantCulture)+"]";
            items.Add(new Item(choice.Path,choice.Weight,label));
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

        Text="詳細選択 | BVE 起動ランチャー";
        Size=new Size(920,680);MinimumSize=new Size(660,520);
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
        BuildColumn(columns,routeList,0,"路線・マップの候補",routeSpec,routes,String.IsNullOrWhiteSpace(routeSpec)?0:ScenarioChoices.Parse(Path.GetDirectoryName(scenario),routeSpec).Count);
        BuildColumn(columns,vehicleList,1,"車両の候補",vehicleSpec,vehicles,String.IsNullOrWhiteSpace(vehicleSpec)?0:ScenarioChoices.Parse(Path.GetDirectoryName(scenario),vehicleSpec).Count);
        routeList.SelectedIndexChanged+=(s,e)=>RefreshStatus();
        vehicleList.SelectedIndexChanged+=(s,e)=>RefreshStatus();
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
        RefreshStatus();
    }

    private static void BuildColumn(TableLayoutPanel parent,ListBox list,int column,string title,string raw,List<Item> available,int total){
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Margin=new Padding(3)};
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,53));
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
                 "\n（候補名の右はフォルダー名・設定上の重み）",
            Dock=DockStyle.Fill,ForeColor=Color.FromArgb(105,117,132),Font=new Font("Yu Gothic UI",9)
        },0,2);
    }

    private void RefreshStatus(){
        var r=routeList.SelectedItem as Item;
        var v=vehicleList.SelectedItem as Item;
        string route=r==null?"シナリオ設定どおり":Path.GetFileName(r.Path);
        string vehicle=v==null?"シナリオ設定どおり":Path.GetFileName(v.Path);
        details.Text="起動時の選択: 路線 "+route+" ／ 車両 "+vehicle;
    }

    private void Accept(int mode){
        SelectedRoute=(routeList.SelectedItem as Item)==null?null:((Item)routeList.SelectedItem).Path;
        SelectedVehicle=(vehicleList.SelectedItem as Item)==null?null:((Item)vehicleList.SelectedItem).Path;
        LaunchMode=mode;
        DialogResult=DialogResult.OK;
        Close();
    }
}
