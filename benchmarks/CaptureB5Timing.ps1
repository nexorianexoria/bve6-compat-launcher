param(
 [ValidateRange(5,300)][int]$Seconds=240,
 [string]$PresentMonPath='',
 [string]$OutputDirectory='',
 [switch]$AnalyzeAfterExit,
 [ValidateRange(1,7200)][int]$AnalysisWaitSeconds=3600,
 [int]$BvePid=0,
 [string]$WaitForPidFile='',
 [string]$ReadyFile='',
 [ValidateRange(5,300)][int]$PidWaitSeconds=120,
 [ValidateRange(0,90)][int]$ReadyDelaySeconds=12,
 [switch]$WaitForFirstPresent,
 [ValidateRange(15,900)][int]$MaxFirstPresentWaitSeconds=360,
 [ValidateRange(5,30)][int]$FirstPresentProbeSeconds=8,
 [switch]$WaitForFirstAtsElapse,
 [ValidateRange(15,900)][int]$MaxFirstAtsWaitSeconds=360
)
$ErrorActionPreference='Stop'
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=New-Object Security.Principal.WindowsPrincipal($identity)
if(!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
 Write-Output 'Administrator PowerShell is required for PresentMon ETW capture.'
 Write-Output ('Re-run this script from Administrator PowerShell: '+$PSCommandPath)
 exit 5
}
if([string]::IsNullOrWhiteSpace($PresentMonPath)){
 $PresentMonPath=Join-Path $PSScriptRoot 'tools\PresentMon-2.5.1-x64.exe'
}
if(!(Test-Path -LiteralPath $PresentMonPath)){
 throw 'Signed PresentMon 2.5.1 not found. Provide -PresentMonPath or copy it to benchmarks\tools.'
}
if((Get-AuthenticodeSignature -LiteralPath $PresentMonPath).Status -ne 'Valid'){
 throw 'The PresentMon signature is not valid. Capture cancelled.'
}
if(-not [string]::IsNullOrWhiteSpace($WaitForPidFile)){
 if($BvePid -ne 0){throw 'Specify BvePid or WaitForPidFile, not both.'}
 if(Test-Path -LiteralPath $WaitForPidFile){throw 'One-click PID marker already exists.'}
 if(-not [string]::IsNullOrWhiteSpace($ReadyFile)){
  if(Test-Path -LiteralPath $ReadyFile){throw 'One-click ready marker already exists.'}
  [IO.File]::WriteAllText($ReadyFile,'ready',(New-Object Text.UTF8Encoding($false)))
 }
 Write-Output 'Waiting for the non-administrator BVE launcher to provide its exact PID.'
 $deadline=(Get-Date).AddSeconds($PidWaitSeconds)
 while(!(Test-Path -LiteralPath $WaitForPidFile)){
  if((Get-Date) -gt $deadline){throw 'Timed out waiting for automatic BVE launch.'}
  Start-Sleep -Milliseconds 300
 }
 $pidText=[IO.File]::ReadAllText($WaitForPidFile).Trim()
 $parsedPid=0
 if(![int]::TryParse($pidText,[ref]$parsedPid) -or $parsedPid -le 0){
  throw 'Automatic BVE PID marker is malformed.'
 }
 $BvePid=$parsedPid
 if($ReadyDelaySeconds -gt 0){
  Write-Output ('Allowing '+$ReadyDelaySeconds+' seconds for BVE to load before FPS capture.')
  Start-Sleep -Seconds $ReadyDelaySeconds
 }
}
if($BvePid -gt 0){
 $bve=@(Get-Process -Id $BvePid -ErrorAction SilentlyContinue | Where-Object {$_.ProcessName -eq 'BveTs'})
}else{
 $bve=@(Get-Process -Name BveTs -ErrorAction SilentlyContinue)
}
if($bve.Count -ne 1){throw 'Exactly one selected BVE process must be running before capture.'}
try{
 $bveFile=$bve[0].Path
 if([string]::IsNullOrWhiteSpace($bveFile) -or
   [IO.Path]::GetFileName($bveFile) -ne 'BveTs.exe') {throw 'The selected process is not BveTs.exe.'}
}catch{throw ('Failed BVE executable validation: '+$_.Exception.Message)}
if([string]::IsNullOrWhiteSpace($OutputDirectory)){
 $OutputDirectory=Join-Path $PSScriptRoot ('results\b5-qpc-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Output folder already exists; pass a unique -OutputDirectory'}
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$csv=Join-Path $OutputDirectory 'b5-qpc-presentmon.csv'
$capturedBvePid=$bve[0].Id
$firstAtsDetectedUtc=''
$firstAtsQpc=''
$firstAtsLog=''
if($WaitForFirstAtsElapse){
 if([string]::IsNullOrWhiteSpace($WaitForPidFile)){
  throw 'WaitForFirstAtsElapse requires WaitForPidFile from the one-click launcher.'
 }
 $bridgeMarker=$WaitForPidFile+'.bridge-log.txt'
 if(!(Test-Path -LiteralPath $bridgeMarker -PathType Leaf)){
  throw 'The launcher did not provide the ATS bridge log path.'
 }
 $bridgeLogPath=[IO.File]::ReadAllText($bridgeMarker,[Text.Encoding]::UTF8).Trim()
 if([IO.Path]::GetFileName($bridgeLogPath) -ne 'bridge.log' -or
    ![IO.Path]::IsPathRooted($bridgeLogPath)){
  throw 'The launcher-provided ATS log path is invalid.'
 }
 $firstAtsLog=$bridgeLogPath
 Write-Output ('Waiting for first completed 32-bit ATS Elapse for BVE PID '+
    $capturedBvePid+' before starting the FPS timer.')
 $deadline=(Get-Date).AddSeconds($MaxFirstAtsWaitSeconds)
 $markerFound=$false
 while(-not $markerFound){
  $game=@(Get-Process -Id $capturedBvePid -ErrorAction SilentlyContinue |
    Where-Object {$_.ProcessName -eq 'BveTs'})
  if($game.Count -ne 1){
   throw 'BVE exited before the first ATS Elapse. FPS recording was not started.'
  }
  if((Get-Date) -ge $deadline){
   throw 'Timed out waiting for the first ATS Elapse. The scenario may still be loading or lack the 32-bit ATS bridge.'
  }
  if(Test-Path -LiteralPath $bridgeLogPath -PathType Leaf){
   try{
    $match=@(Get-Content -LiteralPath $bridgeLogPath -Encoding UTF8 -ErrorAction Stop |
      Where-Object {$_ -match '^HOST_FIRST_ELAPSE frame=1 qpc=[1-9][0-9]* qpc_freq=[1-9][0-9]*$'})
    if($match.Count -gt 0){
     $markerFound=$true
     $firstAtsQpc=([regex]::Match($match[0],'qpc=(\d+)')).Groups[1].Value
     $firstAtsDetectedUtc=(Get-Date).ToUniversalTime().ToString('o')
     Write-Output ('ATS Elapse confirmed (QPC '+$firstAtsQpc+'). Starting short rendering probe.')
    }
   }catch [System.IO.IOException]{
    # The host may temporarily be updating the shared file. Retry normally.
   }
  }
  if(-not $markerFound){Start-Sleep -Milliseconds 500}
 }
}
$firstPresentDetectedUtc=''
$firstPresentProbeCount=0
if($WaitForFirstPresent){
 $probeBegan=Get-Date
 Write-Output ('Waiting for the first actual PresentMon frame for BVE PID '+$capturedBvePid+
  ' (up to '+$MaxFirstPresentWaitSeconds+' seconds).')
 Write-Output 'The FPS recording timer has not started; BVE is never force-closed.'
 $detected=$false
 while(-not $detected){
  if((Get-Date) -ge $probeBegan.AddSeconds($MaxFirstPresentWaitSeconds)){
   throw 'No BVE present event before the frame-detection deadline. Check the scenario loading state.'
  }
  $process=@(Get-Process -Id $capturedBvePid -ErrorAction SilentlyContinue |
    Where-Object {$_.ProcessName -eq 'BveTs'})
  if($process.Count -ne 1){
   throw 'BVE exited before its first frame. No FPS data was collected.'
  }
  ++$firstPresentProbeCount
  $probeCsv=Join-Path $OutputDirectory ('probe-'+$firstPresentProbeCount.ToString('000')+'.csv')
  $ignoredOutput=@(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'CaptureBveFps.ps1') -Seconds $FirstPresentProbeSeconds -OutputPath $probeCsv -PresentMonPath $PresentMonPath -QpcTime -TerminateOnProcExit -BvePid $capturedBvePid 2>&1)
  $probeExit=$LASTEXITCODE
  if($probeExit -ne 0 -and $probeExit -ne 3){
   throw ('First-frame probe failed, exit '+$probeExit+': '+
       (($ignoredOutput|Select-Object -Last 3) -join ' '))
  }
  if(Test-Path -LiteralPath $probeCsv){
   $probeHeaderAndRow=@(Get-Content -LiteralPath $probeCsv -TotalCount 2)
   if($probeHeaderAndRow.Count -ge 2){
    $probeSample=@($probeHeaderAndRow|ConvertFrom-Csv)
    if($probeSample.Count -eq 1 -and
       [string]$probeSample[0].ProcessID -eq [string]$capturedBvePid -and
       [string]$probeSample[0].CPUStartQPC -match '^[1-9][0-9]*$'){
     $detected=$true
     $firstPresentDetectedUtc=(Get-Date).ToUniversalTime().ToString('o')
     Write-Output ('First BVE frame detected after '+$firstPresentProbeCount+
       ' probes. Starting the full '+$Seconds+'-second capture now.')
    }
   }
   Remove-Item -LiteralPath $probeCsv -Force
  }
  if(-not $detected){Write-Output ('No frame yet (probe '+$firstPresentProbeCount+'). BVE is still loading.')}
 }
}
Write-Output ('Capture BVE PID '+$capturedBvePid+' for '+$Seconds+' seconds')
Write-Output 'Do not minimize the driving window. Keep the route, vehicle and camera/viewpoint constant.'
Write-Output 'Capture stops after the requested time OR when BVE exits, whichever comes first.'
$before=(Get-Date).ToUniversalTime()
$beforeQpc=[Diagnostics.Stopwatch]::GetTimestamp()
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'CaptureBveFps.ps1') -Seconds $Seconds -OutputPath $csv -PresentMonPath $PresentMonPath -QpcTime -TerminateOnProcExit -BvePid $capturedBvePid
$captureExit=$LASTEXITCODE
$afterQpc=[Diagnostics.Stopwatch]::GetTimestamp()
$after=(Get-Date).ToUniversalTime()
if($captureExit -ne 0){throw ('QPC capture failed exit='+$captureExit)}
if(!(Test-Path -LiteralPath $csv)){throw 'PresentMon CSV missing'}
$header=Get-Content -LiteralPath $csv -TotalCount 1
if(!$header -or !$header.Contains('CPUStartQPC')){throw 'Capture lacks CPUStartQPC; check PresentMon options'}
$meta=@(
 'capture_kind=BVE_QPC',
 ('bve_pid='+$capturedBvePid),
 ('csv_file='+$csv),
 ('start_utc='+$before.ToString('o')),
 ('end_utc='+$after.ToString('o')),
 ('runner_qpc_start='+$beforeQpc),
 ('runner_qpc_end='+$afterQpc),
 ('runner_qpc_frequency='+[Diagnostics.Stopwatch]::Frequency),
 ('seconds_requested='+$Seconds),
 ('wait_for_first_present='+[bool]$WaitForFirstPresent),
 ('first_present_probe_count='+$firstPresentProbeCount),
 ('wait_for_first_ats_elapse='+[bool]$WaitForFirstAtsElapse),
 ('first_ats_detected_utc='+$firstAtsDetectedUtc),
 ('first_ats_elapse_qpc='+$firstAtsQpc),
 ('first_ats_bridge_log='+$firstAtsLog),
 ('first_present_detected_utc='+$firstPresentDetectedUtc),
 'timestamp_note=PresentMon CPUStartQPC is used for alignment; UTC runner times are informational.',
 'warning=Keep swap chains separate. CPU starts are not exact display times.'
)
[IO.File]::WriteAllLines((Join-Path $OutputDirectory 'capture-metadata.txt'),$meta,(New-Object Text.UTF8Encoding($false)))
Write-Output ('CAPTURE_OK CSV='+$csv)
Write-Output 'Now close BVE normally to flush the ATS bridge SLOW_PHASE events to bridge.log.'
$analysisCommand='powershell.exe -NoProfile -ExecutionPolicy Bypass -File "'+
 (Join-Path $PSScriptRoot 'AnalyzeB5Run.ps1')+'" -CsvPath "'+$csv+'"'
Write-Output ('After BVE exit, run: '+$analysisCommand)
if($AnalyzeAfterExit){
 Write-Output 'Automatic analysis requested. Close BVE normally when ready; this script does not stop it.'
 try {
  & (Join-Path $PSScriptRoot 'AnalyzeB5Run.ps1') -CsvPath $csv -WaitForExit -ExitWaitSeconds $AnalysisWaitSeconds
 }catch{
  Write-Warning ('FPS capture succeeded, but ATS/FPS analysis could not finish: '+$_.Exception.Message)
  Write-Output ('Use the saved CSV and re-run: '+$analysisCommand)
  throw
 }
}