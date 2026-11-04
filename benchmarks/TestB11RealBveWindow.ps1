param(
    [string]$Scenario='',
    [string]$TargetMonitor='\\.\DISPLAY3',
    [int]$ObserveSeconds=45
)
$ErrorActionPreference='Stop'
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
$app=Join-Path $root 'dist-b11-options/BVE6互換ランチャー.exe'
$probe=Join-Path $root 'dist-b11-options/BveWindowsReadOnly.exe'
$closeDialogs=Join-Path $root 'dist-b11-options/CloseBveTestDialogs.exe'
$bve='C:\Program Files\mackoy\BveTs6\BveTs.exe'
$preferences=Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'BveTs/Settings/BveTs6.Preferences.xml'
$monitorSettings=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'BVE6互換ランチャー/launch-displays.xml'
foreach($f in @($app,$probe,$closeDialogs,$bve,$preferences)){
    if(!(Test-Path -LiteralPath $f)){throw ('Required file missing: '+$f)}
}
if($Scenario -and !(Test-Path -LiteralPath $Scenario)){
    throw ('Requested scenario file missing: '+$Scenario)
}
if(@(Get-Process BveTs -ErrorAction SilentlyContinue).Count){
    Write-Output 'B11_REAL_WINDOW_SKIPPED: BVE is currently running.'
    exit 2
}
$folder=Join-Path $root ('benchmarks/b11-real-window-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder|Out-Null
$prefCopy=Join-Path $folder 'BveTs6.Preferences.original.xml'
Copy-Item -LiteralPath $preferences -Destination $prefCopy
$originalHash=(Get-FileHash $preferences).Hash
$monitorHash=if(Test-Path $monitorSettings){(Get-FileHash $monitorSettings).Hash}else{''}
$assembly=[Reflection.Assembly]::LoadFrom($app)
$flags=[Reflection.BindingFlags]'NonPublic,Public,Static'
$type=$assembly.GetType('BveLaunchDisplay',$true)
$method=$type.GetMethod('ArrangeWhenReady',$flags,$null,[Type[]]@([int],[int],[string]),$null)
if(!$method){throw 'No testable display placement overload'}
Add-Type -AssemblyName System.Windows.Forms
$targetScreen=[Windows.Forms.Screen]::AllScreens|Where-Object {
    $_.DeviceName -eq $TargetMonitor
} | Select-Object -First 1
if(!$targetScreen){throw ('Requested test monitor is not connected: '+$TargetMonitor)}
$info=[Diagnostics.ProcessStartInfo]::new()
$info.FileName=$bve
$info.Arguments=if($Scenario){'"'+$Scenario+'"'}else{''}
$info.WorkingDirectory=[IO.Path]::GetDirectoryName($bve)
$info.UseShellExecute=$false
foreach($n in @('BVE5COMPAT_ATS','BVE5COMPAT_LOG','BVE5COMPAT_WORKDIR','BVE5COMPAT_PROFILE')){
    $info.EnvironmentVariables.Remove($n)
}
$started=$null
try{
    if(@(Get-Process BveTs -ErrorAction SilentlyContinue).Count){
        throw 'BVE started while preparing the test; refusing concurrent launch'
    }
    $started=[Diagnostics.Process]::Start($info)
    if(!$started){throw 'Unable to start BVE6'}
    Write-Output ('TEST_STARTED_BVE_PID='+$started.Id)
    $method.Invoke($null,[object[]]@([int]$started.Id,[int]6,[string]$TargetMonitor)) | Out-Null
    $onTarget=$false
    for($i=0;$i -lt $ObserveSeconds;$i++){
        Start-Sleep -Seconds 1
        $started.Refresh()
        if($started.HasExited){
            Write-Output ('BVE_EARLY_EXIT_CODE='+$started.ExitCode)
            break
        }
        $windows=@(& $probe ([string]$started.Id))
        $mainOnTarget=$false
        $pickerOnTarget=$false
        foreach($entry in $windows){
            if($entry -notmatch 'VISIBLE=True' -or
               $entry -notmatch 'RECT=(-?\d+),(-?\d+),(-?\d+),(-?\d+)'){continue}
            $cx=([int]$Matches[1]+[int]$Matches[3])/2.0
            $cy=([int]$Matches[2]+[int]$Matches[4])/2.0
            if($targetScreen.Bounds.Contains([Drawing.Point]::new([int]$cx,[int]$cy))){
                if($entry -match 'TITLE=BVE Trainsim [56]'){$mainOnTarget=$true}
                if($entry -match 'TITLE=シナリオの選択'){$pickerOnTarget=$true}
            }
        }
        $onTarget=$mainOnTarget -and $pickerOnTarget
        if($onTarget){
            Write-Output ('B11_MAIN_AND_SCENARIO_MONITOR_CONFIRMED_AFTER_SECONDS='+($i+1))
        }
        if($onTarget){break}
    }
    if(!$started.HasExited){
        Write-Output 'BVE_VISIBLE_WINDOWS:'
        $lines=@(& $probe ([string]$started.Id))
        $lines|ForEach-Object{Write-Output $_}
        Write-Output ('B11_MAIN_WINDOW_TARGET_MATCH='+$mainOnTarget)
        Write-Output ('B11_SCENARIO_PICKER_TARGET_MATCH='+$pickerOnTarget)
        if(!$onTarget){throw 'BVE main window and scenario picker were not both placed on the selected monitor'}
    }
}finally{
    if($started -ne $null){
        $started.Refresh()
        if(!$started.HasExited){
            & $closeDialogs ([string]$started.Id)
            Start-Sleep -Milliseconds 700
            $started.Refresh()
        }
        if(!$started.HasExited){
            Write-Output ('BVE_CLOSE_REQUEST='+$started.CloseMainWindow())
            if(!$started.WaitForExit(8000)){
                Write-Warning ('Test-launched BVE still running: PID '+$started.Id+'. It was not forcibly terminated.')
            }
        }
        $started.Refresh()
        if($started.HasExited){
            $currentHash=(Get-FileHash $preferences).Hash
            if($currentHash -ne $originalHash){
                Copy-Item -LiteralPath $prefCopy -Destination $preferences -Force
                Write-Output 'BVE6_PREFERENCES_RESTORED_AFTER_TEST'
            }else{Write-Output 'BVE6_PREFERENCES_UNCHANGED'}
        }else{
            Write-Warning 'BVE is still running. Its preferences have not been overwritten while in use.'
        }
        $started.Dispose()
    }
    $monitorAfter=if(Test-Path $monitorSettings){(Get-FileHash $monitorSettings).Hash}else{''}
    if($monitorHash -ne $monitorAfter){throw 'Launcher monitor preference changed unexpectedly'}
    Write-Output 'LAUNCHER_MONITOR_PREFERENCE_UNCHANGED'
}
