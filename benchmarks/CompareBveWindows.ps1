param(
 [Parameter(Mandatory=$true)][string]$BothVisibleCsv,
 [Parameter(Mandatory=$true)][string]$PanelMinimizedCsv,
 [string]$PanelRestoredCsv="",
 [string]$TargetWindow="PanelViewEX",
 [string]$OutputPath=''
)
$ErrorActionPreference='Stop'
function Read-Series([string]$path){
 if(!(Test-Path -LiteralPath $path)){throw 'Capture not found: '+$path}
 $rows=@(Import-Csv -LiteralPath $path)
 if($rows.Count -eq 0){throw 'Capture is empty: '+$path}
 if(!($rows[0].PSObject.Properties.Name -contains 'SwapChainAddress')){throw 'Missing SwapChainAddress column'}
 if(!($rows[0].PSObject.Properties.Name -contains 'TimeInMs')){throw 'Missing TimeInMs column'}
 $map=@{}
 foreach($group in ($rows | Group-Object SwapChainAddress)){
  $values=New-Object 'System.Collections.Generic.List[double]'
  foreach($row in $group.Group){
   $val=0.0
   if([double]::TryParse([string]$row.TimeInMs,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$val)){$values.Add($val)}
  }
  if($values.Count -lt 2){continue}
  $m=$values|Measure-Object -Minimum -Maximum
  $seconds=($m.Maximum-$m.Minimum)/1000.0
  if($seconds -le 0){continue}
  $map[$group.Name]=[PSCustomObject]@{Count=$group.Count;DurationSec=$seconds;PresentRate=$group.Count/$seconds}
 }
 return ,$map
}
$before=Read-Series $BothVisibleCsv
$after=Read-Series $PanelMinimizedCsv
$restored=if([string]::IsNullOrWhiteSpace($PanelRestoredCsv)){@{}}else{Read-Series $PanelRestoredCsv}
$result=@()
$keys=@($before.Keys+$after.Keys+$restored.Keys|Sort-Object -Unique)
foreach($key in $keys){
 $a=$before[$key];$b=$after[$key];$c=$restored[$key]
 $cr=if($null -ne $c){$c.PresentRate}else{$null}
 $br=if($null -ne $a){$a.PresentRate}else{0.0}
 $ar=if($null -ne $b){$b.PresentRate}else{0.0}
 $delta=if($br -gt 0){100.0*($ar-$br)/$br}else{$null}
 $result += [PSCustomObject]@{
  SwapChain=$key
  BothVisiblePresents=if($a){$a.Count}else{0}
  MinimizedPresents=if($b){$b.Count}else{0}
  BothVisibleRate=[Math]::Round($br,2)
  MinimizedRate=[Math]::Round($ar,2)
  ChangePercent=if($null -ne $delta){[Math]::Round($delta,1)}else{$null}
  RestoredRate=if($null -ne $cr){[Math]::Round($cr,2)}else{$null}
 }
}
if($result.Count -eq 0){throw 'No usable swap chain events found'}
Write-Output 'Presentation rates (calls/sec):'
$result | Sort-Object SwapChain | Format-Table -AutoSize | Out-String | Write-Output
$shrunk=@($result|Where-Object {$_.BothVisibleRate -ge 5 -and $_.MinimizedRate -le $_.BothVisibleRate*0.25})
$stable=@($result|Where-Object {$_.BothVisibleRate -ge 5 -and $_.MinimizedRate -ge $_.BothVisibleRate*0.75})
if($shrunk.Count -eq 1 -and $stable.Count -ge 1 -and ([string]::IsNullOrWhiteSpace($PanelRestoredCsv) -or ($null -ne $restored[$shrunk[0].SwapChain] -and $restored[$shrunk[0].SwapChain].PresentRate -ge $shrunk[0].BothVisibleRate*0.75))){
 Write-Output ('One chain slowed markedly after '+$TargetWindow+' was minimized: '+$shrunk[0].SwapChain)
 Write-Output ('Other chain(s) continued at similar rates: '+($stable.SwapChain -join ', '))
 Write-Output 'This is a window-correlated candidate only, not definite identification of an HWND. Restored capture, if supplied, must recover for a positive comparison.'
}else{
 Write-Output 'The captures do not isolate a unique window-associated swap chain.'
 Write-Output 'Potential reasons: rendering continues when minimized, scene changed, other windows affected, or swap chains were recreated.'
}
Write-Output 'No changes to running processes or window state were made by this analysis.'
if(![string]::IsNullOrWhiteSpace($OutputPath)){
 $parent=Split-Path -Parent $OutputPath
 if($parent){New-Item -ItemType Directory -Path $parent -Force|Out-Null}
 $result|Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8
 Write-Output ('Wrote: '+$OutputPath)
}