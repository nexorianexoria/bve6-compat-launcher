$ErrorActionPreference='Stop'
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
$b9Assembly=Join-Path $root 'dist-b9-release/BVE6互換ランチャー.exe'
$b9Package=Join-Path $root 'downloads/0.29b9/BVE6Launcher-package.zip'
$b9Json=[IO.File]::ReadAllText((Join-Path $root 'downloads/0.29b9/package.json'))
$b11Package=Join-Path $root 'downloads/0.29b11/BVE6Launcher-package.zip'
$b11Json=[IO.File]::ReadAllText((Join-Path $root 'update.json'))
foreach($file in @($b9Assembly,$b9Package,$b11Package)){
    if(!(Test-Path -LiteralPath $file)){throw ('Missing test input: '+$file)}
}
$old=([Diagnostics.FileVersionInfo]::GetVersionInfo($b9Assembly)).FileVersion
if($old -ne '0.2.9.20'){throw 'Old b9 release binary is not v0.29b9'}
$assembly=[Reflection.Assembly]::LoadFrom($b9Assembly)
$flags=[Reflection.BindingFlags]'NonPublic,Public,Static'
$auto=$assembly.GetType('AutoUpdate',$true)
$find=$auto.GetMethod('FindNewer',$flags)
$installType=$assembly.GetType('InstallEngine',$true)
$parse=$installType.GetMethod('ParseManifest',$flags)
$install=$installType.GetMethod('Install',$flags)
$active=$installType.GetMethod('ActiveTarget',$flags)
$current=[Version]'0.2.9.20'
$b9Manifest=$parse.Invoke($null,[object[]]@($b9Json,$current,'0.2.9.20'))
$b11Manifest=$find.Invoke($null,[object[]]@($b11Json,$current))
if($b11Manifest -eq $null){throw 'Existing b9 cannot detect the b11 update'}
$manifestType=$b11Manifest.GetType()
$versionField=$manifestType.GetProperty('version').GetValue($b11Manifest)
if($versionField -ne '0.2.9.22'){throw 'Existing b9 detected the wrong update'}
$newNoUpdate=$find.Invoke($null,[object[]]@($b11Json,[Version]'0.2.9.22'))
if($newNoUpdate -ne $null){throw 'b11 incorrectly detects itself as an update'}
Write-Output 'B9_AUTOMATIC_UPDATE_DISCOVERY_TO_B11_OK'
$scratch=Join-Path $root ('benchmarks/b11-upgrade-'+[Guid]::NewGuid().ToString('N'))
$destination=Join-Path $scratch 'BVE6互換ランチャー'
try{
    $arguments=New-Object 'object[]' 4
    $arguments[0]=[string]$destination
    $arguments[1]=[string]$b9Package
    $arguments[2]=$b9Manifest
    $arguments[3]=$null
    $oldInstalled=$install.Invoke($null,$arguments)
    if((Get-Item $oldInstalled).VersionInfo.FileVersion -ne '0.2.9.20'){
        throw 'Baseline b9 installation failed'
    }
    $arguments[1]=[string]$b11Package
    $arguments[2]=$b11Manifest
    $newInstalled=$install.Invoke($null,$arguments)
    if((Get-Item $newInstalled).VersionInfo.FileVersion -ne '0.2.9.22'){
        throw 'b9 did not install the updated b11 version'
    }
    if($newInstalled -ne $active.Invoke($null,[object[]]@([string]$destination))){
        throw 'Updated version is not the active launcher'
    }
    if((Get-FileHash $newInstalled).Hash -ne
        (Get-FileHash (Join-Path $root 'dist-b11-release/BVE6互換ランチャー.exe')).Hash){
        throw 'Updated launcher binary is not the validated b11 binary'
    }
    $newStarter=Join-Path $destination 'LauncherStarter.exe'
    if(!(Test-Path $newStarter) -or
        (Get-FileHash $newStarter).Hash -ne
        (Get-FileHash (Join-Path $root 'dist-b11-release/LauncherStarter.exe')).Hash){
        throw 'Pinned taskbar launcher starter was not updated'
    }
    if(!(Test-Path $oldInstalled)){throw 'Old version was not retained during update'}
    Write-Output 'B9_TO_B11_OFFLINE_UPGRADE_INSTALL_AND_POINTER_OK'
    Write-Output 'B9_TO_B11_PINNED_STARTER_REPLACEMENT_OK'
}finally{
    if(Test-Path -LiteralPath $scratch){
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}
