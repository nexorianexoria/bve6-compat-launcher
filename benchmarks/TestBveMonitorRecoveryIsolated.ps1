param([Parameter(Mandatory=$true)][string]$LauncherPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$root=Split-Path -Parent $PSScriptRoot
$research=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'results')).TrimEnd('\')+'\'
$launcher=[IO.Path]::GetFullPath($LauncherPath)
if(!$launcher.StartsWith($research,[StringComparison]::OrdinalIgnoreCase) -or
   !(Test-Path -LiteralPath $launcher -PathType Leaf)){
    throw 'Only an already built isolated research launcher may be tested.'
}
$sourcePrefs=Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'BveTs\Settings\BveTs6.Preferences.xml'
$displayPrefs=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'BVE6互換ランチャー\launch-displays.xml'
$game='C:\Program Files\mackoy\BveTs6\BveTs.exe'
$before=@{}
foreach($file in @($sourcePrefs,$displayPrefs,$game)){
    $before[$file]=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
}
$screen1=[Windows.Forms.Screen]::AllScreens |
    Where-Object {$_.DeviceName -eq '\\.\DISPLAY1'} | Select-Object -First 1
$screen2=[Windows.Forms.Screen]::AllScreens |
    Where-Object {$_.DeviceName -eq '\\.\DISPLAY2'} | Select-Object -First 1
if(!$screen1 -or !$screen2 -or $screen1.DeviceName -eq $screen2.DeviceName){
    throw 'Requires the real two distinct connected test monitors.'
}
$scratch=Join-Path $research ('bve-monitor-synthetic-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $scratch -ErrorAction Stop | Out-Null
$env:TEMP=$scratch
$env:TMP=$scratch
Add-Type -ReferencedAssemblies @('System.Windows.Forms','System.Drawing') -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
public sealed class QuietMonitorFixture : Form {
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {
        get {var p=base.CreateParams; p.ExStyle|=0x08000000; return p;}
    }
}
public static class MonitorFixtureNative {
    [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]public static extern bool IsZoomed(IntPtr hwnd);
}
'@
$assembly=[Reflection.Assembly]::LoadFrom($launcher)
$flags=[Reflection.BindingFlags]'NonPublic,Public,Static,Instance'
$type=$assembly.GetType('BveLaunchDisplay',$true)
$arrange=$type.GetMethod('ArrangeWhenReady',$flags,$null,[Type[]]@([int],[int],[string]),$null)
$find=$type.GetMethod('FindBveWindows',$flags)
$isOn=$type.GetMethod('IsOnRequestedMonitor',$flags)
$loading=$type.GetMethod('IsLoadingScenario',$flags)
if(!$arrange -or !$find -or !$isOn -or !$loading){
    throw 'Required isolated monitor recovery APIs not compiled.'
}
$main=New-Object QuietMonitorFixture
$loader=New-Object QuietMonitorFixture
$oldMain=$null
function LocateMain {
    $windows=@($find.Invoke($null,[object[]]@([int]$PID)))
    foreach($window in $windows) {
        if(!$window.GetType().GetField('ScenarioPicker',$flags).GetValue($window)){
            return $window
        }
    }
    return $null
}
function OnTarget($target) {
    if(!$target){return $false}
    $bounds=$target.GetType().GetField('Bounds',$flags).GetValue($target)
    return [bool]$isOn.Invoke($null,[object[]]@($bounds,$screen1.Bounds))
}
function Wait-ForTarget([int]$timeoutMs) {
    $end=(Get-Date).AddMilliseconds($timeoutMs)
    while((Get-Date) -lt $end) {
        [Windows.Forms.Application]::DoEvents()
        $target=LocateMain
        if((OnTarget $target) -and [MonitorFixtureNative]::IsZoomed($main.Handle)){
            return $true
        }
        Start-Sleep -Milliseconds 100
    }
    return $false
}
try {
    $main.Text='BVE Trainsim 6'
    $main.StartPosition=[Windows.Forms.FormStartPosition]::Manual
    $main.ShowInTaskbar=$false
    $main.Bounds=[Drawing.Rectangle]::new($screen2.Bounds.Left+50,
        $screen2.Bounds.Top+50,640,651)
    $loader.Text='シナリオを読み込んでいます...'
    $loader.StartPosition=[Windows.Forms.FormStartPosition]::Manual
    $loader.ShowInTaskbar=$false
    $loader.Bounds=[Drawing.Rectangle]::new($screen2.Bounds.Left+100,
        $screen2.Bounds.Top+100,350,180)
    $foreground=[MonitorFixtureNative]::GetForegroundWindow()
    $main.Show()
    $main.WindowState=[Windows.Forms.FormWindowState]::Maximized
    $loader.Show()
    [Windows.Forms.Application]::DoEvents()
    if([MonitorFixtureNative]::GetForegroundWindow() -ne $foreground){
        throw 'Synthetic window stole keyboard focus. Refusing any test.'
    }
    if(OnTarget (LocateMain)){
        throw 'Synthetic window should begin outside the target monitor.'
    }
    if(![bool]$loading.Invoke($null,[object[]]@([int]$PID))){
        throw 'Synthetic route loading window was not recognized.'
    }
    $null=$arrange.Invoke($null,[object[]]@([int]$PID,[int]6,[string]$screen1.DeviceName))
    if(!(Wait-ForTarget 4500)){throw 'Initial maximized placement failed.'}
    Write-Output 'INITIAL_MAXIMIZED_BVE_MAIN_MOVED_TO_SELECTED_DISPLAY_PASS'

    # BVE may subsequently maximize the original main HWND on its previous
    # monitor. Exercise that exact same-HWND startup reset path.
    $main.WindowState=[Windows.Forms.FormWindowState]::Normal
    $main.Bounds=[Drawing.Rectangle]::new($screen2.Bounds.Left+50,
        $screen2.Bounds.Top+50,640,651)
    $main.WindowState=[Windows.Forms.FormWindowState]::Maximized
    [Windows.Forms.Application]::DoEvents()
    if(OnTarget (LocateMain)){
        throw 'Synthetic reset did not reproduce an off-target monitor.'
    }
    if(!(Wait-ForTarget 4500)){
        throw 'Launcher did not recover a SAME-HWND maximized monitor reset.'
    }
    Write-Output 'SAME_HWND_AFTER_SCENARIO_REMAXIMIZE_RECOVERED_PASS'
    if([MonitorFixtureNative]::GetForegroundWindow() -ne $foreground){
        throw 'Monitor recovery stole keyboard focus.'
    }
    Write-Output 'NO_KEYBOARD_FOCUS_STEAL_PASS'
    $left=[Drawing.Rectangle]::new(-3840,-1080,2194,1234)
    $normal=[Drawing.Rectangle]::new(-3600,-800,640,651)
    if(![bool]$isOn.Invoke($null,[object[]]@($normal,$left)) -or
       [bool]$isOn.Invoke($null,[object[]]@($normal,$screen1.Bounds))){
        throw 'Negative-coordinate monitor geometry failed.'
    }
    Write-Output 'NEGATIVE_MONITOR_GEOMETRY_PASS'

    # When the loading form closes, a user may manually move the already
    # placed main HWND. It must stay where the user put it during the grace.
    $loader.Close()
    [Windows.Forms.Application]::DoEvents()
    $originalHandle=$main.Handle
    $main.WindowState=[Windows.Forms.FormWindowState]::Normal
    $main.Bounds=[Drawing.Rectangle]::new($screen2.Bounds.Left+60,
        $screen2.Bounds.Top+60,640,651)
    $main.WindowState=[Windows.Forms.FormWindowState]::Maximized
    [Windows.Forms.Application]::DoEvents()
    if(OnTarget (LocateMain)){throw 'Post-load synthetic user movement did not leave target display.'}
    for($i=0;$i -lt 35;$i++){
        [Windows.Forms.Application]::DoEvents()
        if(OnTarget (LocateMain)){
            throw 'Launcher reversed the simulated manual move after loading ended.'
        }
        Start-Sleep -Milliseconds 100
    }
    Write-Output 'POST_LOAD_SAME_HWND_MANUAL_MOVE_RESPECTED_PASS'

    # A late-created main HWND is different: it still needs its first
    # placement even though the loading form is already gone.
    $oldMain=$main
    $oldMain.Hide()
    $main=New-Object QuietMonitorFixture
    $main.Text='BVE Trainsim 6'
    $main.StartPosition=[Windows.Forms.FormStartPosition]::Manual
    $main.ShowInTaskbar=$false
    $main.Bounds=[Drawing.Rectangle]::new($screen2.Bounds.Left+80,
        $screen2.Bounds.Top+80,640,651)
    $main.Show()
    $main.WindowState=[Windows.Forms.FormWindowState]::Maximized
    [Windows.Forms.Application]::DoEvents()
    if($main.Handle -eq $originalHandle){throw 'Synthetic new main HWND was recycled.'}
    if(!(Wait-ForTarget 4500)){
        throw 'New main HWND was not placed during post-load grace.'
    }
    Write-Output 'POST_LOAD_NEW_HWND_FIRST_PLACEMENT_PASS'
    if([MonitorFixtureNative]::GetForegroundWindow() -ne $foreground){
        throw 'Post-load placement stole keyboard focus.'
    }
}finally {
    $loader.Close()
    $main.Close()
    $loader.Dispose()
    $main.Dispose()
    if($null -ne $oldMain){$oldMain.Close();$oldMain.Dispose()}
}
foreach($file in $before.Keys){
    if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $before[$file]){
        throw ('Protected original changed during synthetic test: '+$file)
    }
}
Write-Output 'ORIGINAL_BVE_AND_USER_MONITOR_PREFERENCES_UNCHANGED_PASS'
Write-Output 'ONLY_OWNED_SYNTHETIC_WINDOWS_WERE_MOVED=True'
