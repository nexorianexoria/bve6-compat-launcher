param(
 [Parameter(Mandatory=$true)][string]$BeforeCsv,
 [Parameter(Mandatory=$true)][string]$BeforeSwapChain,
 [Parameter(Mandatory=$true)][string]$AfterCsv,
 [Parameter(Mandatory=$true)][string]$AfterSwapChain,
 [string]$OutputPath=''
)
$ErrorActionPreference='Stop'
function Measure-Chain([string]$file,[string]$chain,[string]$label){
 if(!(Test-Path -LiteralPath $file)){throw ('Missing CSV: '+$file)}
 $rows=@(Import-Csv -LiteralPath $file | Where-Object {$_.SwapChainAddress -eq $chain})
 if($rows.Count -lt 2){throw ('No chain '+$chain+' in '+$file)}
 $ms=New-Object 'System.Collections.Generic.List[double]'
 $times=New-Object 'System.Collections.Generic.List[double]'
 foreach($r in $rows){
  $v=0.0
  if([double]::TryParse([string]$r.MsBetweenPresents,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$v) -and $v -gt 0){$ms.Add($v)}
  if([double]::TryParse([string]$r.TimeInMs,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$v)){$times.Add($v)}
 }
 if($ms.Count -lt 2 -or $times.Count -lt 2){throw 'Missing timing data'}
 $sorted=$ms.ToArray();[Array]::Sort($sorted)
 $duration=$times|Measure-Object -Minimum -Maximum
 $sec=($duration.Maximum-$duration.Minimum)/1000.0
 if($sec -le 0){throw 'Invalid capture duration'}
 $p95=$sorted[[Math]::Max(0,[int][Math]::Ceiling($sorted.Length*0.95)-1)]
 $p99=$sorted[[Math]::Max(0,[int][Math]::Ceiling($sorted.Length*0.99)-1)]
 $mean=($ms|Measure-Object -Sum).Sum/$ms.Count
 return [pscustomobject]@{
  Label=$label;Chain=$chain;Samples=$ms.Count;Seconds=[Math]::Round($sec,2)
  PresentsPerSec=[Math]::Round($rows.Count/$sec,2)
  MeanMs=[Math]::Round($mean,2)
  P95Ms=[Math]::Round($p95,2);P99Ms=[Math]::Round($p99,2)
  Over33Ms=($ms|Where-Object {$_ -gt 33.33}|Measure-Object).Count
  Over50Ms=($ms|Where-Object {$_ -gt 50}|Measure-Object).Count
 }
}
$measurements=@(
 (Measure-Chain $BeforeCsv $BeforeSwapChain 'b3/baseline')
 (Measure-Chain $AfterCsv $AfterSwapChain 'b4/experimental')
)
Write-Output 'Same route, vehicle, viewpoint and driving segment are REQUIRED for a meaningful comparison.'
Write-Output 'These are presentation events; they do not prove how many frames were visible on screen.'
$measurements|Format-Table Label,Chain,Samples,Seconds,PresentsPerSec,MeanMs,P95Ms,P99Ms,Over33Ms,Over50Ms -AutoSize|Out-String|Write-Output
if($measurements[0].Seconds -gt 0 -and $measurements[1].Seconds -gt 0){
 $a=$measurements[0];$b=$measurements[1]
 Write-Output ('Change: presents/sec '+[Math]::Round($b.PresentsPerSec-$a.PresentsPerSec,2)+', P99 frame time '+[Math]::Round($b.P99Ms-$a.P99Ms,2)+' ms')
}
if(![string]::IsNullOrWhiteSpace($OutputPath)){
 $dir=Split-Path -Parent $OutputPath;if($dir){New-Item -ItemType Directory -Force -Path $dir|Out-Null}
 $measurements|Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8
 Write-Output ('Saved '+$OutputPath)
}