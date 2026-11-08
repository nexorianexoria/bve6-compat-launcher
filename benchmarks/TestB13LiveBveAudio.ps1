param(
    [ValidateSet(5,6)][int]$Version=6,
    [string]$Scenario='',
    [ValidateRange(5,120)][int]$ObserveSeconds=22,
    [switch]$UseLauncherCore
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$sandbox=Join-Path $root ('dist-b13-perf/audio-focus-sandbox/BVE'+$Version)
$exe=Join-Path $sandbox 'BveTs.exe'
$meter=Join-Path $root 'benchmarks/results/B13BveAudioFocusMeter.exe'
$scenarios=Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'BveTs/Scenarios'
if(!$Scenario){$Scenario=Join-Path $scenarios 'mackoy.Keisei.txt'}
$original=if($Version -eq 6){
    'C:\Program Files\mackoy\BveTs6\BveTs.exe'
}else{
    'C:\Program Files (x86)\mackoy\BveTs5\BveTs.exe'
}
$preference=Join-Path ([Environment]::GetFolderPath('MyDocuments')) ('BveTs/Settings/BveTs'+$Version+'.Preferences.xml')
$files=@($exe,$original,$meter,$Scenario)
foreach($f in $files){
    if(!(Test-Path -LiteralPath $f -PathType Leaf)){throw ('Required test file missing: '+$f)}
}
if(@(Get-Process -Name BveTs -ErrorAction SilentlyContinue).Count -gt 0){
    throw 'A BVE process is already running. Refusing to start another.'
}
powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'TestB13BackgroundAudioSandbox.ps1') | Out-Host
if($LASTEXITCODE -ne 0){throw 'Sandbox verification failed'}
$originalHash=(Get-FileHash $original -Algorithm SHA256).Hash
$settingsExisted=Test-Path -LiteralPath $preference
$settingsHash=if($settingsExisted){(Get-FileHash $preference -Algorithm SHA256).Hash}else{''}
$settingsBackup=Join-Path $sandbox ('preferences-backup-'+[guid]::NewGuid().ToString('N')+'.xml')
if($settingsExisted){Copy-Item -LiteralPath $preference -Destination $settingsBackup}
$log=Join-Path $sandbox ('dsoal-live-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
$observations=Join-Path $sandbox ('focus-peaks-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.txt')
$info=New-Object Diagnostics.ProcessStartInfo
$info.FileName=if($UseLauncherCore){$original}else{$exe}
$info.WorkingDirectory=Split-Path -Parent $info.FileName
$info.UseShellExecute=$false
$info.Arguments='"'+$Scenario+'"'
$info.EnvironmentVariables['DSOAL_LOGLEVEL']='2'
$info.EnvironmentVariables['DSOAL_LOGFILE']=$log
foreach($key in @('BVE5COMPAT_ATS','BVE5COMPAT_LOG','BVE5COMPAT_WORKDIR','BVE5COMPAT_PROFILE')){
    $info.EnvironmentVariables.Remove($key)
}
$process=$null
try {
    if($UseLauncherCore){
        $launcher=Join-Path $root 'dist-b13-perf/audio-preview/BVE6互換ランチャー.exe'
        $audioRoot=Join-Path $root 'dist-b13-perf/audio-preview/audio'
        $runs=Join-Path $root 'benchmarks/results/b13-live-launcher-runs'
        if(!(Test-Path $launcher)){throw 'Audio-enabled prototype launcher is missing'}
        $assembly=[Reflection.Assembly]::LoadFrom($launcher)
        $type=$assembly.GetType('BveBackgroundAudio',$true)
        $flags=[Reflection.BindingFlags]'NonPublic,Public,Static'
        $startCore=$type.GetMethod('StartCore',$flags)
        $arguments=New-Object 'object[]' 5
        $arguments[0]=[Diagnostics.ProcessStartInfo]$info.PSObject.BaseObject
        $arguments[1]=[int]$Version
        $arguments[2]=[bool]$true
        $arguments[3]=[string]$audioRoot
        $arguments[4]=[string]$runs
        $process=[Diagnostics.Process]$startCore.Invoke($null,$arguments)
        $sandbox=Split-Path -Parent $process.StartInfo.FileName
        $log=Join-Path $sandbox 'dsoal-test.log'
        Write-Output 'LAUNCH_PATH=EXPERIMENTAL_LAUNCHER_STARTCORE'
    }else{
        $process=[Diagnostics.Process]::Start($info)
        Write-Output 'LAUNCH_PATH=EXISTING_SANDBOX'
    }
    if($null -eq $process){throw 'BVE test process did not start'}
    Write-Output ('TEST_BVE_PID='+$process.Id)
    Write-Output ('TEST_BVE_PATH='+$process.StartInfo.FileName)
    Write-Output ('TEST_SCENARIO='+$Scenario)
    $loadedSound=$false
    $loadedAl=$false
    for($i=0;$i -lt 20;$i++){
        Start-Sleep -Seconds 1
        $process.Refresh()
        if($process.HasExited){break}
        try {
            $modules=@($process.Modules | ForEach-Object {$_.FileName})
            $loadedSound=@($modules|Where-Object {
                $_.Equals((Join-Path $sandbox 'dsound.dll'),[StringComparison]::OrdinalIgnoreCase)
            }).Count -gt 0
            $loadedAl=@($modules|Where-Object {
                $_.Equals((Join-Path $sandbox 'dsoal-aldrv.dll'),[StringComparison]::OrdinalIgnoreCase)
            }).Count -gt 0
            if($loadedSound -and $loadedAl){break}
        }catch{Write-Output ('MODULE_QUERY_WARNING='+$_.Exception.Message)}
    }
    $process.Refresh()
    if($process.HasExited){
        Write-Output ('BVE_EXITED_EARLY='+$process.ExitCode)
    }else{
        Write-Output ('DSOAL_DSOUND_LOADED='+$loadedSound)
        Write-Output ('DSOAL_OPENAL_LOADED='+$loadedAl)
        Write-Output 'AUDIO_METER_START'
        & $meter $process.Id $ObserveSeconds | Out-File -LiteralPath $observations -Encoding utf8
        if($LASTEXITCODE -ne 0){throw 'Audio meter failed'}
        Get-Content -LiteralPath $observations | Where-Object {
            $_ -match '^(FOCUSED|UNFOCUSED|FOCUS_COMPARISON|AUDIO_RAW_RECORDING)='
        } | Out-Host
        Write-Output ('PEAK_RESULTS='+$observations)
    }
}finally{
    if($null -ne $process){
        $process.Refresh()
        if(!$process.HasExited){
            Write-Output ('CLOSE_MAIN_WINDOW_REQUEST='+$process.CloseMainWindow())
            if(!$process.WaitForExit(7000)){
                $helper=Join-Path $root 'dist-b11-options/CloseBveTestDialogs.exe'
                if(Test-Path -LiteralPath $helper) {
                    & $helper ([string]$process.Id) | Out-Host
                }
                $process.Refresh()
                if(!$process.HasExited) {
                    Write-Output ('RETRY_CLOSE_MAIN_WINDOW='+$process.CloseMainWindow())
                    if(!$process.WaitForExit(7000)){
                        Write-Warning ('Test BVE remains open (PID '+$process.Id+
                            '); not terminating forcibly')
                    }
                }
            }
        }
        $process.Refresh()
        $hasExited=$process.HasExited
        $process.Dispose()
        if($hasExited -and !$settingsExisted -and (Test-Path -LiteralPath $preference)){
            Remove-Item -LiteralPath $preference -Force
            Write-Output 'TEST_CREATED_BVE_PREFERENCES_CLEANED'
        }elseif($hasExited -and $settingsExisted -and
            (Get-FileHash $preference -Algorithm SHA256).Hash -ne $settingsHash){
            Copy-Item -LiteralPath $settingsBackup -Destination $preference -Force
            Write-Output 'USER_BVE_PREFERENCES_RESTORED'
        }elseif($hasExited){Write-Output 'USER_BVE_PREFERENCES_UNCHANGED'}
    }
    if((Get-FileHash $original -Algorithm SHA256).Hash -ne $originalHash){
        throw 'Installed BVE unexpectedly changed'
    }
    Write-Output 'INSTALLED_BVE_EXECUTABLE_UNCHANGED'
    if(Test-Path $log){
        Write-Output ('DSOAL_LOG='+$log)
        Get-Content -LiteralPath $log -Tail 14 | Out-Host
    }
    if($null -ne $process -and $hasExited -and (Test-Path $settingsBackup)){
        Remove-Item -LiteralPath $settingsBackup -Force
    }
}
