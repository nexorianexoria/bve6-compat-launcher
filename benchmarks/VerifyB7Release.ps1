$ErrorActionPreference='Stop'
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
$asset=Join-Path $root 'downloads\0.29b7'
$file=Join-Path $asset 'BVE6Launcher-package.zip'
$manifestJson=[IO.File]::ReadAllText((Join-Path $asset 'package.json'))
$manifest=$manifestJson|ConvertFrom-Json
if($manifest.version -ne '0.2.9.18' -or $manifest.displayVersion -ne '0.29b7'){throw 'Wrong version in manifest'}
if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $manifest.sha256){throw 'Hash mismatch'}
$indexFile=Join-Path $asset 'SHA256SUMS.txt'
foreach($line in [IO.File]::ReadAllLines($indexFile)){
    if($line -notmatch '^([a-f0-9]{64})  (downloads/0\.29b7/.+)$'){throw 'Checksum record malformed'}
    $path=Join-Path $root $Matches[2]
    if(!(Test-Path -LiteralPath $path)){throw ('Missing asset: '+$Matches[2])}
    if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Matches[1]){throw ('Asset hash differs: '+$Matches[2])}
}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'dist-b7-ui\BVE6互換ランチャー.exe'))
$type=$assembly.GetType('InstallEngine',$true)
$flags=[Reflection.BindingFlags]'NonPublic,Public,Static'
$parse=$type.GetMethod('ParseManifest',$flags)
$install=$type.GetMethod('Install',$flags)
$parsed=$parse.Invoke($null,[object[]]@([string]$manifestJson,[Version]'0.2.9.18',[string]'0.2.9.18'))
$scratch=Join-Path $root ('benchmarks\b7-install-check-'+[guid]::NewGuid().ToString('N'))
$destination=Join-Path $scratch 'BVE6互換ランチャー'
try {
    $arguments=New-Object 'object[]' 4
    $arguments[0]=[string]$destination
    $arguments[1]=[string]$file
    $arguments[2]=$parsed
    $arguments[3]=$null
    $installed=[string]$install.Invoke($null,$arguments)
    if(!(Test-Path -LiteralPath $installed)){throw 'Installer did not create launcher'}
    if([Diagnostics.FileVersionInfo]::GetVersionInfo($installed).FileVersion -ne '0.2.9.18'){throw 'Installed version differs'}
    if(!(Test-Path (Join-Path $destination '現在.txt')) -or !(Test-Path (Join-Path $destination 'LauncherStarter.exe'))){throw 'Installation pointer/starter missing'}
    Write-Output 'B7_OFFLINE_INSTALL_AND_SHA256_TESTS_OK'
}finally{
    if(Test-Path -LiteralPath $scratch){Remove-Item -LiteralPath $scratch -Recurse -Force}
}
