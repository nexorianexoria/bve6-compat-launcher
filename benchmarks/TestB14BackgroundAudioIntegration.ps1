$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
$preview=Join-Path $root 'dist-b14-release'
$binary=Join-Path $preview 'BVE6互換ランチャー.exe'
$audioRoot=Join-Path $root 'third_party/DSOAL'
if(!(Test-Path -LiteralPath $binary)){throw 'Audio preview launcher not built'}
$assembly=[Reflection.Assembly]::LoadFrom($binary)
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$program=$assembly.GetType('Program',$true)
$homeField=$program.GetField('Home',$flags)
$originalHome=[string]$homeField.GetValue($null)
$options=$assembly.GetType('BveWindowOptions',$true)
$optionType=$assembly.GetType('BveWindowOptions+Settings',$true)
$read=$options.GetMethod('ReadFile',$flags)
$save=$options.GetMethod('SaveFile',$flags)
$audio=$assembly.GetType('BveBackgroundAudio',$true)
$stage=$audio.GetMethod('Stage',$flags)
$hasRuntime=$audio.GetMethod('HasTrustedRuntime',$flags,$null,
    [Type[]]@([int],[string]),$null)
$hasEmbedded=$audio.GetMethod('HasTrustedRuntime',$flags,$null,
    [Type[]]@([int]),$null)
$scratch=Join-Path $root ('benchmarks/results/b14-audio-stage-'+[Guid]::NewGuid().ToString('N'))
$fake=Join-Path $scratch 'nonexistent-dsoal'
New-Item -ItemType Directory -Path $scratch -Force|Out-Null
function NewOptions([bool]$top,[bool]$sound){
    $value=[Activator]::CreateInstance($optionType,$true)
    $optionType.GetField('AlwaysOnTop',$flags).SetValue($value,$top)
    $optionType.GetField('BackgroundAudio',$flags).SetValue($value,$sound)
    return $value
}
function ReadFlag($settings,$name){
    return [bool]$optionType.GetField($name,$flags).GetValue($settings)
}
try{
    $homeField.SetValue($null,$preview+[IO.Path]::DirectorySeparatorChar)
    foreach($version in @(5,6)){
        $runtimeArgs=New-Object 'object[]' 2
        $runtimeArgs[0]=[int]$version
        $runtimeArgs[1]=[string]$audioRoot
        if(![bool]$hasRuntime.Invoke($null,$runtimeArgs)){
            throw ('Trusted DSOAL runtime not available for BVE'+$version)
        }
        $runtimeArgs[1]=[string]$fake
        if([bool]$hasRuntime.Invoke($null,$runtimeArgs)){
            throw 'Missing DLLs should not enable background sound'
        }
        $embeddedArgs=New-Object 'object[]' 1
        $embeddedArgs[0]=[int]$version
        if(![bool]$hasEmbedded.Invoke($null,$embeddedArgs)){
            throw ('The b14 launcher does not embed verified DSOAL for BVE'+$version)
        }
    }
    if(Test-Path -LiteralPath (Join-Path $preview 'audio')){
        throw 'Unexpected external audio folder; standalone installer should use embedded libraries'
    }
    Write-Output 'B14_EMBEDDED_AUDIO_RESOURCES_VERIFIED_BVE5_AND_BVE6'

    $xml=Join-Path $scratch 'options.xml'
    $initial=$read.Invoke($null,[object[]]@([string]$xml,6))
    if((ReadFlag $initial 'AlwaysOnTop') -or
       (ReadFlag $initial 'BackgroundAudio')){
        throw 'Independent controls do not default OFF'
    }
    $save.Invoke($null,[object[]]@([string]$xml,6,(NewOptions $true $false)))|Out-Null
    $save.Invoke($null,[object[]]@([string]$xml,5,(NewOptions $false $true)))|Out-Null
    $a=$read.Invoke($null,[object[]]@([string]$xml,6))
    $b=$read.Invoke($null,[object[]]@([string]$xml,5))
    if(!(ReadFlag $a 'AlwaysOnTop') -or (ReadFlag $a 'BackgroundAudio') -or
       (ReadFlag $b 'AlwaysOnTop') -or !(ReadFlag $b 'BackgroundAudio')){
        throw 'BVE5/BVE6 or topmost/audio flags are not independent'
    }
    Write-Output 'TOPMOST_AND_AUDIO_OPTIONS_PERSIST_INDEPENDENTLY'

    $dialogType=$assembly.GetType('BveOptionsDialog',$true)
    $constructor=$dialogType.GetConstructor($flags,$null,
        [Type[]]@([string],[string]),$null)
    $exe5='C:\Program Files (x86)\mackoy\BveTs5\BveTs.exe'
    $exe6='C:\Program Files\mackoy\BveTs6\BveTs.exe'
    $dialog=$constructor.Invoke([object[]]@($exe5,$exe6))
    try{
        $check=$dialogType.GetField('backgroundAudio',$flags).GetValue($dialog)
        $top=$dialogType.GetField('alwaysTop',$flags).GetValue($dialog)
        if($check -isnot [Windows.Forms.CheckBox] -or
           $top -isnot [Windows.Forms.CheckBox] -or
           !$check.Enabled -or !$check.Text.Contains('試験機能')){
            throw 'Experimental audio checkbox not enabled with trusted runtime'
        }
        Write-Output 'BACKGROUND_AUDIO_UI_CHECKBOX_ENABLED_IN_PREVIEW'
    }finally{$dialog.Dispose()}

    foreach($pair in @(@(5,$exe5),@(6,$exe6))){
        $version=[int]$pair[0]
        $original=[string]$pair[1]
        $oldHash=(Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash
        $destinationRoot=Join-Path $scratch 'isolated-runs'
        $stageArgs=New-Object 'object[]' 4
        $stageArgs[0]=[int]$version
        $stageArgs[1]=[string]$original
        $stageArgs[2]=[string]$audioRoot
        $stageArgs[3]=[string]$destinationRoot
        $copy=[string]$stage.Invoke($null,$stageArgs)
        if(!(Test-Path -LiteralPath $copy) -or
           (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne $oldHash){
            throw ('BVE'+$version+' staged executable differs from installed file')
        }
        $copiedDir=Split-Path -Parent $copy
        $architecture=if($version -eq 5){'Win32'}else{'Win64'}
        foreach($dll in @('dsound.dll','dsoal-aldrv.dll')){
            $sourceDll=Join-Path (Join-Path $audioRoot $architecture) $dll
            $stagedDll=Join-Path $copiedDir $dll
            if((Get-FileHash -LiteralPath $stagedDll -Algorithm SHA256).Hash -ne
               (Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash){
                throw ('BVE'+$version+' staged DLL differs')
            }
        }
        if((Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -ne $oldHash){
            throw ('BVE'+$version+' vendor original was changed')
        }
        Write-Output ('BVE'+$version+'_COPY_AND_AUDIO_DLLS_MATCH_ORIGINALS')

        # Installers keep the old b13 allowlist: the new release must work
        # with NO separate audio/ folders beside the executable.
        $embeddedArgs=New-Object 'object[]' 4
        $embeddedArgs[0]=[int]$version
        $embeddedArgs[1]=[string]$original
        $embeddedArgs[2]=$null
        $embeddedArgs[3]=[string](Join-Path $scratch 'embedded-only-runs')
        $embeddedBve=[string]$stage.Invoke($null,$embeddedArgs)
        if((Get-FileHash -LiteralPath $embeddedBve -Algorithm SHA256).Hash -ne
           $oldHash){throw ('BVE'+$version+' embedded-only BVE copy does not match')}
        foreach($dll in @('dsound.dll','dsoal-aldrv.dll')){
            $sourceDll=Join-Path (Join-Path $audioRoot $architecture) $dll
            $targetDll=Join-Path (Split-Path -Parent $embeddedBve) $dll
            if((Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash -ne
               (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash){
                throw ('BVE'+$version+' embedded DSOAL bytes differ from verified source')
            }
        }
        Write-Output ('BVE'+$version+'_EMBEDDED_ONLY_STAGING_AND_HASH_MATCH_PASS')
    }

    # Switching the experimental mode OFF must use the original process
    # startup path. A harmless cmd.exe is used; no BVE window is opened here.
    $startCore=$audio.GetMethod('StartCore',$flags)
    $processInfo=New-Object Diagnostics.ProcessStartInfo
    $processInfo.FileName=Join-Path $env:WINDIR 'System32/cmd.exe'
    $processInfo.Arguments='/c exit 0'
    $processInfo.UseShellExecute=$false
    $processInfo.CreateNoWindow=$true
    $offRunRoot=Join-Path $scratch 'no-staging-when-off'
    $coreArgs=New-Object 'object[]' 5
    $coreArgs[0]=[Diagnostics.ProcessStartInfo]$processInfo.PSObject.BaseObject
    $coreArgs[1]=[int]6
    $coreArgs[2]=[bool]$false
    $coreArgs[3]=[string]$fake
    $coreArgs[4]=[string]$offRunRoot
    $normalProcess=[Diagnostics.Process]$startCore.Invoke($null,$coreArgs)
    try{
        if(!$normalProcess.WaitForExit(10000) -or
           $normalProcess.ExitCode -ne 0 -or
           (Test-Path -LiteralPath $offRunRoot)){
            throw 'Experimental audio OFF unexpectedly staged or failed to launch'
        }
    }finally{$normalProcess.Dispose()}
    Write-Output 'AUDIO_OFF_NORMAL_PROCESS_START_NO_STAGING_PASS'

    # A missing or tampered third-party dependency must fail closed.
    $badArgs=New-Object 'object[]' 4
    $badArgs[0]=[int]6
    $badArgs[1]=[string]$exe6
    $badArgs[2]=[string]$fake
    $badArgs[3]=[string](Join-Path $scratch 'failed-stage')
    $failedClosed=$false
    try { [void]$stage.Invoke($null,$badArgs) }
    catch {
        if($_.Exception.ToString().Contains('検証済みのDSOAL音声ファイルがありません')){
            $failedClosed=$true
        }else{throw}
    }
    if(!$failedClosed -or (Test-Path -LiteralPath $badArgs[3])){
        throw 'Missing DLLs must not trigger a launch or create a BVE copy'
    }
    Write-Output 'MISSING_DSOAL_FAILS_CLOSED_WITHOUT_STAGING_PASS'

    $cleanup=$audio.GetMethod('CleanupPreviousTestRuns',$flags)
    $cleanupRoot=Join-Path $scratch 'cleanup-test'
    $oldSandbox=Join-Path $cleanupRoot ('BVE6-'+[Guid]::NewGuid().ToString('N'))
    $unmarkedSandbox=Join-Path $cleanupRoot ('BVE5-'+[Guid]::NewGuid().ToString('N'))
    $unrelated=Join-Path $cleanupRoot 'do-not-delete'
    New-Item -ItemType Directory -Path $oldSandbox,$unmarkedSandbox,$unrelated -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $oldSandbox 'old-log.txt'),'old test data')
    [IO.File]::WriteAllText((Join-Path $oldSandbox 'background-audio-sandbox.marker'),
        'BVE_BACKGROUND_AUDIO_TEST_COPY_V1')
    [IO.File]::WriteAllText((Join-Path $unmarkedSandbox 'protected.txt'),'retain')
    [IO.File]::WriteAllText((Join-Path $unrelated 'protected.txt'),'retain')
    [void]$cleanup.Invoke($null,[object[]]@([string]$cleanupRoot))
    if((Test-Path -LiteralPath $oldSandbox) -or
        !(Test-Path -LiteralPath (Join-Path $unmarkedSandbox 'protected.txt')) -or
        !(Test-Path -LiteralPath (Join-Path $unrelated 'protected.txt'))){
        throw 'Audio-run cleanup removed an unrelated directory or retained stale staging'
    }
    Write-Output 'ONLY_MARKED_STALE_TEST_COPIES_ARE_REMOVED_PASS'
    Write-Output 'BVE_BACKGROUNDAUDIO_INTEGRATION_SYNTHETIC_PASS'
}finally{
    $homeField.SetValue($null,$originalHome)
    if(Test-Path -LiteralPath $scratch){
        $resolved=[IO.Path]::GetFullPath($scratch)
        $expected=[IO.Path]::GetFullPath((Join-Path $root 'benchmarks/results'))
        if(!$resolved.StartsWith($expected+[IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)){
            throw 'Refusing to delete outside the isolated results directory'
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
