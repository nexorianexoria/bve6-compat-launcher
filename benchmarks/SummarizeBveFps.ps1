param([Parameter(Mandatory=$true)][string]$CsvPath)
$ErrorActionPreference='Stop'
if(!(Test-Path -LiteralPath $CsvPath)){throw 'Capture file not found.'}
$rows=@(Import-Csv -LiteralPath $CsvPath)
if($rows.Count -eq 0){throw 'There are no presentation events in the file.'}
$fields=@($rows[0].PSObject.Properties.Name)
$column=@('MsBetweenPresents','msBetweenPresents')|Where-Object {$fields -contains $_}|Select-Object -First 1
if(!$column){throw ('MsBetweenPresents is missing. Available fields: '+($fields -join ', '))}
function ParseMs([string]$s){
 $v=0.0
 if([double]::TryParse($s,[System.Globalization.NumberStyles]::Float,[System.Globalization.CultureInfo]::InvariantCulture,[ref]$v)){return $v}
 return $null
}
$groups=if($fields -contains 'SwapChainAddress'){@($rows|Group-Object SwapChainAddress)}else{@([pscustomobject]@{Name='unknown';Group=$rows})}
$results=@()
foreach($group in $groups){
 $values=New-Object 'System.Collections.Generic.List[double]'
 $times=New-Object 'System.Collections.Generic.List[double]'
 foreach($row in $group.Group){
  $v=ParseMs ([string]$row.$column)
  if($null -ne $v -and $v -gt 0 -and $v -lt 10000){$values.Add([double]$v)}
  if($fields -contains 'TimeInMs'){
   $t=ParseMs ([string]$row.TimeInMs)
   if($null -ne $t){$times.Add([double]$t)}
  }
 }
 if($values.Count -eq 0){continue}
 $sorted=$values.ToArray()
 [Array]::Sort($sorted)
 $mean=($values|Measure-Object -Sum).Sum/$values.Count
 $p95=$sorted[[Math]::Max(0,[Math]::Min($sorted.Length-1,[int][Math]::Ceiling($sorted.Length*0.95)-1))]
 $p99=$sorted[[Math]::Max(0,[Math]::Min($sorted.Length-1,[int][Math]::Ceiling($sorted.Length*0.99)-1))]
 $durationSec=0.0
 if($times.Count -ge 2){$range=$times|Measure-Object -Minimum -Maximum;$durationSec=($range.Maximum-$range.Minimum)/1000.0}
 $rate=if($durationSec -gt 0){$group.Group.Count/$durationSec}else{1000/$mean}
 $results+= [pscustomobject][ordered]@{
   SwapChain=$group.Name
   Presents=$group.Group.Count
   DurationSec=[Math]::Round($durationSec,2)
   PresentsPerSec=[Math]::Round($rate,1)
   AvgFrameMs=[Math]::Round($mean,2)
   P95FrameMs=[Math]::Round($p95,2)
   P99FrameMs=[Math]::Round($p99,2)
   WorstFrameMs=[Math]::Round($sorted[$sorted.Length-1],2)
   Over33ms=($values|Where-Object {$_ -gt 33.33}|Measure-Object).Count
   Over50ms=($values|Where-Object {$_ -gt 50}|Measure-Object).Count
 }
}
if($results.Count -eq 0){throw 'No valid frame-time values found.'}
Write-Output ('Capture: '+[IO.Path]::GetFileName($CsvPath))
$results | Sort-Object Presents -Descending | Format-List | Out-String | Write-Output
if($results.Count -gt 1){
 Write-Output ('NOTICE: '+$results.Count+' swap chains were recorded. Do NOT combine their FPS or frame times.')
 Write-Output 'The CSV does not identify which swap chain is the BVE driving window.'
}
Write-Output 'PresentsPerSec counts presentation calls; it is not necessarily displayed FPS.'
Write-Output 'P95/P99 are frame-time percentiles (higher is worse). Compare the same route, view and conditions.'
