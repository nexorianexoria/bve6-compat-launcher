param(
    [ValidateRange(100,100000)][int]$Frames=10000,
    [ValidateRange(1,10)][int]$Pairs=4
)
$ErrorActionPreference='Stop'
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
$testDir=Join-Path $root 'dist-b13-perf'
$mock=Join-Path $root 'dist-b5/bridge-mock/mock_ats.dll'
$harness=Join-Path $root 'dist-b5/bridge-mock/bridge_harness.exe'
$baseline=Join-Path $root 'dist-b11-release/bridge'
$candidates=@{
    baseline=@(
        (Join-Path $baseline 'Bve5VehicleCompatibility_.dll'),
        (Join-Path $baseline 'Bve5VehicleCompatibility.exe')
    )
    optimized=@(
        (Join-Path $root 'src/bridge/frontend/x64/Release/frontend.dll'),
        (Join-Path $root 'src/bridge/backend/Release/backend.exe')
    )
}
foreach($f in @($mock,$harness)+$candidates.baseline+$candidates.optimized){
    if(!(Test-Path -LiteralPath $f)){throw ('Missing test file: '+$f)}
}
if(@(Get-Process BveTs -ErrorAction SilentlyContinue).Count){
    throw 'BVE running: offline A/B benchmark must not interrupt a live drive.'
}
New-Item -ItemType Directory -Force -Path $testDir|Out-Null
foreach($name in @('baseline','optimized')){
    $folder=Join-Path $testDir $name
    New-Item -ItemType Directory -Force -Path $folder|Out-Null
    Copy-Item -LiteralPath $candidates[$name][0] -Destination (Join-Path $folder 'Bve5VehicleCompatibility_.dll') -Force
    Copy-Item -LiteralPath $candidates[$name][1] -Destination (Join-Path $folder 'Bve5VehicleCompatibility.exe') -Force
}
$env:BVE5COMPAT_ATS=$mock
$env:BVE5COMPAT_WORKDIR=Split-Path -Parent $mock
$env:BVE5COMPAT_PROFILE='0'
$env:BVE5COMPAT_TEST_ASSERT_BUFFERED='1'
$env:BVE5COMPAT_TEST_ASSERT_HOST_BC_BUFFERED='1'
$samples=@()
try {
    for($round=0;$round -lt $Pairs;$round++){
        $order=if($round%2 -eq 0){@('baseline','optimized')}else{@('optimized','baseline')}
        foreach($name in $order){
            $folder=Join-Path $testDir $name
            $log=Join-Path $folder ('run-'+$round+'.log')
            $env:BVE5COMPAT_LOG=$log
            if(Test-Path $log){Remove-Item $log -Force}
            $output=@(& $harness (Join-Path $folder 'Bve5VehicleCompatibility_.dll') $Frames 2>&1)
            if($LASTEXITCODE -ne 0){
                throw ('Mock ATS round-trip failed: '+$name+' round '+$round+
                    ' exit '+$LASTEXITCODE+' output '+($output -join ' | '))
            }
            if(-not (@($output|Where-Object{$_ -match 'MOCK_ATS_BRIDGE_OK'}).Count)){
                throw 'Mock harness did not confirm success'
            }
            $bridge=@(Select-String -Path $log -Pattern '^METRICS bridge_elapse_frames=\d+ avg_us=(\d+) max_us=(\d+)')
            $hostMetrics=@(Select-String -Path $log -Pattern '^METRICS host_elapse_frames=\d+ avg_us=(\d+) max_us=(\d+)')
            if($bridge.Count -ne 1 -or $hostMetrics.Count -ne 1){
                throw ('Missing ATS timing data: '+$log)
            }
            $sample=[pscustomobject]@{
                version=$name;round=$round;
                bridge_us=[int]$bridge[0].Matches[0].Groups[1].Value
                bridge_max_us=[int]$bridge[0].Matches[0].Groups[2].Value
                host_us=[int]$hostMetrics[0].Matches[0].Groups[1].Value
            }
            $samples+=$sample
            Write-Output ('B13_MOCK_PASS='+$name+' round='+$round+
                ' bridge_avg_us='+$sample.bridge_us+' host_avg_us='+$sample.host_us)
        }
    }
}finally{
    foreach($var in @('BVE5COMPAT_ATS','BVE5COMPAT_WORKDIR','BVE5COMPAT_PROFILE',
        'BVE5COMPAT_LOG','BVE5COMPAT_TEST_ASSERT_BUFFERED',
        'BVE5COMPAT_TEST_ASSERT_HOST_BC_BUFFERED')){
        Remove-Item ('Env:'+ $var) -ErrorAction SilentlyContinue
    }
}
if($samples.Count -ne ($Pairs*2)){throw 'Incomplete benchmark'}
$report=Join-Path $testDir 'b13-mock-ab-results.csv'
$samples|Export-Csv -Path $report -Encoding UTF8 -NoTypeInformation
foreach($name in @('baseline','optimized')){
    $values=@($samples |Where-Object version -eq $name|
        Sort-Object bridge_us|Select-Object -ExpandProperty bridge_us)
    Write-Output ('B13_ROUNDTRIP_'+$name.ToUpper()+'_MEDIAN_US='+$values[[int][math]::Floor($values.Count/2)])
}
Write-Output ('B13_OFFLINE_RESULTS='+$report)
