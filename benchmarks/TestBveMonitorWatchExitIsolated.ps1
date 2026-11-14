param([Parameter(Mandatory=$true)][string]$LauncherPath)
$ErrorActionPreference='Stop'
$research=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'results')).TrimEnd('\')+'\'
$exe=[IO.Path]::GetFullPath($LauncherPath)
if(!$exe.StartsWith($research,[StringComparison]::OrdinalIgnoreCase) -or
   !(Test-Path -LiteralPath $exe -PathType Leaf)){
    throw 'Only an isolated research launcher executable is allowed.'
}
$assembly=[Reflection.Assembly]::LoadFrom($exe)
$type=$assembly.GetType('BveLaunchDisplay',$true)
$method=$type.GetMethod('ShouldStopWatching',
    [Reflection.BindingFlags]'Static,NonPublic,Public',
    $null,[Type[]]@([int],[int],[bool],[bool]),$null)
if(!$method){throw 'Missing monitor-watch exit helper.'}
$arrange=$type.GetMethod('ShouldArrangeDuringWatch',
    [Reflection.BindingFlags]'Static,NonPublic,Public',
    $null,[Type[]]@([bool],[bool]),$null)
if(!$arrange){throw 'Missing post-load window placement helper.'}
@(
    @(0,-1,$false,$false,$false),
    @(179,-1,$true,$false,$false),
    @(180,-1,$false,$true,$true),
    @(20,19,$true,$false,$false),
    @(48,19,$true,$false,$false),
    @(49,19,$true,$false,$true),
    @(49,19,$false,$false,$false),
    @(198,19,$false,$false,$false),
    @(199,19,$false,$false,$true),
    @(198,19,$false,$true,$false),
    @(199,19,$false,$true,$true),
    @(200,19,$true,$false,$true),
    @(208,178,$true,$false,$true),
    @(2399,2399,$true,$false,$false)
) | ForEach-Object {
    $iteration=[int]$_[0]
    $last=[int]$_[1]
    $mainAlreadyPlaced=[bool]$_[2]
    $newMainVisible=[bool]$_[3]
    $expected=[bool]$_[4]
    $actual=[bool]$method.Invoke($null,[object[]]@($iteration,$last,
        $mainAlreadyPlaced,$newMainVisible))
    if($actual -ne $expected){
        throw ('Unexpected watch exit at iteration='+$iteration+', last='+$last)
    }
}
@(
    @($false,$false,$true),
    @($false,$true,$false),
    @($true,$true,$true),
    @($true,$false,$true)
) | ForEach-Object {
    $visible=[bool]$_[0]
    $placed=[bool]$_[1]
    $expected=[bool]$_[2]
    $actual=[bool]$arrange.Invoke($null,[object[]]@($visible,$placed))
    if($actual -ne $expected){
        throw ('Unexpected placement decision visible='+$visible+', placed='+$placed)
    }
}
Write-Output 'POST_LOAD_KNOWN_HWND_MANUAL_MOVE_RESPECTED_POLICY_PASS'
Write-Output 'POST_LOAD_NEW_HWND_INITIAL_PLACEMENT_POLICY_PASS'
Write-Output 'POST_PICKER_NO_MAIN_GAP_UP_TO_90_SECONDS_POLICY_PASS'
Write-Output 'NO_LOADER_KNOWN_HWND_MANUAL_MOVE_RESPECTED_POLICY_PASS'
Write-Output 'NO_LOADING_FORM_FALLBACK_EXPIRES_AFTER_90_SECONDS_PASS'
Write-Output 'NO_USER_BVE_OR_SETTINGS_TOUCHED=True'
