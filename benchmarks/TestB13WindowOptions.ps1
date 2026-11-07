param([string]$LauncherPath='')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
$exe=if([string]::IsNullOrWhiteSpace($LauncherPath)){
    Join-Path $root 'dist-b13-perf/window-options-ui/BVE6互換ランチャー.exe'
}else{[IO.Path]::GetFullPath($LauncherPath)}
if(!(Test-Path -LiteralPath $exe)){throw 'B13 window-options UI build missing'}
$assembly=[Reflection.Assembly]::LoadFrom($exe)
$flags=[Reflection.BindingFlags]'Instance,Static,Public,NonPublic'
$owner=$assembly.GetType('BveWindowOptions',$true)
$settingsType=$assembly.GetType('BveWindowOptions+Settings',$true)
$read=$owner.GetMethod('ReadFile',$flags)
$save=$owner.GetMethod('SaveFile',$flags)
$topmost=$owner.GetMethod('SetTopmost',$flags)
$scratch=Join-Path $root 'benchmarks/results/window-options-regression.xml'
if(Test-Path $scratch){Remove-Item -LiteralPath $scratch -Force}
$originalSettings=Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'BveTs/Settings/BveTs6.Preferences.xml'
$before=(Get-FileHash -LiteralPath $originalSettings).Hash
function Option([bool]$top,[bool]$audio){
    $value=[Activator]::CreateInstance($settingsType,$true)
    $settingsType.GetField('AlwaysOnTop',$flags).SetValue($value,$top)
    $settingsType.GetField('BackgroundAudio',$flags).SetValue($value,$audio)
    return $value
}
function Check([int]$version,[bool]$top,[bool]$audio){
    $value=$read.Invoke($null,[object[]]@([string]$scratch,$version))
    if([bool]$settingsType.GetField('AlwaysOnTop',$flags).GetValue($value) -ne $top -or
       [bool]$settingsType.GetField('BackgroundAudio',$flags).GetValue($value) -ne $audio){
        throw ('Window options mismatch for BVE'+$version)
    }
}
Check 5 $false $false
Check 6 $false $false
$save.Invoke($null,[object[]]@([string]$scratch,6,(Option $true $false)))|Out-Null
$save.Invoke($null,[object[]]@([string]$scratch,5,(Option $false $true)))|Out-Null
Check 6 $true $false
Check 5 $false $true
$save.Invoke($null,[object[]]@([string]$scratch,6,(Option $true $true)))|Out-Null
Check 6 $true $true
$save.Invoke($null,[object[]]@([string]$scratch,6,(Option $false $false)))|Out-Null
Check 6 $false $false
Check 5 $false $true
Write-Output 'B13_WINDOW_CHECKBOX_FLAGS_INDEPENDENT_AND_PERSISTED'

$getTopmost=$owner.GetMethod('IsTopmost',$flags)
$form=New-Object System.Windows.Forms.Form
try{
    $handle=$form.Handle
    if(![bool]$topmost.Invoke($null,[object[]]@($handle,$true))){
        throw 'Could not make a synthetic window topmost'
    }
    if(![bool]$getTopmost.Invoke($null,[object[]]@($handle))){
        throw 'WS_EX_TOPMOST not set'
    }
    if(![bool]$topmost.Invoke($null,[object[]]@($handle,$false))){
        throw 'Could not restore a synthetic window'
    }
    if([bool]$getTopmost.Invoke($null,[object[]]@($handle))){
        throw 'WS_EX_TOPMOST not cleared'
    }
    Write-Output 'B13_SYNTHETIC_WINDOW_TOPMOST_ON_OFF_OK'
}finally{$form.Dispose()}

$dialogType=$assembly.GetType('BveOptionsDialog',$true)
$constructor=$dialogType.GetConstructor($flags,$null,[Type[]]@([string],[string]),$null)
$dialog=$constructor.Invoke([object[]]@([string]'C:\invalid\BveTs5.exe',[string]'C:\invalid\BveTs6.exe'))
try{
    $a=$dialogType.GetField('alwaysTop',$flags).GetValue($dialog)
    $b=$dialogType.GetField('backgroundAudio',$flags).GetValue($dialog)
    if($a -isnot [Windows.Forms.CheckBox] -or $b -isnot [Windows.Forms.CheckBox]){
        throw 'Both window options must be checkboxes'
    }
    # The UI must honor an existing user's topmost preference. An isolated
    # empty-config check above verifies the actual default is off. Never
    # overwrite or assume the state of the user's live preference file.
    $readLive=$owner.GetMethod('Read',$flags)
    $current=$readLive.Invoke($null,[object[]]@(6))
    $savedTopmost=[bool]$settingsType.GetField('AlwaysOnTop',$flags).GetValue($current)
    if($a.Checked -ne $savedTopmost){throw 'The UI did not load the saved topmost preference'}
    if($b.Checked){throw 'The unimplemented audio option must be unchecked'}
    if($b.Enabled){throw 'Unverified background audio must not be enabled'}
    if(!$a.Text.Contains('最前面') -or !$b.Text.Contains('音声')){
        throw 'Checkbox descriptions are missing'
    }
    Write-Output 'B13_BOTH_UI_CHECKBOXES_PRESENT_AUDIO_SAFELY_DISABLED'
}finally{$dialog.Dispose()}

if((Get-FileHash -LiteralPath $originalSettings).Hash -ne $before){
    throw 'Live BVE preferences changed'
}
if(@(Get-Process -Name BveTs -ErrorAction SilentlyContinue).Count){
    Write-Output 'BVE_IS_RUNNING_NO_LIVE_TEST_WAS_PERFORMED'
}
Write-Output 'B13_NATIVE_BVE_SETTINGS_UNCHANGED'
