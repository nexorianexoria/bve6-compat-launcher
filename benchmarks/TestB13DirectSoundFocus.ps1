# Run the synthetic DirectSound flag experiment for both installed BVE versions.
# No BVE binaries, audio settings, system services, or launch preferences change.
$ErrorActionPreference='Stop'
$base=Split-Path -Parent $PSScriptRoot
$resultFolder=Join-Path $PSScriptRoot 'results\B13DirectSoundFocus'
New-Item -Path $resultFolder -ItemType Directory -Force | Out-Null
$src=Join-Path $PSScriptRoot 'B13DirectSoundFocusProbe.cs'
$csc='C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
if(!(Test-Path $csc)){throw 'C# compiler unavailable'}
$targets=@(
    @{Version='BVE5';Arch='x86';Root='C:\Program Files (x86)\mackoy\BveTs5'},
    @{Version='BVE6';Arch='x64';Root='C:\Program Files\mackoy\BveTs6'}
)
foreach($t in $targets){
    $exe=Join-Path $t.Root 'BveTs.exe'
    $slimdx=Join-Path $t.Root 'SlimDX.dll'
    if(!(Test-Path $exe) -or !(Test-Path $slimdx)){
        Write-Host "$($t.Version): SKIPPED (BVE/SlimDX not installed)"
        continue
    }
    $beforeExe=(Get-FileHash $exe -Algorithm SHA256).Hash
    $beforeSlim=(Get-FileHash $slimdx -Algorithm SHA256).Hash
    $out=Join-Path $resultFolder ($t.Version+'-focus-probe.exe')
    & $csc '/nologo' '/target:exe' ('/platform:'+$t.Arch) ('/out:'+$out) "/reference:System.Windows.Forms.dll" ('/reference:'+$slimdx) $src
    if($LASTEXITCODE -ne 0){throw "$($t.Version) compile failed"}
    # The installed BVE5 SlimDX is a .NET 2 mixed-mode assembly. Reproduce
    # its documented runtime activation policy in the probe's OWN .config.
    $config='<?xml version="1.0"?><configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" /></startup></configuration>'
    [IO.File]::WriteAllText($out+'.config',$config,[Text.Encoding]::UTF8)
    & $out $slimdx
    if($LASTEXITCODE -ne 0){throw "$($t.Version) focus probe failed: $LASTEXITCODE"}
    if((Get-FileHash $exe -Algorithm SHA256).Hash -ne $beforeExe){throw 'BVE original file changed'}
    if((Get-FileHash $slimdx -Algorithm SHA256).Hash -ne $beforeSlim){throw 'Original SlimDX changed'}
    Write-Host "$($t.Version): ORIGINAL_FILES_UNCHANGED=PASS"
}
