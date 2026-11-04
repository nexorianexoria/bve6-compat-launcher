$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'dist-b11-input-display/BVE6互換ランチャー.exe'))
$flags=[Reflection.BindingFlags]'NonPublic,Public,Instance,Static'
$display=$assembly.GetType('WindowsMonitorCatalog',$true)
$list=$display.GetMethod('GetCurrent',$flags).Invoke($null,@())
$model=$assembly.GetType('WindowsMonitorInfo',$true)
if($list.Count -ne [Windows.Forms.Screen]::AllScreens.Count){
    throw 'Connected monitor count differs from Windows'
}
$registered=@(Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorID |
    Where-Object Active | ForEach-Object {
        [Text.Encoding]::ASCII.GetString(
            [byte[]](@($_.UserFriendlyName)|Where-Object{$_ -ne 0}))
    })
$labels=@()
foreach($item in $list){
    $name=[string]$model.GetField('Name',$flags).GetValue($item)
    $width=[int]$model.GetField('Width',$flags).GetValue($item)
    $height=[int]$model.GetField('Height',$flags).GetValue($item)
    $hz=[int]$model.GetField('RefreshHz',$flags).GetValue($item)
    $label=[string]$model.GetProperty('Label',$flags).GetValue($item)
    if($width -lt 640 -or $height -lt 400 -or $hz -le 0){
        throw ('Invalid current Windows display resolution/refresh: '+$label)
    }
    if($registered.Count -ge $list.Count -and $registered -notcontains $name){
        throw ('Windows registered monitor name did not match: '+$name)
    }
    if(!$label.Contains($name) -or !$label.Contains(([string]$hz+' Hz'))){
        throw ('Monitor UI does not show its registered name and refresh: '+$label)
    }
    $labels+=$label
}
Write-Output 'B11_WINDOWS_MONITOR_FRIENDLY_NAMES_RESOLUTION_HZ_OK'
$labels|ForEach-Object{Write-Output ('MONITOR '+$_)}

$controllerPath=Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'BveTs/Settings/Kusaanko.NumerousControllerInterface.xml'
$bvePath=Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'BveTs/Settings/BveTs6.Preferences.xml'
$beforeController=(Get-FileHash $controllerPath).Hash
$beforeBve=(Get-FileHash $bvePath).Hash
$settingsType=$assembly.GetType('NumerousControllerSettings',$true)
$controllers=$settingsType.GetMethod('Read',$flags).Invoke($null,[object[]]@([string]$controllerPath))
$profiles=$settingsType.GetMethod('ReadProfiles',$flags).Invoke($null,[object[]]@([string]$controllerPath))
$pluginDialogType=$assembly.GetType('NumerousControllerPropertiesDialog',$true)
$ctor=$pluginDialogType.GetConstructors($flags)|Select-Object -First 1
$args=New-Object 'object[]' 2
$args[0]=$controllers
$args[1]=$profiles
$editor=$ctor.Invoke($args)
try{
    $deviceList=$pluginDialogType.GetField('deviceList',$flags).GetValue($editor)
    $enabled=$pluginDialogType.GetField('enabled',$flags).GetValue($editor)
    $profile=$pluginDialogType.GetField('profile',$flags).GetValue($editor)
    if($deviceList.Items.Count -lt 2){throw 'Controller properties list is empty'}
    $selected=[string]$deviceList.SelectedItem
    if($selected -ne 'MultiTrainController P5B5'){throw 'Expected saved P5B5 controller to be selected'}
    if(!$enabled.Enabled -or !$enabled.Checked -or !$profile.Enabled){
        throw 'Saved P5B5 enabled/profile controls not reflected in properties'
    }
    $beforeEnabled=$enabled.Checked
    $enabled.Checked=!$enabled.Checked
    if($enabled.Checked -eq $beforeEnabled){throw 'Controller enable toggle not operable'}
    $deviceList.SelectedIndex=0
    $deviceList.SelectedItem='MultiTrainController P5B5'
    if($enabled.Checked -eq $beforeEnabled){throw 'Controller enable change not retained within editor'}
}finally{$editor.Dispose()}
Write-Output 'B11_BVE_STYLE_PLUGIN_PROPERTIES_AND_CONTROLLER_TOGGLE_OK'

$optionsType=$assembly.GetType('BveOptionsDialog',$true)
$args=New-Object 'object[]' 2
$args[0]='C:\Program Files (x86)\mackoy\BveTs5\BveTs.exe'
$args[1]='C:\Program Files\mackoy\BveTs6\BveTs.exe'
$options=$optionsType.GetConstructors($flags)|Select-Object -First 1
$form=$options.Invoke($args)
try{
    $screenChoice=$optionsType.GetField('screenChoice',$flags).GetValue($form)
    $plugins=$optionsType.GetField('plugins',$flags).GetValue($form)
    $properties=$optionsType.GetField('pluginProperties',$flags).GetValue($form)
    if($plugins.CheckOnClick){throw 'Selecting a plugin row unexpectedly toggles its enabled status'}
    if($screenChoice.Items.Count -ne $list.Count+1){
        throw 'Monitor selector count does not match connected displays'
    }
    foreach($label in $labels){
        if(!@($screenChoice.Items | ForEach-Object ToString).Contains($label)){
            throw ('Friendly monitor label not present in BVE options: '+$label)
        }
    }
    $pluginNames=@($plugins.Items|ForEach-Object ToString)
    if($pluginNames -notcontains 'NumerousControllerInterface'){
        throw 'NumerousControllerInterface is not shown with a friendly name'
    }
    $plugins.SelectedIndex=[Array]::IndexOf($pluginNames,'NumerousControllerInterface')
    if(!$properties.Enabled -or $properties.Text -notmatch 'プロパティー'){
        throw 'BVE-style Properties button cannot open the selected controller plugin'
    }
    Write-Output 'B11_BVE_STYLE_DEVICE_LIST_AND_PROPERTIES_BUTTON_OK'
}finally{$form.Dispose()}
if((Get-FileHash $controllerPath).Hash -ne $beforeController -or
    (Get-FileHash $bvePath).Hash -ne $beforeBve){
    throw 'Real BVE preferences or controller settings were changed by UI tests'
}
Write-Output 'B11_REAL_BVE_AND_DEVICE_SETTINGS_UNCHANGED'
