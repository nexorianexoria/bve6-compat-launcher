using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// Mirrors BVE's "select an input plugin -> Properties..." workflow while
// editing only NumerousControllerInterface's already registered settings.
internal sealed class NumerousControllerPropertiesDialog : Form {
    private readonly ListBox deviceList=new ListBox();
    private readonly CheckBox enabled=new CheckBox();
    private readonly ComboBox profile=new ComboBox();
    private readonly Label availability=new Label();
    private readonly List<ControllerSwitch> deviceValues;
    private readonly List<ControllerProfile> profileValues;
    private bool updating;

    internal List<ControllerSwitch> SelectedControllers {get;private set;}
    internal List<ControllerProfile> SelectedProfiles {get;private set;}

    internal NumerousControllerPropertiesDialog(
        IEnumerable<ControllerSwitch> controllers,
        IEnumerable<ControllerProfile> profiles) {
        deviceValues=(controllers??Enumerable.Empty<ControllerSwitch>())
            .Select(x=>new ControllerSwitch{Name=x.Name,Enabled=x.Enabled}).ToList();
        profileValues=(profiles??Enumerable.Empty<ControllerProfile>())
            .Select(x=>new ControllerProfile{Name=x.Name,Profile=x.Profile}).ToList();
        Text="NumerousControllerInterface のプロパティー";
        Size=new Size(830,525);MinimumSize=new Size(700,455);
        StartPosition=FormStartPosition.CenterParent;
        Font=new Font("Yu Gothic UI",10);
        BackColor=Color.FromArgb(246,248,252);
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,
            Padding=new Padding(17)};
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,39));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,55));
        Controls.Add(root);
        root.Controls.Add(new Label{
            Text="コントローラーを選択して、「有効にする」と使用するプロファイルを設定します。",
            Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);
        var content=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,46));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,54));
        root.Controls.Add(content,0,1);
        deviceList.Dock=DockStyle.Fill;
        deviceList.IntegralHeight=false;
        deviceList.SelectedIndexChanged+=(s,e)=>ShowSelection();
        var left=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2};
        left.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
        left.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        left.Controls.Add(new Label{Text="コントローラー",Dock=DockStyle.Fill,
            Font=new Font(Font,FontStyle.Bold)},0,0);
        left.Controls.Add(deviceList,0,1);
        content.Controls.Add(left,0,0);
        var right=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=6,
            Padding=new Padding(15,6,7,7)};
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,39));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,61));
        right.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        right.Controls.Add(new Label{Text="設定",Dock=DockStyle.Fill,
            Font=new Font(Font,FontStyle.Bold)},0,0);
        enabled.Text="このコントローラーを有効にする";
        enabled.Dock=DockStyle.Fill;
        enabled.CheckedChanged+=(s,e)=>{
            if(updating)return;
            var entry=deviceValues.FirstOrDefault(x=>x.Name==SelectedName());
            if(entry!=null)entry.Enabled=enabled.Checked;
        };
        right.Controls.Add(enabled,0,1);
        right.Controls.Add(new Label{Text="使用するプロファイル",
            Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,2);
        profile.DropDownStyle=ComboBoxStyle.DropDownList;profile.Dock=DockStyle.Fill;
        profile.SelectedIndexChanged+=(s,e)=>{
            if(updating)return;
            var entry=profileValues.FirstOrDefault(x=>x.Name==SelectedName());
            if(entry!=null && profile.SelectedItem is string)
                entry.Profile=(string)profile.SelectedItem;
        };
        right.Controls.Add(profile,0,3);
        availability.Dock=DockStyle.Fill;availability.AutoSize=false;
        availability.ForeColor=Color.FromArgb(103,118,135);
        right.Controls.Add(availability,0,4);
        right.Controls.Add(new Label{
            Text="この画面ではBVEの設定ファイルを変更しません。\n親画面で「設定を保存」を押すと反映されます。",
            ForeColor=Color.FromArgb(103,118,135),
            Dock=DockStyle.Fill},0,5);
        content.Controls.Add(right,1,0);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,
            FlowDirection=FlowDirection.RightToLeft};
        var ok=new Button{Text="変更を反映",Width=143,Height=36};
        ok.Click+=(s,e)=>{
            SelectedControllers=deviceValues.Select(x=>new ControllerSwitch{
                Name=x.Name,Enabled=x.Enabled}).ToList();
            SelectedProfiles=profileValues.Select(x=>new ControllerProfile{
                Name=x.Name,Profile=x.Profile}).ToList();
            DialogResult=DialogResult.OK;Close();
        };
        var cancel=new Button{Text="戻る",Width=104,Height=36,
            DialogResult=DialogResult.Cancel};
        actions.Controls.Add(ok);actions.Controls.Add(cancel);
        root.Controls.Add(actions,0,2);
        AcceptButton=ok;CancelButton=cancel;
        var names=deviceValues.Select(x=>x.Name)
            .Concat(profileValues.Select(x=>x.Name))
            .Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.CurrentCulture)
            .ToList();
        foreach(string name in names)deviceList.Items.Add(name);
        var prefer=names.FindIndex(x=>x=="MultiTrainController P5B5");
        if(names.Count>0)deviceList.SelectedIndex=prefer>=0?prefer:0;
    }
    private string SelectedName(){return deviceList.SelectedItem as string;}
    private void ShowSelection(){
        updating=true;
        try {
            var name=SelectedName();
            var controller=deviceValues.FirstOrDefault(x=>x.Name==name);
            var mapping=profileValues.FirstOrDefault(x=>x.Name==name);
            enabled.Enabled=controller!=null;
            enabled.Checked=controller!=null && controller.Enabled;
            profile.Items.Clear();
            var allowed=profileValues.Select(x=>x.Profile)
                .Distinct(StringComparer.Ordinal).ToArray();
            profile.Items.AddRange(allowed.Cast<object>().ToArray());
            profile.Enabled=mapping!=null;
            if(mapping!=null)profile.SelectedItem=mapping.Profile;
            availability.Text=controller==null?
                "この機種の有効／無効設定は未登録です。\nプロファイルのみ変更できます。":
                mapping==null?
                "この機種のプロファイル設定は未登録です。":
                "現在の設定を表示しています。";
        }finally{updating=false;}
    }
}
