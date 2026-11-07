param(
    [Parameter(Mandatory=$true)][string]$LauncherExe,
    [string]$ScenarioRoot='',
    [string]$ReferenceCsv='',
    [Parameter(Mandatory=$true)][string]$OutputCsv
)
$ErrorActionPreference='Stop'
if(@(Get-Process -Name BveTs -ErrorAction SilentlyContinue).Count){
    throw 'BVE is running. Do not compete with a live driving session.'
}
$projectRoot=Split-Path -Parent $PSScriptRoot
if([string]::IsNullOrWhiteSpace($ScenarioRoot)){
    $ScenarioRoot=Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'BveTs\Scenarios'
}
if(!(Test-Path -LiteralPath $LauncherExe -PathType Leaf)){
    throw ('Launcher test executable not found: '+$LauncherExe)
}
if(!(Test-Path -LiteralPath $ScenarioRoot -PathType Container)){
    throw ('Scenario root not found: '+$ScenarioRoot)
}
$outputFull=[IO.Path]::GetFullPath($OutputCsv)
$resultsDir=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'results'))
if(!$outputFull.StartsWith($resultsDir+[IO.Path]::DirectorySeparatorChar,
                          [StringComparison]::OrdinalIgnoreCase)){
    throw 'Preview result CSV must be inside ignored benchmarks/results/'
}
New-Item -ItemType Directory -Path (Split-Path $outputFull) -Force|Out-Null
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($LauncherExe))
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static'
$program=$assembly.GetType('Program',$true)
$program.GetField('Data',$flags).SetValue($null,(Join-Path $resultsDir 'preview-test-data'))
$preview=$assembly.GetType('Previews',$true).GetMethods($flags) | Where-Object {
    $_.Name -eq 'Resolve' -and $_.GetParameters().Length -eq 2
} | Select-Object -First 1
if($null -eq $preview){throw 'Previews.Resolve overload not found'}
$expected=$null
if(![string]::IsNullOrWhiteSpace($ReferenceCsv)){
    $expected=@(Import-Csv -LiteralPath $ReferenceCsv)
    $paths=@($expected|ForEach-Object {$_.Path})
}else{
    $s=New-Object 'object[]' 3
    $s[0]=[IO.Path]::GetFullPath($ScenarioRoot)
    $s[1]=[Threading.CancellationToken]::None
    $s[2]=4
    $scan=$program.GetMethod('ScanBoth',$flags).Invoke($null,$s)
    $paths=@($scan.Vehicles|ForEach-Object {$_.Path}|Sort-Object)
}
$result=New-Object 'System.Collections.Generic.List[object]'
$watch=[Diagnostics.Stopwatch]::StartNew()
foreach($path in $paths){
    $start=[Diagnostics.Stopwatch]::GetTimestamp()
    $value=''
    try{
        $resolved=$preview.Invoke($null,[object[]]@([string]$path,$true))
        if($null -ne $resolved){$value=[string]$resolved}
    }catch [Reflection.TargetInvocationException] {
        # Match failures too. A later candidate must not silently change the result.
        $value='ERROR: '+$_.Exception.InnerException.GetType().FullName
    }
    $durationMs=1000.0*([Diagnostics.Stopwatch]::GetTimestamp()-$start)/
                [Diagnostics.Stopwatch]::Frequency
    $result.Add([pscustomobject]@{
        Path=$path;Preview=$value;ElapsedMs=[Math]::Round($durationMs,3)
    })
    if($result.Count%250 -eq 0){
        Write-Output ('PREVIEW_PROGRESS count='+$result.Count+' of '+$paths.Count)
    }
}
$watch.Stop()
if($null -ne $expected){
    if($expected.Count -ne $result.Count){throw 'Different number of vehicles'}
    for($i=0;$i -lt $expected.Count;$i++){
        if(![string]::Equals([string]$expected[$i].Path,[string]$result[$i].Path,
                            [StringComparison]::OrdinalIgnoreCase) -or
           ![string]::Equals([string]$expected[$i].Preview,[string]$result[$i].Preview,
                            [StringComparison]::OrdinalIgnoreCase)){
            throw ('Preview changed for vehicle '+$expected[$i].Path+
                   ': before='+$expected[$i].Preview+' after='+$result[$i].Preview)
        }
    }
    Write-Output ('PREVIEW_REFERENCE_EXACT_MATCH='+$expected.Count)
}
$result|Export-Csv -LiteralPath $outputFull -NoTypeInformation -Encoding UTF8
Write-Output ('PREVIEW_TOTAL_COUNT='+$result.Count)
Write-Output ('PREVIEW_TOTAL_MS='+$watch.ElapsedMilliseconds)
Write-Output ('PREVIEW_RESULTS='+$outputFull)
