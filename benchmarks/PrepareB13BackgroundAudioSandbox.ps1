param(
    [ValidateSet(5,6)][int[]]$Versions=@(5,6)
)
$ErrorActionPreference='Stop'

# Creates an isolated, LOCAL-ONLY test copy. Does not edit the installed BVE,
# its binaries, its configuration, Windows audio, or the public launcher.
$root=Split-Path -Parent $PSScriptRoot
$package=Join-Path $root 'dist-b13-perf/tools/DSOALRuntime/DSOAL.7z'
$extracted=Join-Path $root 'dist-b13-perf/tools/DSOALRuntime/extracted/DSOAL'
$destinationRoot=Join-Path $root 'dist-b13-perf/audio-focus-sandbox'
$expectedPackageHash='51552E3D192D0432DA54DF75E20CF1EE720D7906207D6BEF8F72869CA73C2096'
if(!(Test-Path -LiteralPath $package) -or
   (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $expectedPackageHash){
    throw 'Official DSOAL test archive missing or SHA256 mismatch; aborting'
}
$versionsToCreate=@($Versions | Select-Object -Unique)
foreach($version in $versionsToCreate){
    $installed=if($version -eq 6){
        'C:\Program Files\mackoy\BveTs6'
    }else{
        'C:\Program Files (x86)\mackoy\BveTs5'
    }
    $architecture=if($version -eq 6){'Win64'}else{'Win32'}
    $sourceDsoal=Join-Path $extracted $architecture
    $sandbox=Join-Path $destinationRoot ('BVE'+$version)
    if(Test-Path -LiteralPath $sandbox){
        throw ('Sandbox already exists; refusing overwrite: '+$sandbox)
    }
    foreach($name in @('BveTs.exe','BveTs.exe.config','SlimDX.dll')){
        if(!(Test-Path -LiteralPath (Join-Path $installed $name))){
            throw ('Installed BVE file missing: '+$name)
        }
    }
    foreach($name in @('dsound.dll','dsoal-aldrv.dll')){
        if(!(Test-Path -LiteralPath (Join-Path $sourceDsoal $name))){
            throw ('Verified DSOAL extraction missing: '+$name)
        }
    }
    New-Item -ItemType Directory -Path $sandbox -Force | Out-Null
    $fileHashes=[ordered]@{}
    # Skip BVE5 backup: only copy files the application normally uses.
    $allFiles=Get-ChildItem -LiteralPath $installed -Recurse -File |
        Where-Object { $_.Name -ne 'BveTs.exe.Backup' }
    foreach($file in $allFiles){
        $relative=$file.FullName.Substring($installed.Length).TrimStart('\')
        $target=Join-Path $sandbox $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
        $before=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        $after=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        if($before -ne $after){throw ('Vendor binary copy mismatch: '+$relative)}
        $fileHashes[$relative]=$after
    }
    $originalDlls=@{}
    foreach($name in @('dsound.dll','dsoal-aldrv.dll')){
        $original=Join-Path $sourceDsoal $name
        $copy=Join-Path $sandbox $name
        Copy-Item -LiteralPath $original -Destination $copy
        $originalDlls[$name]=(Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash
    }
    # Keep the dependency's attribution with the isolated test.
    Copy-Item -LiteralPath (Join-Path $extracted 'Documentation/DSOAL-License.txt') -Destination (Join-Path $sandbox 'DSOAL-License.txt')
    Copy-Item -LiteralPath (Join-Path $extracted 'Documentation/DSOAL-ReadMe.txt') -Destination (Join-Path $sandbox 'DSOAL-ReadMe.txt')
    $script=@'
@echo off
setlocal
cd /d "%~dp0"
set "DSOAL_LOGLEVEL=2"
set "DSOAL_LOGFILE=%~dp0dsoal-test.log"
start "" "%~dp0BveTs.exe"
endlocal
'@
    [IO.File]::WriteAllText((Join-Path $sandbox 'Start-BVE-Audio-Test.cmd'),$script,[Text.Encoding]::ASCII)
    $manifest=[ordered]@{
        version=('BVE'+$version)
        originalInstallation=$installed
        originalFiles=$fileHashes
        dsoalFiles=$originalDlls
        dsoalPackageSha256=$expectedPackageHash
        liveGameplayAudioTest='NOT_TESTED'
        note='Local disposable test copy. Do not redistribute bundled BVE binaries.'
    }
    [IO.File]::WriteAllText((Join-Path $sandbox 'sandbox-manifest.json'),
        ($manifest|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
    Write-Output ('BVE'+$version+'_SANDBOX_READY='+$sandbox)
    Write-Output ('BVE'+$version+'_VENDOR_BINARY_HASHES_UNCHANGED=PASS')
}
Write-Output 'SANDBOX_CREATION_ONLY_BVE_NOT_LAUNCHED'
