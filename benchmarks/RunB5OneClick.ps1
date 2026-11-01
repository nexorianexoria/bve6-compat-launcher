param(
 [string]$ScenarioPath='',
 [ValidateRange(15,300)][int]$Seconds=240,
 [switch]$ValidateOnly
)
$ErrorActionPreference='Stop'

$packageRoot=Split-Path -Parent $PSScriptRoot
$launcher=Join-Path $packageRoot 'BVE6互換ランチャー.exe'
$recordScript=Join-Path $PSScriptRoot 'CaptureB5Timing.ps1'
$config=Join-Path $packageRoot 'oneclick-scenario.local.txt'

if(!(Test-Path -LiteralPath $launcher -PathType Leaf)){throw 'The b5 launcher EXE is missing.'}
if(!(Test-Path -LiteralPath $recordScript -PathType Leaf)){throw 'The FPS capture helper is missing.'}
if([string]::IsNullOrWhiteSpace($ScenarioPath) -and (Test-Path -LiteralPath $config)){
 $ScenarioPath=[IO.File]::ReadAllText($config,[Text.Encoding]::UTF8).Trim()
}
if([string]::IsNullOrWhiteSpace($ScenarioPath)){
 Write-Output 'First-time setup: enter the full path to a BVE scenario TXT.'
 Write-Output 'This is saved locally and will not be included in public ZIPs.'
 $ScenarioPath=Read-Host 'Scenario TXT path'
 if([string]::IsNullOrWhiteSpace($ScenarioPath)){throw 'No scenario was selected.'}
 $ScenarioPath=$ScenarioPath.Trim('"')
 if(!(Test-Path -LiteralPath $ScenarioPath -PathType Leaf)){throw 'Scenario file not found.'}
 [IO.File]::WriteAllText($config,[IO.Path]::GetFullPath($ScenarioPath),
    (New-Object Text.UTF8Encoding($false)))
}
if(!(Test-Path -LiteralPath $ScenarioPath -PathType Leaf)){
 throw ('The configured scenario no longer exists: '+$ScenarioPath)
}
$ScenarioPath=(Resolve-Path -LiteralPath $ScenarioPath).Path
if(-not $ScenarioPath.EndsWith('.txt',[StringComparison]::OrdinalIgnoreCase)){
 throw 'The scenario must be a .txt file.'
}
if($ScenarioPath.Contains('"')){throw 'Scenario path contains an invalid quote character.'}
if($ValidateOnly){
 $results=Join-Path $PSScriptRoot 'results'
 New-Item -ItemType Directory -Path $results -Force|Out-Null
 $pidFile=Join-Path $results ('oneclick-validation-'+[Guid]::NewGuid().ToString('N')+'.txt')
 $arguments='--auto-compat "'+$ScenarioPath+'" --pid-file "'+$pidFile+'" --validate-only'
 $child=Start-Process -FilePath $launcher -ArgumentList $arguments -WorkingDirectory $packageRoot -PassThru -Wait
 if($child.ExitCode -ne 0){throw ('Launcher validation failed. Exit code '+$child.ExitCode+'. Check launcher logs.')}
 if(Test-Path -LiteralPath $pidFile){throw 'Validation unexpectedly started BVE.'}
 Write-Output ('ONECLICK_VALIDATION_OK scenario='+$ScenarioPath)
 return
}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=New-Object Security.Principal.WindowsPrincipal($identity)
if($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
 throw 'Open the one-click CMD normally, not as Administrator. BVE must remain non-elevated.'
}
if(@(Get-Process -Name BveTs -ErrorAction SilentlyContinue).Count -ne 0){
 throw 'Another BVE instance is running. Exit it before using one-click measurement.'
}
$results=Join-Path $PSScriptRoot 'results'
$run=Join-Path $results ('oneclick-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
if(Test-Path -LiteralPath $run){throw 'Output run already exists.'}
New-Item -ItemType Directory -Path $run -Force|Out-Null
$pidFile=Join-Path $run 'bve.pid'
$readyFile=Join-Path $run 'admin-ready.txt'
$phaseFile=Join-Path $run 'bve-oneclick-session.txt'
$helperArgs='-NoProfile -ExecutionPolicy Bypass -File "'+$recordScript+
 '" -WaitForPidFile "'+$pidFile+'" -ReadyFile "'+$readyFile+
 '" -Seconds '+$Seconds+' -OutputDirectory "'+(Join-Path $run 'capture')+
 '" -WaitForFirstAtsElapse -ReadyDelaySeconds 0 -WaitForFirstPresent -AnalyzeAfterExit -AnalysisWaitSeconds 3600'
Write-Output 'One-click b5 test: first grant administrator permission to PresentMon only.'
Write-Output 'The BVE launcher will run under normal (non-administrator) user permissions.'
Write-Output 'The test will not close BVE. Close the simulator normally after driving.'
Write-Output ('Selected scenario: '+$ScenarioPath)
Write-Output ('Session folder: '+$run)
try{
 $monitor=Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $helperArgs -PassThru
}catch{
 throw ('Could not start the elevated FPS recorder (UAC may have been cancelled): '+$_.Exception.Message)
}
$deadline=(Get-Date).AddSeconds(60)
while(!(Test-Path -LiteralPath $readyFile)){
 if($monitor.HasExited){throw ('Elevated FPS recorder exited before it was ready (exit code '+$monitor.ExitCode+').')}
 if((Get-Date) -gt $deadline){throw 'Elevated FPS recorder did not signal readiness.'}
 Start-Sleep -Milliseconds 250
}
Write-Output 'Recorder is ready. Automatically launching the previous BVE scenario.'
$arguments='--auto-compat "'+$ScenarioPath+'" --pid-file "'+$pidFile+'"'
$launcherProcess=Start-Process -FilePath $launcher -ArgumentList $arguments -WorkingDirectory $packageRoot -PassThru
$launchDeadline=(Get-Date).AddSeconds(180)
while(!(Test-Path -LiteralPath $pidFile)){
 if($launcherProcess.HasExited){
  $errFile=$pidFile+'.error.txt'
  if(Test-Path -LiteralPath $errFile){
   throw ('Automatic BVE start failed: '+([IO.File]::ReadAllText($errFile)).Substring(0,[Math]::Min(700,(Get-Item $errFile).Length)))
  }
  throw ('Launcher exited before BVE started. Exit code '+$launcherProcess.ExitCode)
 }
 if((Get-Date) -gt $launchDeadline){throw 'BVE did not report its PID within 180 seconds.'}
 Start-Sleep -Milliseconds 250
}
$launchedPid=0
if(![int]::TryParse(([IO.File]::ReadAllText($pidFile)).Trim(),[ref]$launchedPid) -or $launchedPid -le 0){
 throw 'Launcher produced an invalid BVE PID.'
}
[IO.File]::WriteAllLines($phaseFile,
 @('scenario='+$ScenarioPath,'bve_pid='+$launchedPid,'launched_utc='+(Get-Date).ToUniversalTime().ToString('o')),
 (New-Object Text.UTF8Encoding($false)))
Write-Output ('BVE started (PID '+$launchedPid+'). Waiting for first ATS Elapse and drawing; the 240-second capture then starts automatically.')
Write-Output 'Continue driving normally and close BVE when finished; analysis will run after game exit.'
$monitor.WaitForExit()
if($monitor.ExitCode -ne 0){
 throw ('Elevated FPS/ATS recorder failed (exit code '+$monitor.ExitCode+'). See the recorder PowerShell window and '+$run)
}
Write-Output ('ONECLICK_COMPLETED. Saved data: '+$run)