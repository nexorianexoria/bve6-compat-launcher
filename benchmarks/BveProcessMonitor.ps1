param(
 [string]$ProcessName='BveTs',
 [int]$MaxSeconds=180,
 [int]$IntervalSeconds=2,
 [string]$OutputPath=''
)
$ErrorActionPreference='Stop'
if($MaxSeconds -lt 1 -or $IntervalSeconds -lt 1){throw 'MaxSeconds and IntervalSeconds must be >= 1'}
if([string]::IsNullOrWhiteSpace($OutputPath)){
 $OutputPath=Join-Path $PSScriptRoot ('results\BVE-process-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.csv')
}
$directory=Split-Path -Parent $OutputPath
if($directory){New-Item -ItemType Directory -Force -Path $directory|Out-Null}
$start=Get-Date;$prior=@{};$found=$false;$records=New-Object System.Collections.ArrayList
Write-Output ('Monitoring: '+$ProcessName+' for up to '+$MaxSeconds+' seconds')
while(((Get-Date)-$start).TotalSeconds -lt $MaxSeconds){
 $now=Get-Date
 $processes=@(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
 foreach($process in $processes){
  $found=$true
  try{
   $cpuSec=[double]$process.TotalProcessorTime.TotalSeconds
   $cpuPct=$null
   $idKey=[string]$process.Id
   if($prior.ContainsKey($idKey)){
    $elapsed=($now-$prior[$idKey].Time).TotalSeconds
    if($elapsed -gt 0){
     $cpuPct=[Math]::Round(100*($cpuSec-$prior[$idKey].Cpu)/$elapsed/[Environment]::ProcessorCount,1)
     $cpuPct=[Math]::Max(0,$cpuPct)
    }
   }
   $prior[$idKey]=@{Cpu=$cpuSec;Time=$now}
   [void]$records.Add([PSCustomObject]@{
    Timestamp=$now.ToString('o')
    ProcessName=$process.ProcessName
    PID=$process.Id
    CpuPercentOfSystem=$cpuPct
    WorkingSetMB=[Math]::Round($process.WorkingSet64/1MB,1)
    PrivateMemoryMB=[Math]::Round($process.PrivateMemorySize64/1MB,1)
    Threads=$process.Threads.Count
    Handles=$process.HandleCount
   })
  }catch{Write-Warning ('Unable to inspect PID '+$process.Id+': '+$_.Exception.Message)}
 }
 Start-Sleep -Seconds $IntervalSeconds
}
if($records.Count -eq 0){Write-Output 'No matching process was observed. No CSV created.';exit 2}
$records|Export-Csv -LiteralPath $OutputPath -Encoding UTF8 -NoTypeInformation
Write-Output ('Samples='+$records.Count+' File='+$OutputPath)
Write-Output 'This monitor does not measure FPS and does not change process priority or Windows settings.'
