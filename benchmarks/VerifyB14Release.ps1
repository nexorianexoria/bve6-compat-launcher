$ErrorActionPreference='Stop'
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
$asset=Join-Path $root 'downloads\0.29b14'
$file=Join-Path $asset 'BVE6Launcher-package.zip'
$manifestJson=[IO.File]::ReadAllText((Join-Path $asset 'package.json'))
$manifest=$manifestJson|ConvertFrom-Json
if($manifest.version -ne '0.2.9.25' -or $manifest.displayVersion -ne '0.29b14'){throw 'Wrong version in manifest'}
if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $manifest.sha256){throw 'Hash mismatch'}
$buildDir=Join-Path $root 'dist-b14-release'
if((Get-FileHash -LiteralPath (Join-Path $asset 'BVE6Launcher-Setup.exe')).Hash -ne
    (Get-FileHash -LiteralPath (Join-Path $buildDir 'BVE6Launcher-Setup.exe')).Hash){
    throw 'Published installer differs from the final release build'
}
# The previous b7 failure was a package built from the stale UI directory.
# Verify the actual bytes inside BOTH distributed archives, not just version metadata.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$package=[IO.Compression.ZipFile]::OpenRead($file)
try {
    foreach($name in @('BVE6互換ランチャー.exe','導入・更新.exe','LauncherStarter.exe','Uninstall.exe')){
        $entry=$package.GetEntry($name)
        if($null -eq $entry){throw ('Missing application executable from release ZIP: '+$name)}
        $tmp=Join-Path $env:TEMP ([guid]::NewGuid().ToString('N')+'.exe')
        try {
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$tmp)
            $actual=(Get-FileHash -LiteralPath $tmp -Algorithm SHA256).Hash
            $expected=(Get-FileHash -LiteralPath (Join-Path $buildDir $name) -Algorithm SHA256).Hash
            if($actual -ne $expected){throw ('STALE BINARY IN RELEASE ZIP: '+$name)}
        }finally{if(Test-Path $tmp){Remove-Item -LiteralPath $tmp -Force}}
    }
}finally{$package.Dispose()}
$portableZip=Join-Path $asset 'BVE6Launcher-0.29b14-Portable.zip'
$portable=[IO.Compression.ZipFile]::OpenRead($portableZip)
try {
    $entry=$portable.Entries | Where-Object {
        $_.Name -eq 'BVE6互換ランチャー.exe'
    } | Select-Object -First 1
    if($null -eq $entry){throw 'Portable launcher not in published archive'}
    $tmp=Join-Path $env:TEMP ([guid]::NewGuid().ToString('N')+'.exe')
    try{
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$tmp)
        if((Get-FileHash $tmp).Hash -ne (Get-FileHash (Join-Path $buildDir 'BVE6互換ランチャー.exe')).Hash){
            throw 'STALE BINARY IN PORTABLE ZIP'
        }
    }finally{if(Test-Path $tmp){Remove-Item $tmp -Force}}
}finally{$portable.Dispose()}
$indexFile=Join-Path $asset 'SHA256SUMS.txt'
foreach($line in [IO.File]::ReadAllLines($indexFile)){
    if($line -notmatch '^([a-f0-9]{64})  (downloads/0\.29b14/.+)$'){throw 'Checksum record malformed'}
    $path=Join-Path $root $Matches[2]
    if(!(Test-Path -LiteralPath $path)){throw ('Missing asset: '+$Matches[2])}
    if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Matches[1]){throw ('Asset hash differs: '+$Matches[2])}
}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'dist-b14-release\BVE6互換ランチャー.exe'))
$type=$assembly.GetType('InstallEngine',$true)
$flags=[Reflection.BindingFlags]'NonPublic,Public,Static'
$parse=$type.GetMethod('ParseManifest',$flags)
$install=$type.GetMethod('Install',$flags)
$parsed=$parse.Invoke($null,[object[]]@([string]$manifestJson,[Version]'0.2.9.25',[string]'0.2.9.25'))
$scratch=Join-Path $root ('benchmarks\b14-install-check-'+[guid]::NewGuid().ToString('N'))
$destination=Join-Path $scratch 'BVE6互換ランチャー'
try {
    $arguments=New-Object 'object[]' 4
    $arguments[0]=[string]$destination
    $arguments[1]=[string]$file
    $arguments[2]=$parsed
    $arguments[3]=$null
    $installed=[string]$install.Invoke($null,$arguments)
    if(!(Test-Path -LiteralPath $installed)){throw 'Installer did not create launcher'}
    if([Diagnostics.FileVersionInfo]::GetVersionInfo($installed).FileVersion -ne '0.2.9.25'){throw 'Installed version differs'}
    if(!(Test-Path (Join-Path $destination '現在.txt')) -or !(Test-Path (Join-Path $destination 'LauncherStarter.exe'))){throw 'Installation pointer/starter missing'}
    if((Get-FileHash $installed).Hash -ne (Get-FileHash (Join-Path $buildDir 'BVE6互換ランチャー.exe')).Hash){
        throw 'Installed launcher differs from the final release build'
    }
    Write-Output 'B14_OFFLINE_INSTALL_ARCHIVE_BINARY_MATCH_AND_SHA256_TESTS_OK'
}finally{
    if(Test-Path -LiteralPath $scratch){Remove-Item -LiteralPath $scratch -Recurse -Force}
}
