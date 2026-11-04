using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// A small editor for EXISTING NumerousControllerInterface profile names.
// It never invents device profiles and never writes BVE's files directly.
internal sealed class NumerousProfilesDialog : Form {
    private readonly List<ControllerProfile> profiles;
    private readonly Dictionary<string,ComboBox> choices=new Dictionary<string,ComboBox>();
    internal List<ControllerProfile> SelectedProfiles {get;private set;}

    internal NumerousProfilesDialog(IEnumerable<ControllerProfile> source){
        profiles=source.Select(x=>new ControllerProfile{Name=x.Name,Profile=x.Profile}).ToList();
        var allowed=profiles.Select(x=>x.Profile).Distinct(StringComparer.Ordinal).ToArray();
        Text="マスコンの使用プロファイル | 設定";
        Size=new Size(820,570);MinimumSize=new Size(660,440);
        Font=new Font("Yu Gothic UI",9.5f);
        StartPosition=FormStartPosition.CenterParent;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,Padding=new Padding(16)};
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        Controls.Add(root);
        root.Controls.Add(new Label{
            Text="使用するプロファイルを選択してください。元の設定を変更するのは親画面の「設定を保存」を押したときです。",
            Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);
        var list=new TableLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,
            ColumnCount=2,RowCount=profiles.Count};
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,46));
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,54));
        for(int i=0;i<profiles.Count;i++){
            var item=profiles[i];
            list.RowStyles.Add(new RowStyle(SizeType.Absolute,49));
            list.Controls.Add(new Label{Text=item.Name,Dock=DockStyle.Fill,
                TextAlign=ContentAlignment.MiddleLeft},0,i);
            var combo=new ComboBox{Dock=DockStyle.Fill,
                DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(6,9,6,5)};
            combo.Items.AddRange(allowed.Cast<object>().ToArray());
            combo.SelectedItem=item.Profile;
            choices.Add(item.Name,combo);
            list.Controls.Add(combo,1,i);
        }
        root.Controls.Add(list,0,1);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var use=new Button{Text="選択を反映",Width=145,Height=36};
        use.Click+=(s,e)=>{
            SelectedProfiles=profiles.Select(x=>new ControllerProfile{Name=x.Name,
                Profile=(string)choices[x.Name].SelectedItem}).ToList();
            DialogResult=DialogResult.OK;Close();
        };
        var cancel=new Button{Text="戻る",DialogResult=DialogResult.Cancel,Width=100,Height=36};
        actions.Controls.Add(use);actions.Controls.Add(cancel);
        root.Controls.Add(actions,0,2);
        AcceptButton=use;CancelButton=cancel;
    }
}
