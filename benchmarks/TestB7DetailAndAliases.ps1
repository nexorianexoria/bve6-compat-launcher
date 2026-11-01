$ErrorActionPreference='Stop'
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
$exe=Join-Path $root 'dist-b7-ui\BVE6互換ランチャー.exe'
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
        if($lists.Count -ne 2){throw 'Expected two selection lists'}
        $routes=$lists[0];$vehicles=$lists[1]
        if($routes.Items.Count -ne 3 -or $vehicles.Items.Count -ne 3){throw 'Candidate rows missing'}
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
        $vehicles.SelectedIndex=1
        $picture=$detail.GetField('panelPicture',$flags).GetValue($form)
        for($i=0;$i -lt 60 -and $null -eq $picture.Image;$i++){
            [Windows.Forms.Application]::DoEvents()
            Start-Sleep -Milliseconds 50
        }
        if($null -eq $picture.Image){throw 'Selected vehicle panel preview did not load'}
        if($picture.Image.Width -ne 300 -or $picture.Image.Height -ne 120){throw 'Unexpected selected panel dimensions'}
        $vehicles.SelectedIndex=2
        for($i=0;$i -lt 60;$i++){
            [Windows.Forms.Application]::DoEvents()
            Start-Sleep -Milliseconds 20
        }
        if($picture.Image -ne $null){throw 'Vehicle without panel image retained the previous image'}
    }finally{$form.Dispose()}
    $null=Invoke-Method $alias 'SetName' @([string]$routeA,[string]'テスト1')
    $null=Invoke-Method $alias 'SetEnabled' @($false)
    $null=Invoke-Method $alias 'ReloadForTest' @()
    if([bool]$alias.GetProperty('Enabled',$flags).GetValue($null)){throw 'Disabled state not saved'}
    if((Invoke-Method $alias 'Display' @([string]$routeA)) -match 'テスト1'){throw 'Names still displayed when setting is off'}
    if((Invoke-Method $alias 'GetName' @([string]$routeA)) -ne 'テスト1'){throw 'Turning off deleted saved name'}
    if((Get-FileHash -LiteralPath $routeA -Algorithm SHA256).Hash -ne $before){throw 'Original route file changed'}
    $program.GetField('Home',$flags).SetValue($null,(Join-Path $root 'dist-b7-ui'))
    $program.GetField('Data',$flags).SetValue($null,(Join-Path $scratch 'work'))
    $main=$launcherType.GetConstructor([Reflection.BindingFlags]'Public,NonPublic,Instance',$null,[Type[]]@([bool]),$null).Invoke([object[]]@($false))
    try{
        $detailButton=$launcherType.GetField('detailSelectionButton',$flags).GetValue($main)
        if($detailButton.Text -ne '路線・車両を選ぶ' -or $detailButton.Enabled){throw 'Main detail button label or default state wrong'}
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
