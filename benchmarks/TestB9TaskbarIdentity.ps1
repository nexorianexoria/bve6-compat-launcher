$ErrorActionPreference='Stop'
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'dist-b9-release/BVE6互換ランチャー.exe'))
$id=$assembly.GetType('TaskbarIdentity',$true)
$shell=$assembly.GetType('InstallShell',$true)
$flags=[Reflection.BindingFlags]'NonPublic,Public,Static,Instance'
$identity=[string]$id.GetField('AppId',$flags).GetRawConstantValue()
if($identity -ne 'Nexorianexoria.BVE6CompatibilityLauncher'){throw 'Unstable taskbar application ID'}
$scratch=Join-Path $root ('benchmarks/b9-link-test-'+[guid]::NewGuid().ToString('N'))
$targetDir=Join-Path $scratch 'BVE6互換ランチャー'
$shortcutDir=Join-Path $scratch 'shortcuts'
New-Item -ItemType Directory -Path $targetDir,$shortcutDir | Out-Null
try {
    Copy-Item (Join-Path $root 'dist-b9-release/LauncherStarter.exe') -Destination $targetDir
    $method=$shell.GetMethod('Shortcut',$flags)
    $null=$method.Invoke($null,[object[]]@([string]$shortcutDir,[string]$targetDir))
    $lnk=Join-Path $shortcutDir 'BVE6互換ランチャー.lnk'
    if(!(Test-Path $lnk)){throw 'Start menu shortcut not created'}
    $ws=New-Object -ComObject WScript.Shell
    $created=$ws.CreateShortcut($lnk)
    if($created.TargetPath -ne (Join-Path $targetDir 'LauncherStarter.exe')){
        throw 'Shortcut points to a versioned executable instead of stable starter'
    }
    $foreign=Join-Path $scratch 'other'
    New-Item -ItemType Directory -Path $foreign | Out-Null
    $created.TargetPath=Join-Path $foreign 'someone-else.exe'
    $created.Save()
    Write-Output 'B9_STABLE_TASKBAR_SHORTCUT_TARGET_TEST_OK'
}finally{
    if(Test-Path $scratch){Remove-Item -LiteralPath $scratch -Recurse -Force}
}
