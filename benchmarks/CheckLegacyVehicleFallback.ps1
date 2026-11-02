param(
  [Parameter(Mandatory=$true)][string]$ScenarioPath,
  [string]$Bve5Executable = "${env:ProgramFiles(x86)}\mackoy\BveTs5\BveTs.exe"
)
$ErrorActionPreference = 'Stop'
# Read-only validation. This does not launch BVE or modify any user data.
function Assert-File([string]$path) {
  if(!(Test-Path -LiteralPath $path -PathType Leaf)) {
    throw ('Missing file: '+$path)
  }
}
function Read-Data([string]$path) {
  return [IO.File]::ReadAllLines($path,[Text.Encoding]::GetEncoding(932))
}
function Get-Setting([string[]]$lines,[string]$key) {
  $pattern='^\s*'+[regex]::Escape($key)+'\s*=\s*(.*?)\s*(?:[;#].*)?$'
  foreach($line in $lines) {
    if($line -match $pattern) {return $Matches[1].Trim()}
  }
  return ''
}
function Resolve-Relative([string]$source,[string]$relative) {
  return [IO.Path]::GetFullPath([IO.Path]::Combine([IO.Path]::GetDirectoryName($source),$relative))
}
Assert-File $Bve5Executable
Assert-File $ScenarioPath
$scenario=Read-Data $ScenarioPath
if($scenario[0] -notmatch '^\s*BveTs Scenario (1|2)\.00') {throw 'Not a BVE5/6 scenario'}
$vehicleName=Get-Setting $scenario 'Vehicle'
if(!$vehicleName -or $vehicleName.Contains('|')){throw 'Select a fixed-vehicle scenario'}
$vehicle=Resolve-Relative $ScenarioPath $vehicleName
Assert-File $vehicle
$definition=Read-Data $vehicle
if($definition[0] -notmatch '^\s*BveTs Vehicle (0\.03|1\.00|2\.00)') {throw 'Unsupported vehicle definition'}
$atsName=Get-Setting $definition 'Ats'
if(!$atsName){$atsName=Get-Setting $definition 'Ats32'}
if(!$atsName){throw 'No 32-bit ATS defined for vehicle'}
$ats=Resolve-Relative $vehicle $atsName
Assert-File $ats
$bytes=[IO.File]::ReadAllBytes($ats)
if([BitConverter]::ToUInt16($bytes,0) -ne 0x5a4d){throw 'Not an executable DLL'}
$pe=[BitConverter]::ToInt32($bytes,0x3c)
if([BitConverter]::ToUInt16($bytes,$pe+4) -ne 0x14c){throw 'The ATS is not x86'}
$soundName=Get-Setting $definition 'Sound'
if(!$soundName){throw 'No vehicle sound file'}
$sound=Resolve-Relative $vehicle $soundName
Assert-File $sound
$config=Read-Data $sound
$section=''
$atsSounds=@{}
foreach($line in $config) {
  if($line -match '^\s*\[([^]]+)\]'){$section=$Matches[1];continue}
  if($section -eq 'Ats' -and $line -match '^\s*(\d+)\s*=\s*(.+?)\s*$') {
    $index=[int]$Matches[1]
    $file=Resolve-Relative $sound $Matches[2]
    $atsSounds[$index]=$file
  }
}
$hornParameters=Join-Path ([IO.Path]::GetDirectoryName($ats)) 'HornParameter.txt'
$hornIndices=@()
if(Test-Path $hornParameters){
  foreach($line in Read-Data $hornParameters){
    if($line -match 'HornSoundIndex\s*=\s*(\d+)'){$hornIndices+=([int]$Matches[1])}
  }
  foreach($index in ($hornIndices|Select-Object -Unique)){
    if(!$atsSounds.ContainsKey($index)) {throw ('Missing sound mapping for ATS index '+$index)}
    Assert-File $atsSounds[$index]
  }
}
Write-Output ('BVE5_EXE='+$Bve5Executable)
Write-Output ('SCENARIO='+$ScenarioPath)
Write-Output ('VEHICLE='+$vehicle)
Write-Output ('DIRECT_ATS_X86='+$ats)
Write-Output ('HORN_ATS_INDICES='+($hornIndices -join ','))
Write-Output 'BVE5_FALLBACK_STATIC_PATHS_OK'
Write-Output 'Audio playback and keyboard response are NOT verified; no BVE process was started.'
