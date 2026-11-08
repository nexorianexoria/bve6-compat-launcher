using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

// Edit BVE's existing preferences without launching BVE. Keep the game itself
// untouched; all changes are explicitly chosen by the user and backed up.
internal sealed class BveOptionsDialog : Form {
    private readonly ComboBox versionChoice=new ComboBox();
    private readonly ComboBox screenChoice=new ComboBox();
    private readonly CheckBox windowed=new CheckBox();
    private readonly CheckBox alwaysTop=new CheckBox(),backgroundAudio=new CheckBox();
    private readonly NumericUpDown width=new NumericUpDown(),height=new NumericUpDown();
    private readonly NumericUpDown front=new NumericUpDown(),back=new NumericUpDown(),filter=new NumericUpDown();
    private readonly CheckedListBox plugins=new CheckedListBox();
    private readonly Label warning=new Label();
    private readonly Button save=new Button();
    private readonly Button pluginProperties=new Button();
    private readonly Label selectedPluginStatus=new Label();
    private readonly string bve5,bve6;
    private BvePreferences current;
    private List<ControllerSwitch> controllerItems=new List<ControllerSwitch>();
    private List<ControllerProfile> profileItems=new List<ControllerProfile>();
    private bool loading;
    private sealed class MonitorOption {
        internal string Device,Title;
        public override string ToString(){return Title;}
    }
    private sealed class PluginOption {
        internal string Id;
        internal string Title;
        public override string ToString(){return Title;}
    }
    private static PluginOption PluginInfo(string id){
        string name=id;
        switch((id??"").ToLowerInvariant()){
            case "bveex.caller.inputdevice":name="BveEX";break;
            case "kusaanko.numerouscontrollerinterface":
                name="NumerousControllerInterface";break;
            case "mackoy.tsmcinterface":name="TSマスコン (Mackoy.TsmcInterface)";break;
            case "zkns_001interface":name="ZKNS_001interface";break;
            case "sanyinginput":name="SanYingInput";break;
        }
        return new PluginOption{Id=id,Title=name};
    }

    internal BveOptionsDialog(string executable5,string executable6){
        bve5=executable5;bve6=executable6;
        Text="BVEの画面・軽量化・入力デバイス | 設定";
        Size=new Size(795,675);MinimumSize=new Size(680,570);
        Font=new Font("Yu Gothic UI",10);
        BackColor=Color.FromArgb(245,248,252);
        StartPosition=FormStartPosition.CenterParent;
        var frame=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(18)};
        frame.RowStyles.Add(new RowStyle(SizeType.Absolute,45));
        frame.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        frame.RowStyles.Add(new RowStyle(SizeType.Absolute,53));
        frame.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        Controls.Add(frame);
        var selector=new FlowLayoutPanel{Dock=DockStyle.Fill};
        selector.Controls.Add(new Label{Text="設定対象",AutoSize=true,Padding=new Padding(2,7,8,0)});
        versionChoice.DropDownStyle=ComboBoxStyle.DropDownList;
        versionChoice.Width=130;versionChoice.Items.AddRange(new object[]{"BVE6","BVE5"});
        selector.Controls.Add(versionChoice);
        selector.Controls.Add(new Label{
            Text="BVEを起動せずに変更できます。保存時はBVEを終了してください。",
            AutoSize=true,Padding=new Padding(12,8,0,0),ForeColor=Color.FromArgb(90,107,125)});
        frame.Controls.Add(selector,0,0);
        var tabs=new TabControl{Dock=DockStyle.Fill};
        frame.Controls.Add(tabs,0,1);
        tabs.TabPages.Add(ScreenPage());
        tabs.TabPages.Add(PerformancePage());
        tabs.TabPages.Add(DevicesPage());
        warning.Dock=DockStyle.Fill;warning.TextAlign=ContentAlignment.MiddleLeft;
        warning.ForeColor=Color.FromArgb(145,79,41);
        frame.Controls.Add(warning,0,2);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        save.Text="設定を保存";save.Width=140;save.Height=36;
        save.Click+=(s,e)=>SaveSettings();
        var cancel=new Button{Text="閉じる",Width=95,Height=36,DialogResult=DialogResult.Cancel};
        actions.Controls.Add(save);actions.Controls.Add(cancel);frame.Controls.Add(actions,0,3);
        versionChoice.SelectedIndexChanged+=(s,e)=>LoadSettings();
        versionChoice.SelectedIndex=0;
        Shown+=(s,e)=>RefreshRunningState();
        Activated+=(s,e)=>RefreshRunningState();
    }
    private static Control Caption(string text) {
        return new Label{Text=text,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};
    }
    private static TableLayoutPanel OptionLayout(){
        var table=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Padding=new Padding(20,16,20,15)};
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        return table;
    }
    private static void Add(TableLayoutPanel layout,int row,string text,Control content,int height=47){
        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        layout.Controls.Add(Caption(text),0,row);
        content.Dock=DockStyle.Fill;
        layout.Controls.Add(content,1,row);
    }
    private static TabPage Page(string title,Control control){
        var page=new TabPage(title){Padding=new Padding(3)};
        page.Controls.Add(control);return page;
    }
    private TabPage ScreenPage(){
        var panel=OptionLayout();
        windowed.Text="ウィンドウ表示で起動";windowed.AutoSize=true;
        Add(panel,0,"表示方法",windowed);
        var size=new FlowLayoutPanel{Dock=DockStyle.Fill};
        width.Minimum=640;width.Maximum=7680;width.Width=108;
        height.Minimum=400;height.Maximum=4320;height.Width=108;
        size.Controls.Add(width);
        size.Controls.Add(new Label{Text="×",AutoSize=true,Padding=new Padding(7,7,7,0)});
        size.Controls.Add(height);
        Add(panel,1,"ウィンドウサイズ",size);
        screenChoice.DropDownStyle=ComboBoxStyle.DropDownList;
        Add(panel,2,"起動するモニター",screenChoice);
        alwaysTop.Text="BVEの運転画面を最前面に固定";
        alwaysTop.AutoSize=true;
        Add(panel,3,"最前面表示",alwaysTop);
        backgroundAudio.Text="別ウィンドウ操作中も音声を維持（DSOAL・試験機能）";
        backgroundAudio.AutoSize=true;
        backgroundAudio.Enabled=false;
        Add(panel,4,"バックグラウンド音声",backgroundAudio);
        var note=new Label{Text="最前面固定はウィンドウ表示でのみ有効です。ほかのアプリをクリックしてもBVEに入力フォーカスは戻しません。\nバックグラウンド音声はBVE本体のコピーと検証済みDSOALを使用する試験機能です。DSOALがない場合は選べません。BVE6で一例確認、BVE5・各プラグインの動作は未確認です。",
            Dock=DockStyle.Fill,ForeColor=Color.FromArgb(100,115,135),TextAlign=ContentAlignment.MiddleLeft};
        Add(panel,5,"",note,110);
        windowed.CheckedChanged+=(s,e)=>{screenChoice.Enabled=windowed.Checked;alwaysTop.Enabled=windowed.Checked;};
        return Page("画面",panel);
    }
    private TabPage PerformancePage(){
        var panel=OptionLayout();
        front.Minimum=100;front.Maximum=10000;front.Increment=100;front.Width=118;
        back.Minimum=0;back.Maximum=5000;back.Increment=10;back.Width=118;
        filter.Minimum=1;filter.Maximum=16;filter.Width=118;
        Add(panel,0,"前方の描画距離",front);
        Add(panel,1,"後方の描画距離",back);
        Add(panel,2,"異方性フィルター",filter);
        var presets=new FlowLayoutPanel{Dock=DockStyle.Fill};
        var lite=new Button{Text="軽量",Width=100,Height=32};
        lite.Click+=(s,e)=>{front.Value=400;back.Value=30;filter.Value=2;};
        var balance=new Button{Text="バランス",Width=110,Height=32};
        balance.Click+=(s,e)=>{front.Value=800;back.Value=50;filter.Value=8;};
        var quality=new Button{Text="画質優先",Width=110,Height=32};
        quality.Click+=(s,e)=>{front.Value=1200;back.Value=100;filter.Value=16;};
        var undo=new Button{Text="元の値に戻す",Width=125,Height=32};
        undo.Click+=(s,e)=>{if(current!=null){
            front.Value=Math.Min(front.Maximum,Math.Max(front.Minimum,current.DrawDistance));
            back.Value=Math.Min(back.Maximum,Math.Max(back.Minimum,current.BackDrawDistance));
            filter.Value=Math.Min(filter.Maximum,Math.Max(filter.Minimum,current.Anisotropy));
        }};
        presets.Controls.Add(lite);presets.Controls.Add(balance);
        presets.Controls.Add(quality);presets.Controls.Add(undo);
        Add(panel,3,"かんたん調整",presets,52);
        Add(panel,4,"",new Label{Text="小さい値ほど描画する範囲やフィルターの負荷を減らせます。\n車両・路線によって効果は変わるため、FPS向上は保証されません。\nBVE本体のプログラムや車両データは変更しません。",
            ForeColor=Color.FromArgb(100,115,135),Dock=DockStyle.Fill,
            TextAlign=ContentAlignment.MiddleLeft},110);
        return Page("軽量化・描画",panel);
    }
    private TabPage DevicesPage(){
        // Mirror BVE's usual interaction: first check a plugin to enable it,
        // then select its row and click "Properties..." for its own settings.
        var parent=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,
            RowCount=5,Padding=new Padding(15)};
        parent.RowStyles.Add(new RowStyle(SizeType.Absolute,37));
        parent.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        parent.RowStyles.Add(new RowStyle(SizeType.Absolute,50));
        parent.RowStyles.Add(new RowStyle(SizeType.Absolute,58));
        parent.RowStyles.Add(new RowStyle(SizeType.Absolute,33));
        parent.Controls.Add(new Label{
            Text="追加の入力デバイス（チェックで有効／無効）",
            Dock=DockStyle.Fill,Font=new Font(Font,FontStyle.Bold),
            TextAlign=ContentAlignment.MiddleLeft},0,0);
        // Like BVE's native list, selecting a row for Properties must not
        // accidentally toggle the input device on/off.
        plugins.CheckOnClick=false;
        plugins.Dock=DockStyle.Fill;
        plugins.IntegralHeight=false;
        plugins.SelectedIndexChanged+=(s,e)=>RefreshPluginProperties();
        plugins.ItemCheck+=(s,e)=>{
            if(!loading && IsHandleCreated)
                BeginInvoke((Action)(()=>RefreshPluginProperties()));
        };
        parent.Controls.Add(plugins,0,1);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        pluginProperties.Text="プロパティー…";
        pluginProperties.Width=170;pluginProperties.Height=34;
        pluginProperties.Enabled=false;
        pluginProperties.Click+=(s,e)=>OpenSelectedPluginProperties();
        buttons.Controls.Add(pluginProperties);
        parent.Controls.Add(buttons,0,2);
        selectedPluginStatus.Dock=DockStyle.Fill;
        selectedPluginStatus.ForeColor=Color.FromArgb(91,108,129);
        selectedPluginStatus.TextAlign=ContentAlignment.MiddleLeft;
        parent.Controls.Add(selectedPluginStatus,0,3);
        parent.Controls.Add(new Label{
            Text="BVEと同様に、一覧の項目を選んでから「プロパティー…」を開きます。",
            ForeColor=Color.FromArgb(91,108,129),
            Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,4);
        return Page("追加入力デバイス",parent);
    }
    private static bool IsNumerousPlugin(string id){
        return String.Equals(id,"kusaanko.numerouscontrollerinterface",
            StringComparison.OrdinalIgnoreCase);
    }
    private void RefreshPluginProperties(){
        var plugin=(plugins.SelectedItem as PluginOption)?.Id;
        bool known=IsNumerousPlugin(plugin);
        pluginProperties.Enabled=known && !loading &&
            (controllerItems.Count>0 || profileItems.Count>0);
        selectedPluginStatus.Text=String.IsNullOrWhiteSpace(plugin)?
            "一覧の入力プラグインを選択してください。":
            known?
                pluginProperties.Enabled?
                    "NumerousControllerInterface：マスコンの有効／無効・使用プロファイルを設定できます。":
                    "NumerousControllerInterfaceの設定ファイルがありません。":
                "このプラグインの独自プロパティー画面はランチャーでの操作に未対応です。";
    }
    private void OpenSelectedPluginProperties(){
        if(loading || !pluginProperties.Enabled)return;
        if(!IsNumerousPlugin((plugins.SelectedItem as PluginOption)?.Id))return;
        using(var editor=new NumerousControllerPropertiesDialog(controllerItems,profileItems)){
            if(editor.ShowDialog(this)==DialogResult.OK){
                controllerItems=editor.SelectedControllers;
                profileItems=editor.SelectedProfiles;
                warning.Text="マスコンの変更は、下の「設定を保存」を押すと反映されます。";
            }
        }
    }
    private void RefreshRunningState(){
        if(loading)return;
        bool running=BvePreferencesService.IsGameRunning();
        warning.Text=running?
            "BVEが起動中です。現在のゲーム設定を守るため、終了後に保存できます。":
            "保存時に元の設定ファイルをバックアップします。変更は次回のBVE起動から反映されます。";
        save.Enabled=!running && current!=null;
    }
    private void LoadSettings(){
        if(versionChoice.SelectedIndex<0)return;
        loading=true;
        try{
            int version=versionChoice.SelectedIndex==0?6:5;
            current=BvePreferencesService.Read(version);
            var launcherWindow=BveWindowOptions.Read(version);
            windowed.Checked=current.Windowed;
            alwaysTop.Checked=launcherWindow.AlwaysOnTop;
            backgroundAudio.Checked=launcherWindow.BackgroundAudio;
            backgroundAudio.Enabled=BveBackgroundAudio.HasTrustedRuntime(version);
            width.Value=Math.Min(width.Maximum,Math.Max(width.Minimum,current.Width));
            height.Value=Math.Min(height.Maximum,Math.Max(height.Minimum,current.Height));
            front.Value=Math.Min(front.Maximum,Math.Max(front.Minimum,current.DrawDistance));
            back.Value=Math.Min(back.Maximum,Math.Max(back.Minimum,current.BackDrawDistance));
            filter.Value=Math.Min(filter.Maximum,Math.Max(filter.Minimum,current.Anisotropy));
            screenChoice.Items.Clear();
            screenChoice.Items.Add(new MonitorOption{Title="BVEに任せる",Device=""});
            var chosen=BveLaunchDisplay.Read(version);
            int selected=0,index=0;
            foreach(var monitor in WindowsMonitorCatalog.GetCurrent()){
                index++;
                screenChoice.Items.Add(new MonitorOption{Device=monitor.Device,
                    Title=monitor.Label});
                if(String.Equals(monitor.Device,chosen,StringComparison.OrdinalIgnoreCase))
                    selected=index;
            }
            screenChoice.SelectedIndex=selected;
            screenChoice.Enabled=windowed.Checked;
            alwaysTop.Enabled=windowed.Checked;
            plugins.Items.Clear();
            string executable=version==6?bve6:bve5;
            foreach(string plugin in BvePreferencesService.DiscoverPlugins(executable,current.InputPlugins))
                plugins.Items.Add(PluginInfo(plugin),
                    current.InputPlugins.Contains(plugin,StringComparer.OrdinalIgnoreCase));
            if(plugins.Items.Count>0){
                var firstChecked=plugins.CheckedIndices.Count>0?plugins.CheckedIndices[0]:0;
                plugins.SelectedIndex=firstChecked;
            }
            controllerItems=NumerousControllerSettings.Read(NumerousControllerSettings.PathOnDisk);
            profileItems=NumerousControllerSettings.ReadProfiles(NumerousControllerSettings.PathOnDisk);
        }catch(Exception ex){
            current=null;warning.Text="設定を読み込めませんでした: "+ex.Message;
            MessageBox.Show(this,ex.Message,"設定の読み込み",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }finally{loading=false;RefreshRunningState();RefreshPluginProperties();}
    }
    private void SaveSettings(){
        try{
            if(BvePreferencesService.IsGameRunning())throw new InvalidOperationException(
                "BVEを終了してから保存してください。");
            int version=versionChoice.SelectedIndex==0?6:5;
            var selected=plugins.CheckedItems.Cast<PluginOption>().Select(x=>x.Id).ToList();
            var active=new HashSet<string>(selected,StringComparer.OrdinalIgnoreCase);
            // Preserve the original load order of currently enabled input
            // plugins. Changing their order can affect some controllers.
            var ordered=current.InputPlugins.Where(x=>active.Contains(x)).ToList();
            foreach(var name in selected)
                if(!ordered.Contains(name,StringComparer.OrdinalIgnoreCase))ordered.Add(name);
            var updated=new BvePreferences {
                Windowed=windowed.Checked,Width=(int)width.Value,Height=(int)height.Value,
                DrawDistance=(int)front.Value,BackDrawDistance=(int)back.Value,
                Anisotropy=(int)filter.Value,
                InputPlugins=ordered
            };
            BvePreferencesService.Validate(updated);
            string target=(screenChoice.SelectedItem as MonitorOption)?.Device??"";
            if(!updated.Windowed)target="";
            // Only change settings in response to the explicit Save button.
            // Save each source file with a timestamped backup.
            BvePreferencesService.Save(version,updated);
            if(controllerItems.Count>0 || profileItems.Count>0)
                NumerousControllerSettings.SaveAll(
                    NumerousControllerSettings.PathOnDisk,controllerItems,profileItems);
            BveLaunchDisplay.Save(version,target);
            BveWindowOptions.Save(version,new BveWindowOptions.Settings{
                AlwaysOnTop=updated.Windowed && alwaysTop.Checked,
                BackgroundAudio=backgroundAudio.Enabled && backgroundAudio.Checked
            });
            MessageBox.Show(this,"設定を保存しました。次のBVE起動で反映されます。"+
                "\nBVEの設定ファイルはバックアップ済みです。","設定を保存",
                MessageBoxButtons.OK,MessageBoxIcon.Information);
            LoadSettings();
        }catch(Exception ex){
            Diagnostics.Write("BVE_OPTIONS_SAVE",ex.Message);
            MessageBox.Show(this,"設定を保存できませんでした。\n"+ex.Message+
                "\n一部保存済みの場合は設定ファイルのバックアップから復元できます。",
                "設定の保存",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
    }
}
