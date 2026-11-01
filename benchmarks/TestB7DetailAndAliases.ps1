$ErrorActionPreference='Stop'
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
$exe=Join-Path $root 'dist-b7-cards\BVE6互換ランチャー.exe'
if(!(Test-Path $exe)){throw 'Build b7 launcher first'}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$assembly=[Reflection.Assembly]::LoadFrom($exe)
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$alias=$assembly.GetType('RouteDisplayNames',$true)
$detail=$assembly.GetType('ScenarioDetailDialog',$true)
$preview=$assembly.GetType('Previews',$true)
$launcherType=$assembly.GetType('Launcher',$true)
$program=$assembly.GetType('Program',$true)
$scratch=Join-Path $root ('benchmarks\b7-ui-test-'+[guid]::NewGuid().ToString('N'))
$enc=[Text.Encoding]::GetEncoding(932)
function Invoke-Method($type,$name,$arguments){
    $method=@($type.GetMethods($flags)|Where-Object{$_.Name -eq $name -and $_.GetParameters().Count -eq $arguments.Count})
    if($method.Count -ne 1){throw ('Ambiguous test method: '+$name)}
    return $method[0].Invoke($null,[object[]]$arguments)
}
function Controls-OfType($control,$type){
    foreach($child in $control.Controls){
        if($type.IsAssignableFrom($child.GetType())){$child}
        Controls-OfType $child $type
    }
}
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $alias.GetField('TestFilePath',$flags).SetValue($null,(Join-Path $scratch 'names.xml'))
    $null=Invoke-Method $alias 'ReloadForTest' @()
    $routeA=Join-Path $scratch 'route-a.map'
    $routeB=Join-Path $scratch 'route-b.map'
    $vehicleA=Join-Path $scratch 'vehicle-a.txt'
    $vehicleB=Join-Path $scratch 'vehicle-b.txt'
    $panel=Join-Path $scratch 'panel.txt'
    $image=Join-Path $scratch 'daytime.png'
    $scenario=Join-Path $scratch 'demo.txt'
    [IO.File]::WriteAllText($routeA,'test-a',$enc)
    [IO.File]::WriteAllText($routeB,'test-b',$enc)
    [IO.File]::WriteAllText($vehicleA,"BveTs Vehicle 2.00:shift_jis`r`nPanel = panel.txt`r`n",$enc)
    [IO.File]::WriteAllText($vehicleB,"BveTs Vehicle 2.00:shift_jis`r`n",$enc)
    [IO.File]::WriteAllText($panel,"[This]`r`nDaytimeImage = daytime.png`r`n",$enc)
    [IO.File]::WriteAllText($scenario,"BveTs Scenario 2.00:shift_jis`r`nRoute = route-a.map * 1 | route-b.map * 1`r`nVehicle = vehicle-a.txt * 1 | vehicle-b.txt * 1`r`n",$enc)
    $bitmap=New-Object Drawing.Bitmap(300,120)
    try{
        $g=[Drawing.Graphics]::FromImage($bitmap)
        try{$g.Clear([Drawing.Color]::Aqua)}finally{$g.Dispose()}
        $bitmap.Save($image,[Drawing.Imaging.ImageFormat]::Png)
    }finally{$bitmap.Dispose()}
    $before=(Get-FileHash -LiteralPath $routeA -Algorithm SHA256).Hash
    $resolve=Invoke-Method $preview 'Resolve' @([string]$vehicleA,$true)
    if($resolve -ne $image){throw 'Panel screenshot is not resolved from vehicle definition'}
    $namesFile=Join-Path $scratch 'names.xml'
    if([bool]$alias.GetProperty('Enabled',$flags).GetValue($null)){throw 'Alias setting must be off by default'}
    $null=Invoke-Method $alias 'SetEnabled' @($true)
    $null=Invoke-Method $alias 'SetName' @([string]$routeA,[string]'テスト1')
    $null=Invoke-Method $alias 'ReloadForTest' @()
    if(![bool]$alias.GetProperty('Enabled',$flags).GetValue($null)){throw 'Enabled setting did not survive restart'}
    if((Invoke-Method $alias 'GetName' @([string]$routeA)) -ne 'テスト1'){throw 'Custom name did not survive restart'}
    if((Invoke-Method $alias 'Display' @([string]$routeA)) -notmatch 'テスト1'){throw 'Custom name not displayed'}
    $ctor=$detail.GetConstructor([Reflection.BindingFlags]'Public,NonPublic,Instance',$null,[Type[]]@([string]),$null)
    $form=$ctor.Invoke([object[]]@([string]$scenario))
    try{
        $lists=@(Controls-OfType $form ([Windows.Forms.ListBox]))
        $cards=@(Controls-OfType $form ($assembly.GetType('CatalogView',$true)))
        if($lists.Count -ne 1 -or $cards.Count -ne 1){throw 'Expected route list and regular vehicle card gallery'}
        $routes=$lists[0];$vehicles=$cards[0]
        if($routes.Items.Count -ne 3 -or $vehicles.ItemCount -ne 2){throw 'Candidate rows missing'}
        if($vehicles.SelectedItem -ne $null){throw 'Vehicle should start as random / unselected'}
        if($routes.Items[1].ToString().Contains('重み')){throw 'Unnecessary candidate weight label still appears'}
        $vehicleDefaults=@(Controls-OfType $form ([Windows.Forms.Button]) | Where-Object { $_.Text -match '指定しない' })
        if($vehicleDefaults.Count -ne 1 -or $vehicleDefaults[0].Text -ne '指定しない（ランダム）'){throw 'Random selection reset button missing'}
        if($routes.Items[1].ToString() -notmatch 'テスト1'){throw 'Saved route name missing from selection screen'}
        $editor=$detail.GetField('routeEditorPanel',$flags).GetValue($form)
        $routeColumn=$detail.GetField('routeColumn',$flags).GetValue($form)
        if($routeColumn.RowStyles[3].Height -ne 0){throw 'Name editor was allocated space before route selection'}
        $routes.SelectedIndex=1
        if($routeColumn.RowStyles[3].Height -ne 186){throw 'Name editor did not allocate space after choosing a route'}
        $input=$detail.GetField('routeName',$flags).GetValue($form)
        if($input.Text -ne 'テスト1'){throw 'Stored route name not restored in editor'}
        $input.Text='変更後'
        $button=$detail.GetField('saveRouteName',$flags).GetValue($form)
        $null=$detail.GetMethod('SaveRouteName',$flags).Invoke($form,[object[]]@())
        if((Invoke-Method $alias 'GetName' @([string]$routeA)) -ne '変更後'){throw 'Saving edited route name failed'}
        $input.Text=''
        $null=$detail.GetMethod('SaveRouteName',$flags).Invoke($form,[object[]]@())
        if(Invoke-Method $alias 'GetName' @([string]$routeA)){throw 'Clearing name did not restore default'}
        $items=@($assembly.GetType('CatalogView',$true).GetField('items',$flags).GetValue($vehicles))
        $vehicleWithPanel=$items|Where-Object { $_.Entry.Path -eq $vehicleA }|Select-Object -First 1
        $vehicleWithoutPanel=$items|Where-Object { $_.Entry.Path -eq $vehicleB }|Select-Object -First 1
        if($null -eq $vehicleWithPanel -or $null -eq $vehicleWithoutPanel){throw 'Vehicle gallery candidates missing'}
        if($vehicleWithPanel.ImagePath -ne $image){throw 'Normal vehicle gallery did not resolve the selected panel image'}
        if($vehicleWithoutPanel.ImagePath){throw 'Vehicle without panel falsely reused an unrelated panel image'}
        if(!$vehicles.SelectPath($vehicleA)){throw 'Selecting an image-backed vehicle failed'}
        if($vehicles.SelectedItem.Entry.Path -ne $vehicleA){throw 'Card selection did not select desired vehicle'}
        # Invoke the wired button click even though the test dialog stays hidden.
        [Windows.Forms.Control].GetMethod('OnClick',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($vehicleDefaults[0],[object[]]@([EventArgs]::Empty)) | Out-Null
        if($null -ne $vehicles.SelectedItem){throw 'Reset did not restore random vehicle choice'}
    }finally{$form.Dispose()}
    $null=Invoke-Method $alias 'SetName' @([string]$routeA,[string]'テスト1')
    $null=Invoke-Method $alias 'SetEnabled' @($false)
    $null=Invoke-Method $alias 'ReloadForTest' @()
    if([bool]$alias.GetProperty('Enabled',$flags).GetValue($null)){throw 'Disabled state not saved'}
    if((Invoke-Method $alias 'Display' @([string]$routeA)) -match 'テスト1'){throw 'Names still displayed when setting is off'}
    if((Invoke-Method $alias 'GetName' @([string]$routeA)) -ne 'テスト1'){throw 'Turning off deleted saved name'}
    if((Get-FileHash -LiteralPath $routeA -Algorithm SHA256).Hash -ne $before){throw 'Original route file changed'}
    $program.GetField('Home',$flags).SetValue($null,(Join-Path $root 'dist-b7-cards'))
    $program.GetField('Data',$flags).SetValue($null,(Join-Path $scratch 'work'))
    $main=$launcherType.GetConstructor([Reflection.BindingFlags]'Public,NonPublic,Instance',$null,[Type[]]@([bool]),$null).Invoke([object[]]@($false))
    try{
        $detailButton=$launcherType.GetField('detailSelectionButton',$flags).GetValue($main)
        if($detailButton.Text -ne 'ルート設定' -or $detailButton.Enabled){throw 'Main detail button label or default state wrong'}
        if($main.Text -ne 'BVE 起動ランチャー Ver.0.29b7'){throw 'Window title contains an unrequested version label'}
        $headerLabels=@(Controls-OfType $main ([Windows.Forms.Label]) | Where-Object { $_.Text -like 'Ver.*' })
        if($headerLabels.Count -ne 1 -or $headerLabels[0].Text -ne 'Ver.0.29b7'){throw 'Header must show only the version number'}
        $settings=$launcherType.GetMethod('CreateSettings',$flags).Invoke($main,[object[]]@())
        try{
            $checks=@(Controls-OfType $settings ([Windows.Forms.CheckBox]))
            if($checks.Count -ne 1 -or $checks[0].Text -ne 'ルート名を自分用に設定'){throw 'Route alias checkbox missing'}
        }finally{$settings.Dispose()}
    }finally{$main.Dispose()}
    Write-Output 'B7_ALIAS_PANEL_PREVIEW_UI_TESTS_OK'
}finally{
    $null=Invoke-Method $alias 'ReloadForTest' @()
    $alias.GetField('TestFilePath',$flags).SetValue($null,$null)
    if(Test-Path $scratch){Remove-Item -LiteralPath $scratch -Recurse -Force}
}
