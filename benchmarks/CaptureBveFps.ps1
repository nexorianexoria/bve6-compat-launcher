param(
 [ValidateRange(5,300)][int]$Seconds=30,
 [string]$OutputPath='',
 [switch]$QpcTime,
 [switch]$TerminateOnProcExit,
 [string]$PresentMonPath='',
 [int]$BvePid=0
)
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($PresentMonPath)){$tool=Join-Path $PSScriptRoot 'tools\PresentMon-2.5.1-x64.exe'}else{$tool=[IO.Path]::GetFullPath($PresentMonPath)}
if(!(Test-Path -LiteralPath $tool)){throw 'PresentMon.exe was not found in benchmarks\tools.'}
$signature=Get-AuthenticodeSignature -LiteralPath $tool
if($signature.Status -ne 'Valid'){throw 'The downloaded PresentMon program is not signed properly. Do not run it.'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=New-Object Security.Principal.WindowsPrincipal($identity)
if(!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
 Write-Warning 'FPS recording needs administrator privileges to create a Windows ETW trace.'
 Write-Output 'Open PowerShell as Administrator, then run:'
 Write-Output ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "'+$PSCommandPath+'" -Seconds '+$Seconds)
 exit 5
}
if($BvePid -lt 0){throw 'BvePid must not be negative'}
if($BvePid -gt 0){
 $process=@(Get-Process -Id $BvePid -ErrorAction SilentlyContinue | Where-Object {$_.ProcessName -eq 'BveTs'})
 if($process.Count -ne 1){throw ('The requested BVE PID '+$BvePid+' is not running.')}
}else{
 $process=@(Get-Process -Name BveTs -ErrorAction SilentlyContinue | Where-Object {
  try{ $_.Path -match '\\BveTs[56]?\\BveTs.exe$' }catch{ $false }
 })
 if($process.Count -ne 1){throw 'Start exactly one BVE process before recording.'}
}
try{
 $exePath=$process[0].Path
 if([string]::IsNullOrWhiteSpace($exePath) -or
    [IO.Path]::GetFileName($exePath) -ne 'BveTs.exe'){
  throw 'The selected process executable is not BveTs.exe.'
 }
}catch{throw ('Cannot confirm BVE process executable for PID '+$process[0].Id+': '+$_.Exception.Message)}
if([string]::IsNullOrWhiteSpace($OutputPath)){
 $OutputPath=Join-Path $PSScriptRoot ('results\BVE-FPS-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.csv')
}
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force|Out-Null
Write-Output ('Recording BVE PID '+$process[0].Id+' for '+$Seconds+' seconds')
Write-Output ('Destination: '+$OutputPath)
# PresentMon passively monitors present events; no process priority, GPU or BVE settings are changed.
$args=@('--process_id',[string]$process[0].Id,'--timed',[string]$Seconds,'--terminate_after_timed','--no_console_stats','--output_file',('"'+$OutputPath+'"'))
if($QpcTime){$args+='--qpc_time';Write-Output 'QPC capture active (CPUStartQPC column).'}
if($TerminateOnProcExit){$args+='--terminate_on_proc_exit'}
$child=Start-Process -FilePath $tool -ArgumentList $args -Wait -PassThru -NoNewWindow
if($child.ExitCode -ne 0){Write-Warning ('PresentMon exited with code '+$child.ExitCode);exit $child.ExitCode}
if(!(Test-Path -LiteralPath $OutputPath)){Write-Warning 'No frame events were captured';exit 3}
Write-Output 'FPS capture complete. Use SummarizeBveFps.ps1 on this CSV to inspect the result.'