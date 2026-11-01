$ErrorActionPreference='Stop'
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
$exe=Join-Path $root 'dist-panel-diagnostics\BVE6互換ランチャー.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Build the diagnostic launcher first'}
$assembly=[Reflection.Assembly]::LoadFrom($exe)
$type=$assembly.GetType('PanelViewDiagnostics', $true)
$flags=[Reflection.BindingFlags]'Static,NonPublic,Public'
$parse=$type.GetMethod('TryParseLaunch',$flags)
$args=[object[]]@('pid=12345 exe=C:\Program Files\mackoy\BveTs6\BveTs.exe',0,$null)
if(![bool]$parse.Invoke($null,$args) -or $args[1] -ne 12345 -or $args[2] -ne 'C:\Program Files\mackoy\BveTs6\BveTs.exe'){
    throw 'Launch PID parser failed'
}
$invalid=[object[]]@('not-a-process',0,$null)
if([bool]$parse.Invoke($null,$invalid)){throw 'Invalid launch PID unexpectedly accepted'}
$find=$type.GetMethod('FindExtension',$flags)
$temporary=Join-Path $root ('benchmarks\panelview-diagnostics-check-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    # Entirely synthetic BveEX layout; no installed plug-in or BVE process is needed.
    $fakeExe=Join-Path $temporary 'BveTs.exe'
    [IO.File]::WriteAllBytes($fakeExe,[byte[]]@())
    $devices=New-Item -ItemType Directory -Path (Join-Path $temporary 'Input Devices')
    [IO.File]::WriteAllBytes((Join-Path $devices.FullName 'BveEx.Caller.InputDevice.dll'),[byte[]]@())
    $bveEx=New-Item -ItemType Directory -Path (Join-Path $temporary 'BveEx')
    [IO.File]::WriteAllBytes((Join-Path $bveEx.FullName 'BveEx.Launcher.dll'),[byte[]]@())
    [IO.File]::WriteAllText((Join-Path $devices.FullName 'BveEx.Caller.InputDevice.txt'),$bveEx.FullName)
    $core=New-Item -ItemType Directory -Path (Join-Path $bveEx.FullName '2.0')
    $extensions=New-Item -ItemType Directory -Path (Join-Path $core.FullName 'Extensions')
    [IO.File]::WriteAllBytes((Join-Path $extensions.FullName 'PanelViewEX.dll'),[byte[]]@())
    [IO.File]::WriteAllText((Join-Path $core.FullName 'LoadedExtensions.xml'),'<LoadedExtensions><Extensions><Extension Path="PanelViewEX.dll" IsEnabled="true" /></Extensions></LoadedExtensions>')
    $plugin=[string]$find.Invoke($null,[object[]]@([string]$fakeExe))
    if($plugin -ne (Join-Path $extensions.FullName 'PanelViewEX.dll')){
        throw 'The BveEX extension path was not resolved'
    }
    $capture=$type.GetMethod('Capture',$flags)
    $output=$capture.Invoke($null,[object[]]@(2147483000,[string]$plugin,[string]$temporary))
    if(!(Test-Path -LiteralPath $output)){throw 'No diagnostic report written'}
    $content=[IO.File]::ReadAllText($output)
    if(!$content.Contains('BVE終了済み') -or !$content.Contains('BveEX設定上の有効状態: true')){
        throw 'The report should record a terminated process and enabled extension'
    }
    Write-Output 'PANELVIEW_DIAGNOSTICS_TESTS_OK'
}finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
