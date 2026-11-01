param(
 [Parameter(Mandatory=$true)][string]$AssemblyPath,
 [Parameter(Mandatory=$true)][string]$Scenarios,
 [string]$Label='baseline',
 [int]$Repeat=2
)
$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath).Path)
$program=$assembly.GetType('Program',$true)
$flags=[Reflection.BindingFlags]'Static, Public, NonPublic'
$program.GetField('Data',$flags).SetValue($null,(Join-Path $PSScriptRoot 'results\data'))
$scan=$program.GetMethod('ScanBoth',$flags)
$catalog=$assembly.GetType('CatalogData',$true)
$build=$catalog.GetMethods($flags) | Where-Object { $_.Name -eq 'Build' -and $_.GetParameters().Length -eq 4 } | Select-Object -First 1
if(!$scan -or !$build){throw 'Methods not found'}
$results=@()
for($i=1;$i -le $Repeat;$i++){
 [GC]::Collect();[GC]::WaitForPendingFinalizers()
 $t=[Diagnostics.Stopwatch]::StartNew()
 $scanResult=$scan.Invoke($null,[object[]]@($Scenarios,[Threading.CancellationToken]::None))
 $t.Stop();$scanMs=$t.ElapsedMilliseconds
 $routes=$scanResult.Scenarios;$vehicles=$scanResult.Vehicles
 $t.Restart()
 $rargs=New-Object object[] 4;$rargs[0]=$routes;$rargs[1]=$Scenarios;$rargs[2]=$false;$rargs[3]=$null
 $vargs=New-Object object[] 4;$vargs[0]=$vehicles;$vargs[1]=$Scenarios;$vargs[2]=$true;$vargs[3]=$null
 $rdata=$build.Invoke($null,$rargs)
 $vdata=$build.Invoke($null,$vargs)
 $t.Stop();$buildMs=$t.ElapsedMilliseconds
 $memory=[GC]::GetTotalMemory($false)
 $row=[PSCustomObject]@{label=$Label;iteration=$i;scenarios=$routes.Count;vehicles=$vehicles.Count;scanMs=$scanMs;metadataMs=$buildMs;totalMs=$scanMs+$buildMs;managedBytes=$memory}
 $results+=$row
 Write-Output ($row | ConvertTo-Json -Compress)
}
$dir=Join-Path $PSScriptRoot 'results';New-Item -ItemType Directory -Force -Path $dir|Out-Null
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dir ($Label+'.json')) -Encoding UTF8
