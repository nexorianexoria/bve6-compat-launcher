$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$sandboxRoot=Join-Path $root 'dist-b13-perf/audio-focus-sandbox'
$moduleRoots=@{
    5='C:\Program Files (x86)\mackoy\BveTs5'
    6='C:\Program Files\mackoy\BveTs6'
}
foreach($version in @(5,6)){
    $dir=Join-Path $sandboxRoot ('BVE'+$version)
    $manifestFile=Join-Path $dir 'sandbox-manifest.json'
    if(!(Test-Path -LiteralPath $manifestFile)){throw ('No BVE'+$version+' manifest')}
    $manifest=[IO.File]::ReadAllText($manifestFile)|ConvertFrom-Json
    $real=$moduleRoots[$version]
    if($manifest.version -ne ('BVE'+$version) -or
        $manifest.originalInstallation -ne $real){throw ('Wrong BVE'+$version+' manifest')}
    foreach($property in $manifest.originalFiles.PSObject.Properties){
        $name=$property.Name
        $original=Join-Path $real $name
        $copied=Join-Path $dir $name
        if(!(Test-Path -LiteralPath $original) -or !(Test-Path -LiteralPath $copied)){
            throw ('Missing BVE'+$version+' file '+$name)
        }
        $expected=$property.Value
        if((Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -ne $expected -or
           (Get-FileHash -LiteralPath $copied -Algorithm SHA256).Hash -ne $expected){
            throw ('Changed vendor BVE'+$version+' file '+$name)
        }
    }
    foreach($property in $manifest.dsoalFiles.PSObject.Properties){
        $dll=Join-Path $dir $property.Name
        if(!(Test-Path -LiteralPath $dll) -or
           (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash -ne $property.Value){
            throw ('DSOAL DLL hash mismatch: '+$dll)
        }
    }
    $typeOf=@{'BVE5'=0x014c;'BVE6'=0x8664}
    foreach($name in @('BveTs.exe','dsound.dll','dsoal-aldrv.dll')){
        $path=Join-Path $dir $name
        $stream=[IO.File]::OpenRead($path)
        try{
            $reader=New-Object IO.BinaryReader($stream)
            [void]$stream.Seek(60,[IO.SeekOrigin]::Begin)
            $peHeader=$reader.ReadInt32()
            [void]$stream.Seek(($peHeader+4),[IO.SeekOrigin]::Begin)
            $machine=$reader.ReadUInt16()
        }finally{$stream.Dispose()}
        $expectedMachine=$typeOf[('BVE'+$version)]
        if($machine -ne $expectedMachine){
            throw ('BVE'+$version+' wrong architecture '+$name+' PE='+$machine)
        }
    }
    $launchScript=Join-Path $dir 'Start-BVE-Audio-Test.cmd'
    if(!(Test-Path -LiteralPath $launchScript)){throw 'Missing sandbox launcher'}
    Write-Output ('BVE'+$version+'_ALL_VENDOR_HASHES_AND_DSOAL_ARCHITECTURE_PASS')
}
Write-Output 'BVE_NOT_LAUNCHED_BACKGROUND_AUDIO_NOT_YET_VERIFIED'
