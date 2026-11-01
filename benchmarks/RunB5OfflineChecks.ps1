param(
 [string]$BaselineFrontendPath='',
 [ValidateRange(1000,100000)][int]$Frames=12000,
 [ValidateRange(1,10)][int]$Pairs=3,
 [string]$OutputDirectory='',
 [switch]$SkipBuild,
 [switch]$SkipPacedRegression
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$baselineDefault=Join-Path $root 'dist-b5\benchmark-baseline\frontend-prebuffer.dll'
if([string]::IsNullOrWhiteSpace($BaselineFrontendPath)){
 $BaselineFrontendPath=$baselineDefault
}
if(!(Test-Path -LiteralPath $BaselineFrontendPath -PathType Leaf)){
 throw ('Pre-buffered reference DLL missing. Set -BaselineFrontendPath to a previously compiled b5 frontend: '+$BaselineFrontendPath)
}
if([IO.Path]::GetFullPath($BaselineFrontendPath) -eq [IO.Path]::GetFullPath($baselineDefault)){
 $expected='DC993F0790AE11D7A58AC5C5BC739E1C4F85E56728F6A19A9133D324AEAFD42B'
 if((Get-FileHash -LiteralPath $BaselineFrontendPath -Algorithm SHA256).Hash -ne $expected){
  throw 'Local pre-buffered reference DLL hash changed. Refusing an incorrectly labeled A/B comparison.'
 }
}
if(@(Get-Process -Name BveTs -ErrorAction SilentlyContinue).Count){
 throw 'BVE is running. Offline checks avoid concurrent game activity.'
}
Write-Output 'BVE-free offline checks: using a deterministic test-only 32-bit ATS.'
Write-Output 'This workflow does not start BVE or load route/vehicle assets.'
$fixtureDir=Join-Path $root 'dist-b5\bridge-mock'
if(!$SkipBuild){
 $msbuild='C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
 if(!(Test-Path -LiteralPath $msbuild)){
  throw 'Visual Studio 2022 Community MSBuild was not found. Use -SkipBuild only when current binaries already exist.'
 }
 foreach($item in @(
   @('src\bridge\frontend\frontend.vcxproj','x64'),
   @('src\bridge\backend\backend.vcxproj','Win32')
  )){
  & $msbuild (Join-Path $root $item[0]) /t:Build /p:Configuration=Release ('/p:Platform='+$item[1]) /p:PlatformToolset=v143 /nologo /v:minimal
  if($LASTEXITCODE -ne 0){throw ('Native build failed: '+$item[0])}
 }
 & $msbuild (Join-Path $root 'BVELauncher.UI.csproj') /t:Build /p:Configuration=Release '/p:OutputPath=dist-b5\' /nologo /v:minimal
 if($LASTEXITCODE -ne 0){throw 'Launcher build failed'}
 & cmd.exe /c ('"'+(Join-Path $PSScriptRoot 'fixtures\_run_bridge_mock_b5.cmd')+'"')
 if($LASTEXITCODE -ne 0){throw 'Test-only mock ATS / harness failed to compile'}
}
$frontend=Join-Path $root 'src\bridge\frontend\x64\Release\frontend.dll'
$backend=Join-Path $root 'src\bridge\backend\Release\backend.exe'
$ats=Join-Path $fixtureDir 'mock_ats.dll'
$harness=Join-Path $fixtureDir 'bridge_harness.exe'
foreach($required in @($frontend,$backend,$ats,$harness)){
 if(!(Test-Path -LiteralPath $required -PathType Leaf)){throw ('Test component missing: '+$required)}
}
if([string]::IsNullOrWhiteSpace($OutputDirectory)){
 $OutputDirectory=Join-Path $root ('dist-b5\offline-checks\run-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+
  '-'+[guid]::NewGuid().ToString('N').Substring(0,6))
}
if(Test-Path -LiteralPath $OutputDirectory){throw ('Output folder exists, refusing overwrite: '+$OutputDirectory)}
foreach($test in @(
 'test_b5_offline_ab.py',
 'test_b5_cadence.py',
 'test_b5_host_sound.py',
 'test_b5_fast_array.py',
 'test_b5_hotpath_buffers.py',
 'test_b5_release_defaults.py',
 'test_b5_update_startup.py',
 'test_b5_cpu_frames.py',
 'test_pipe_stalls.py',
 'test_correlate_b5_qpc.py',
 'test_b5_empty_events.py'
)){
 Write-Output ('RUN_UNIT_TEST='+$test)
 & python (Join-Path $PSScriptRoot ('fixtures\'+$test))
 if($LASTEXITCODE -ne 0){throw ('Offline unit test failed: '+$test)}
}
Write-Output ('RUN_MOCK_AB_TEST frames='+$Frames+' paired_repetitions='+$Pairs)
$arguments=@(
 (Join-Path $PSScriptRoot 'CompareB5BufferedDiagnostics.py'),
 '--legacy-frontend', $BaselineFrontendPath,
 '--buffered-frontend', $frontend,
 '--backend', $backend,
 '--mock-ats', $ats,
 '--harness', $harness,
 '--output', $OutputDirectory,
 '--frames', [string]$Frames,
 '--pairs', [string]$Pairs
)
& python @arguments
if($LASTEXITCODE -ne 0){throw ('Mock A/B test failed. Inspect: '+$OutputDirectory)}
if(!(Test-Path -LiteralPath (Join-Path $OutputDirectory 'offline-ab-report.md'))){
 throw 'Offline comparison produced no report'
}
if(@(Get-Process -Name BveTs -ErrorAction SilentlyContinue).Count){
 throw 'BVE became active during offline test. A/B results may be affected by concurrent load.'
}
$hostSoundOutput=Join-Path $OutputDirectory 'host-brake-sound-buffer'
Write-Output 'RUN_HOST_BRAKE_SOUND_BUFFER_REGRESSION with cyclic synthetic sound and overflow'
$hostSoundArgs=@(
 (Join-Path $PSScriptRoot 'VerifyB5HostSoundBuffer.py'),
 '--frontend',$frontend,
 '--backend',$backend,
 '--mock-ats',$ats,
 '--harness',$harness,
 '--output',$hostSoundOutput
)
& python @hostSoundArgs
if($LASTEXITCODE -ne 0){throw ('Host brake-sound diagnostic regression failed. Inspect '+$hostSoundOutput)}
if(!(Test-Path -LiteralPath (Join-Path $hostSoundOutput 'host-sound-buffer-report.md'))){
 throw 'Host brake-sound regression did not produce a report'
}
if(!$SkipPacedRegression){
 foreach($cadenceUs in @(16667,33333)){
  $paced=Join-Path $OutputDirectory ('paced-'+$cadenceUs+'us')
  Write-Output ('RUN_PACED_STALL_REGRESSION cadence_us='+$cadenceUs+
   ' ATS pause=85ms, harness pause=120ms')
  $pacedArgs=@(
   (Join-Path $PSScriptRoot 'RunB5CadenceRegression.py'),
   '--legacy-frontend',$BaselineFrontendPath,
   '--buffered-frontend',$frontend,
   '--backend',$backend,
   '--mock-ats',$ats,
   '--harness',$harness,
   '--output',$paced,
   '--cadence-us',[string]$cadenceUs
  )
  & python @pacedArgs
  if($LASTEXITCODE -ne 0){throw ('Paced ATS/host stall regression failed. Inspect '+$paced)}
  if(!(Test-Path -LiteralPath (Join-Path $paced 'cadence-regression-report.md'))){
   throw 'Paced regression completed without a report'
  }
 }
}
Write-Output ('OFFLINE_CHECKS_OK='+$OutputDirectory)
Write-Output 'No BVE or PresentMon process was launched by this workflow.'