param(
 [string]$CsvPath='',
 [string]$LauncherDataDirectory='',
 [string]$OutputPath='',
 [switch]$WaitForExit,
 [ValidateRange(1,7200)][int]$ExitWaitSeconds=3600
)
$ErrorActionPreference='Stop'

# Operates on files only. It never changes or closes a BVE process.
$packageRoot=Split-Path -Parent $PSScriptRoot
if([string]::IsNullOrWhiteSpace($CsvPath)){
 $resultDir=Join-Path $PSScriptRoot 'results'
 $latest=Get-ChildItem -LiteralPath $resultDir -Recurse -File -Filter 'b5-qpc-presentmon.csv' -ErrorAction SilentlyContinue |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1
 if($null -eq $latest){throw 'No FPS CSV found. Pass -CsvPath to an existing QPC capture.'}
 $CsvPath=$latest.FullName
}
if(!(Test-Path -LiteralPath $CsvPath -PathType Leaf)){throw ('FPS CSV missing: '+$CsvPath)}
$CsvPath=(Resolve-Path -LiteralPath $CsvPath).Path
$header=Get-Content -LiteralPath $CsvPath -TotalCount 1
if($header -notmatch '(^|,)CPUStartQPC(,|$)'){throw 'FPS CSV must contain CPUStartQPC (capture with -QpcTime).'}
$metaPath=Join-Path (Split-Path -Parent $CsvPath) 'capture-metadata.txt'
if(!(Test-Path -LiteralPath $metaPath)){throw 'Matching capture-metadata.txt is missing. Cannot safely match the BVE process.'}
$metadata=@{}
foreach($line in (Get-Content -LiteralPath $metaPath)){
 $idx=$line.IndexOf('=')
 if($idx -gt 0){$metadata[$line.Substring(0,$idx)]=$line.Substring($idx+1)}
}
[int]$capturePid=0
if(![int]::TryParse([string]$metadata.bve_pid,[ref]$capturePid) -or $capturePid -le 0){
 throw 'capture-metadata.txt does not contain a valid BVE process id.'
}
# Check that all captured rows really belong to the BVE PID in the metadata.
# Do not silently analyze frames from another BVE process.
$csvPids=@(Import-Csv -LiteralPath $CsvPath | Select-Object -ExpandProperty ProcessID -Unique)
if($csvPids.Count -ne 1 -or [string]$csvPids[0] -ne [string]$capturePid){
 throw ('PresentMon CSV ProcessID values ('+($csvPids -join ',')+
  ') do not match capture-metadata BVE PID '+$capturePid+'. Analysis refused.')
}
if([string]::IsNullOrWhiteSpace($LauncherDataDirectory)){
 $LauncherDataDirectory=Join-Path $packageRoot '作業データ'
}
if(!(Test-Path -LiteralPath $LauncherDataDirectory -PathType Container)){
 throw ('Launcher working-data folder missing: '+$LauncherDataDirectory)
}
$logDir=Join-Path $LauncherDataDirectory 'logs'
if(!(Test-Path -LiteralPath $logDir -PathType Container)){throw ('Launcher logs missing: '+$logDir)}
$captureStart=[datetimeoffset]::MinValue
$captureEnd=[datetimeoffset]::MaxValue
if(-not [datetimeoffset]::TryParse([string]$metadata.start_utc,[ref]$captureStart) -or
    -not [datetimeoffset]::TryParse([string]$metadata.end_utc,[ref]$captureEnd)){
 throw 'Capture start/end UTC timestamps missing or invalid.'
}
if($captureEnd -lt $captureStart){throw 'Capture metadata has invalid timestamp order.'}

$matched=@()
foreach($session in (Get-ChildItem -LiteralPath $logDir -File -Filter 'launcher-*.log'|Sort-Object LastWriteTime -Descending)){
 $lines=Get-Content -LiteralPath $session.FullName -Encoding UTF8
 $start=@($lines|Where-Object {$_ -match ('\[BVE_STARTED\] pid='+$capturePid+'(?:\s|$)')})
 if($start.Count -ne 1){continue}
 $startLine=$start[0]
 $startStamp=[datetimeoffset]::MinValue
 if($startLine -notmatch '^(\S+) ' -or -not [datetimeoffset]::TryParse($matches[1],[ref]$startStamp)){continue}
 if($startStamp -gt $captureEnd){continue}
 $exit=@($lines|Where-Object {$_ -match ('\[BVE_EXIT\] pid='+$capturePid+'(?:\s|$)')})
 if($exit.Count -eq 1){
  $exitStamp=[datetimeoffset]::MinValue
  if($exit[0] -notmatch '^(\S+) ' -or -not [datetimeoffset]::TryParse($matches[1],[ref]$exitStamp)){continue}
  if($exitStamp -lt $captureStart){continue}
 }
 $prepared=@($lines|Where-Object {$_ -match '\[PREPARED\].*bridgeLog='})
 if($prepared.Count -ne 1){continue}
 $bridgePath=(($prepared[0] -split 'bridgeLog=',2)[1]).Trim()
 if([string]::IsNullOrWhiteSpace($bridgePath)){continue}
 $matched+=[pscustomobject]@{LogPath=$bridgePath;SessionLog=$session.FullName;Exited=$exit.Count -eq 1}
}
if($matched.Count -ne 1){
 throw ('Found '+$matched.Count+' matching launcher sessions for PID '+$capturePid+
  '. For safety, cannot choose a bridge log automatically. Check that the CSV and launcher folder belong to the same run.')
}
$run=$matched[0]
Write-Output ('MATCHED BVE PID '+$capturePid+' session='+$run.SessionLog)
Write-Output ('ATS bridge log: '+$run.LogPath)
if(-not $run.Exited){
 if($WaitForExit){
  $proc=Get-Process -Id $capturePid -ErrorAction SilentlyContinue
  if($null -ne $proc){
   if($proc.ProcessName -ne 'BveTs'){throw 'PID was reused by a different process. Refusing to wait.'}
   Write-Output ('BVE is running. Waiting up to '+$ExitWaitSeconds+' seconds for normal game exit (no forced close).')
   try{Wait-Process -Id $capturePid -Timeout $ExitWaitSeconds -ErrorAction Stop}catch{
    throw 'BVE has not exited in the requested timeout. Exit normally, then re-run this script.'
   }
  }
  # The launcher receives the exit event asynchronously. Avoid selecting an unfinished session.
  $foundExit=$false
  foreach($attempt in 1..20){
   $log=Get-Content -LiteralPath $run.SessionLog -Encoding UTF8
   if($log -match ('\[BVE_EXIT\] pid='+$capturePid+'(?:\s|$)')){$foundExit=$true;break}
   Start-Sleep -Milliseconds 200
  }
  if(-not $foundExit){throw 'BVE exited but launcher termination log is not yet available. Re-run this command.'}
 }else{
  throw 'BVE has not exited yet. Exit the simulator normally and run this command again (or use -WaitForExit).'
 }
}
if(!(Test-Path -LiteralPath $run.LogPath -PathType Leaf)){throw ('ATS log missing: '+$run.LogPath)}
$contents=Get-Content -LiteralPath $run.LogPath
$diagnosticSummary=@($contents|Select-String '^FRONTEND_DIAGNOSTIC_BUFFER_SUMMARY stored=(\d+) dropped=(\d+) capacity=(\d+)$')
if($diagnosticSummary.Count -eq 1){
 $diagnosticMatch=[regex]::Match($diagnosticSummary[0].Line,
    '^FRONTEND_DIAGNOSTIC_BUFFER_SUMMARY stored=(\d+) dropped=(\d+) capacity=(\d+)$')
 $diagnosticDropped=[long]$diagnosticMatch.Groups[2].Value
 if($diagnosticDropped -gt 0){
  Write-Warning ('Frontend diagnostics memory buffer omitted '+
    $diagnosticDropped+' lines. ATS/FPS measurements are stored separately.')
 }
}
$hostSoundSummary=@($contents|Select-String '^BC_SOUND_BUFFER_SUMMARY stored=(\d+) dropped=(\d+) capacity=(\d+)$')
if($hostSoundSummary.Count -eq 1){
 $soundMatch=[regex]::Match($hostSoundSummary[0].Line,
    '^BC_SOUND_BUFFER_SUMMARY stored=(\d+) dropped=(\d+) capacity=(\d+)$')
 $soundDropped=[long]$soundMatch.Groups[2].Value
 if($soundDropped -gt 0){
  Write-Warning ('Host brake-sound diagnostics memory buffer omitted '+
    $soundDropped+' optional log entries. Actual ATS audio data was not discarded.')
 }
}
if(-not @($contents|Select-String '^SLOW_PHASE_SUMMARY ').Count -or
   -not @($contents|Select-String '^METRICS host_elapse_frames=').Count){
 throw 'ATS log metrics are incomplete. Check whether BVE was started in 32-bit ATS compatibility mode.'
}
if([string]::IsNullOrWhiteSpace($OutputPath)){
 $OutputPath=Join-Path (Split-Path -Parent $CsvPath) 'b5-ats-fps-analysis.md'
}
if(Test-Path -LiteralPath $OutputPath){throw ('Output report exists; refusing overwrite: '+$OutputPath)}
$analyzer=Join-Path $PSScriptRoot 'AnalyzeB5Goal.py'
if(!(Test-Path -LiteralPath $analyzer)){throw 'AnalyzeB5Goal.py was not found beside this script'}
Write-Output 'Running offline analysis. No game files or settings will be changed.'
& python $analyzer --csv $CsvPath --bridge-log $run.LogPath --out $OutputPath
if($LASTEXITCODE -ne 0){throw ('Analysis failed with exit code '+$LASTEXITCODE)}
$pipeAnalyzer=Join-Path $PSScriptRoot 'AnalyzePipeStalls.py'
if(Test-Path -LiteralPath $pipeAnalyzer -PathType Leaf){
 $pipeOut=Join-Path (Split-Path -Parent $OutputPath) 'b5-pipe-wait-analysis.md'
 if(Test-Path -LiteralPath $pipeOut){
  throw ('Pipe report exists; refusing overwrite: '+$pipeOut)
 }
 & python $pipeAnalyzer --bridge-log $run.LogPath --out $pipeOut
 if($LASTEXITCODE -ne 0){
  throw ('Pipe timing analysis failed with exit code '+$LASTEXITCODE)
 }
 Write-Output ('PIPE_ANALYSIS_OK='+$pipeOut)
}
$cpuAnalyzer=Join-Path $PSScriptRoot 'AnalyzeB5CpuFrames.py'
if(Test-Path -LiteralPath $cpuAnalyzer -PathType Leaf){
 $cpuOutput=Join-Path (Split-Path -Parent $OutputPath) 'b5-cpu-gpu-frames.md'
 if(Test-Path -LiteralPath $cpuOutput){
  throw ('CPU/GPU report already exists, refusing overwrite: '+$cpuOutput)
 }
 $captureQpc=0L
 $captureFreq=10000000L
 [long]::TryParse([string]$metadata.runner_qpc_start,[ref]$captureQpc)|Out-Null
 [long]::TryParse([string]$metadata.runner_qpc_frequency,[ref]$captureFreq)|Out-Null
 if($captureFreq -le 0){throw 'Invalid capture QPC frequency'}
 & python $cpuAnalyzer --csv $CsvPath --out $cpuOutput --qpc-start $captureQpc --qpc-frequency $captureFreq
 if($LASTEXITCODE -ne 0){throw 'CPU/GPU frame analysis failed'}
 Write-Output ('CPU_GPU_ANALYSIS_OK='+$cpuOutput)
}
Write-Output ('ANALYSIS_OK='+$OutputPath)