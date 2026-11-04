$ErrorActionPreference='Stop'
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
if(@(Get-Process BveTs -ErrorAction SilentlyContinue).Count){
    throw 'BVE is active. Do not compete for CPU during a game.'
}
$harness=Join-Path $root 'dist-b5/bridge-mock/bridge_harness.exe'
$mock=Join-Path $root 'dist-b5/bridge-mock/mock_ats.dll'
$base=Join-Path $root 'dist-b13-perf'
$env:BVE5COMPAT_ATS=$mock
$env:BVE5COMPAT_WORKDIR=Split-Path -Parent $mock
$env:BVE5COMPAT_PROFILE='0'
$env:BVE5COMPAT_TEST_ASSERT_BUFFERED='1'
$env:BVE5COMPAT_TEST_ASSERT_HOST_BC_BUFFERED='1'
$env:BVE5COMPAT_TEST_MOCK_SOUND_CYCLE='7'
try{
    foreach($cadence in @(16667,33333)){
        $env:BVE5COMPAT_TEST_HARNESS_CADENCE_US=[string]$cadence
        foreach($version in @('baseline','optimized')){
            $dir=Join-Path $base $version
            $env:BVE5COMPAT_LOG=Join-Path $dir ('cadence-'+$cadence+'.log')
            if(Test-Path $env:BVE5COMPAT_LOG){Remove-Item $env:BVE5COMPAT_LOG -Force}
            $output=@(& $harness (Join-Path $dir 'Bve5VehicleCompatibility_.dll') 180 2>&1)
            if($LASTEXITCODE -ne 0 -or -not
                @($output|Where-Object{$_ -match '^MOCK_ATS_BRIDGE_OK frames=180$'}).Count){
                throw ('ATS cadence regression failed: '+$version+' '+$cadence+
                    ' exit='+$LASTEXITCODE+' output='+($output -join '|'))
            }
            Write-Output ('CADENCE_OK '+$version+' target_us='+$cadence)
        }
    }
}finally{
    foreach($name in @('BVE5COMPAT_ATS','BVE5COMPAT_WORKDIR','BVE5COMPAT_PROFILE',
        'BVE5COMPAT_TEST_ASSERT_BUFFERED','BVE5COMPAT_TEST_ASSERT_HOST_BC_BUFFERED',
        'BVE5COMPAT_TEST_MOCK_SOUND_CYCLE',
        'BVE5COMPAT_TEST_HARNESS_CADENCE_US','BVE5COMPAT_LOG')){
        Remove-Item ('Env:'+ $name) -ErrorAction SilentlyContinue
    }
}
Write-Output 'B13_CADENCE_BUFFER_AND_SOUND_REGRESSION_PASS'
