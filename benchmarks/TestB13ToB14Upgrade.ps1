$ErrorActionPreference='Stop'
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
$oldExe=Join-Path $root 'dist-b13-release/BVE6互換ランチャー.exe'
$oldPackage=Join-Path $root 'downloads/0.29b13/BVE6Launcher-package.zip'
$oldJson=[IO.File]::ReadAllText((Join-Path $root 'downloads/0.29b13/package.json'))
$newExe=Join-Path $root 'dist-b14-release/BVE6互換ランチャー.exe'
$newStarter=Join-Path $root 'dist-b14-release/LauncherStarter.exe'
$newPackage=Join-Path $root 'downloads/0.29b14/BVE6Launcher-package.zip'
$newJson=[IO.File]::ReadAllText((Join-Path $root 'downloads/0.29b14/package.json'))
foreach($file in @($oldExe,$oldPackage,$newExe,$newStarter,$newPackage)){
    if(!(Test-Path -LiteralPath $file)){throw ('Missing test input: '+$file)}
}
if(([Diagnostics.FileVersionInfo]::GetVersionInfo($oldExe)).FileVersion -ne '0.2.9.24'){
    throw 'Baseline executable is not the published b13'
}
$assembly=[Reflection.Assembly]::LoadFrom($oldExe)
$flags=[Reflection.BindingFlags]'NonPublic,Public,Static'
$auto=$assembly.GetType('AutoUpdate',$true)
$find=$auto.GetMethod('FindNewer',$flags)
$engine=$assembly.GetType('InstallEngine',$true)
$parse=$engine.GetMethod('ParseManifest',$flags)
$install=$engine.GetMethod('Install',$flags)
$active=$engine.GetMethod('ActiveTarget',$flags)
$oldManifest=$parse.Invoke($null,[object[]]@($oldJson,[Version]'0.2.9.24','0.2.9.24'))
$newManifest=$find.Invoke($null,[object[]]@($newJson,[Version]'0.2.9.24'))
if($null -eq $newManifest){throw 'Published b13 cannot detect b14 update'}
if($newManifest.GetType().GetProperty('version').GetValue($newManifest) -ne '0.2.9.25'){
    throw 'Wrong update version'
}
if($null -ne $find.Invoke($null,[object[]]@($newJson,[Version]'0.2.9.25'))){
    throw 'b14 incorrectly detects itself as newer'
}
Write-Output 'B13_TO_B14_AUTOMATIC_UPDATE_DISCOVERY_OK'

$scratch=Join-Path $root ('benchmarks/b14-upgrade-'+[Guid]::NewGuid().ToString('N'))
$destination=Join-Path $scratch 'BVE6互換ランチャー'
try{
    $arguments=New-Object 'object[]' 4
    $arguments[0]=[string]$destination
    $arguments[1]=[string]$oldPackage
    $arguments[2]=$oldManifest
    $arguments[3]=$null
    $oldInstalled=[string]$install.Invoke($null,$arguments)
    if(([Diagnostics.FileVersionInfo]::GetVersionInfo($oldInstalled)).FileVersion -ne '0.2.9.24'){
        throw 'Old version installation failed'
    }
    $arguments[1]=[string]$newPackage
    $arguments[2]=$newManifest
    $updated=[string]$install.Invoke($null,$arguments)
    if(([Diagnostics.FileVersionInfo]::GetVersionInfo($updated)).FileVersion -ne '0.2.9.25'){
        throw 'Upgrade has wrong version'
    }
    if($updated -ne $active.Invoke($null,[object[]]@([string]$destination))){
        throw 'Updated executable is not active'
    }
    if((Get-FileHash -LiteralPath $updated).Hash -ne (Get-FileHash -LiteralPath $newExe).Hash){
        throw 'Updated executable differs from release'
    }
    if((Get-FileHash -LiteralPath (Join-Path $destination 'LauncherStarter.exe')).Hash -ne
       (Get-FileHash -LiteralPath $newStarter).Hash){
        throw 'Taskbar starter was not replaced'
    }
    if(!(Test-Path -LiteralPath $oldInstalled)){throw 'The previous release was not retained'}
    Write-Output 'B13_TO_B14_OFFLINE_UPGRADE_AND_STARTER_MATCH_PASS'
}finally{
    if(Test-Path -LiteralPath $scratch){
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}
