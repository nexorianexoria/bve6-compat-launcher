$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$archive=Join-Path $root 'dist-b13-perf/tools/DSOALRuntime/DSOAL.7z'
$srcRoot=Join-Path $root 'dist-b13-perf/tools/DSOALRuntime/extracted/DSOAL'
$results=Join-Path $root 'benchmarks/results/B13DsoalFocus'
$csc='C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
$expected='51552E3D192D0432DA54DF75E20CF1EE720D7906207D6BEF8F72869CA73C2096'
if(!(Test-Path $archive) -or (Get-FileHash $archive -Algorithm SHA256).Hash -ne $expected){
    throw 'Official GitHub DSOAL archive missing or SHA256 mismatch'
}
New-Item -ItemType Directory -Path $results -Force | Out-Null
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'B13DirectSoundFocusProbe.cs'))
$anchor='            return allValid?0:3;'
if(!$source.Contains($anchor)){throw 'Synthetic probe source changed unexpectedly'}
$inject='            foreach(System.Diagnostics.ProcessModule m in System.Diagnostics.Process.GetCurrentProcess().Modules) { if (m.ModuleName.Equals("dsound.dll",StringComparison.OrdinalIgnoreCase) || m.ModuleName.Equals("dsoal-aldrv.dll",StringComparison.OrdinalIgnoreCase)) Console.WriteLine("LOADED_MODULE=" + m.ModuleName + " PATH=" + m.FileName); }'
$source=$source.Replace($anchor,$inject+[Environment]::NewLine+$anchor)
$instrumented=Join-Path $results 'DSOAL-Module-Probe.cs'
[IO.File]::WriteAllText($instrumented,$source,[Text.UTF8Encoding]::new($false))
$config='<?xml version="1.0"?><configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>'
foreach($t in @(
    @{Version=5;Architecture='x86';Dir='Win32';Original='C:\Program Files (x86)\mackoy\BveTs5'},
    @{Version=6;Architecture='x64';Dir='Win64';Original='C:\Program Files\mackoy\BveTs6'}
)){
    $dest=Join-Path $results ('BVE'+$t.Version)
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    foreach($dll in @('dsound.dll','dsoal-aldrv.dll')){
        Copy-Item -LiteralPath (Join-Path (Join-Path $srcRoot $t.Dir) $dll) -Destination $dest -Force
    }
    $installedBve=Join-Path $t.Original 'BveTs.exe'
    $slimdx=Join-Path $t.Original 'SlimDX.dll'
    $beforeBve=(Get-FileHash $installedBve -Algorithm SHA256).Hash
    $beforeSlimdx=(Get-FileHash $slimdx -Algorithm SHA256).Hash
    $probe=Join-Path $dest ('BVE'+$t.Version+'-dsoal-runtime-test.exe')
    & $csc /nologo /target:exe ('/platform:'+$t.Architecture) ('/out:'+$probe) /reference:System.Windows.Forms.dll ('/reference:'+$slimdx) $instrumented
    if($LASTEXITCODE -ne 0){throw 'Compiling DSOAL test failed'}
    [IO.File]::WriteAllText($probe+'.config',$config,[Text.UTF8Encoding]::new($false))
    $response=& $probe $slimdx
    if($LASTEXITCODE -ne 0){throw 'Isolated DirectSound test failed'}
    $text=$response -join [Environment]::NewLine
    if(!$text.Contains('BUFFER_FLAG_ROUNDTRIP=PASS') -or
       !$text.Contains(('PATH='+$dest+'\DSOUND.dll')) -or
       !$text.Contains(('PATH='+$dest+'\dsoal-aldrv.dll'))){
        throw 'DSOAL did not actually load from the isolated application folder'
    }
    if((Get-FileHash $installedBve -Algorithm SHA256).Hash -ne $beforeBve -or
       (Get-FileHash $slimdx -Algorithm SHA256).Hash -ne $beforeSlimdx){
        throw 'Original BVE/SlimDX changed during testing'
    }
    Write-Output ('BVE'+$t.Version+'_DSOAL_ACTUALLY_LOADED_AND_FOCUS_FLAGS_PASS')
}
Write-Output 'ACTUAL_BVE_GAMEPLAY_AUDIO_NOT_TESTED'
