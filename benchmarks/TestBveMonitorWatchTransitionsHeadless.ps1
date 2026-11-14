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
$flags=[Reflection.BindingFlags]'Static,NonPublic,Public'
$stopMethod=$type.GetMethod('ShouldStopWatching',$flags,$null,
    [Type[]]@([int],[int],[bool],[bool]),$null)
$arrangeMethod=$type.GetMethod('ShouldArrangeDuringWatch',$flags,$null,
    [Type[]]@([bool],[bool]),$null)
if(!$stopMethod -or !$arrangeMethod){throw 'Monitor watch policy methods not found.'}

# A headless sequence of the SAME policy decisions taken in ArrangeWhenReady.
# No window, display, BVE process, preferences or installed launcher is touched.
function Reset-Watch {
    $script:lastLoading=-1
    $script:placedMain=New-Object 'System.Collections.Generic.HashSet[string]'
    $script:placedPicker=New-Object 'System.Collections.Generic.HashSet[string]'
    $script:stopped=$false
}
function Window([string]$id,[bool]$picker,[bool]$onTarget,[bool]$failed=$false){
    return @{Id=$id;Picker=$picker;OnTarget=$onTarget;FailPlacement=$failed}
}
function Step([int]$tick,[bool]$loading,[object[]]$windows){
    if($script:stopped){return 'STOPPED'}
    $hasPicker=$false
    $knownMain=$false
    $freshMain=$false
    foreach($w in $windows){
        if($w.Picker){$hasPicker=$true;continue}
        if($script:placedMain.Contains([string]$w.Id)){$knownMain=$true}
        else{$freshMain=$true}
    }
    $loadingOrPicker=$loading -or $hasPicker
    if($loadingOrPicker){$script:lastLoading=$tick}
    $stop=[bool]$stopMethod.Invoke($null,[object[]]@(
        $tick,$script:lastLoading,$knownMain,$freshMain))
    if($stop){$script:stopped=$true;return 'STOP'}
    $actions=New-Object 'System.Collections.Generic.List[string]'
    foreach($w in $windows){
        $id=[string]$w.Id
        if($w.Picker){$set=$script:placedPicker}else{$set=$script:placedMain}
        $known=$set.Contains($id)
        if($w.Picker -and $known){[void]$actions.Add('PICKER_ALREADY_PLACED');continue}
        if($w.OnTarget){
            [void]$set.Add($id)
            [void]$actions.Add('ON_TARGET:'+ $id)
            continue
        }
        $move=[bool]$arrangeMethod.Invoke($null,[object[]]@($loadingOrPicker,$known))
        if(!$move){[void]$actions.Add('LEAVE_USER_MOVE:'+ $id);continue}
        if($w.FailPlacement){
            [void]$actions.Add('FAILED_FIRST_PLACEMENT:'+ $id)
        }else{
            [void]$set.Add($id)
            [void]$actions.Add('MOVE:'+ $id)
        }
    }
    if($actions.Count -eq 0){return 'WAIT'}
    return $actions -join ','
}
function Expect([string]$case,[string]$actual,[string]$expected){
    if($actual -ne $expected){
        throw ($case+': expected ['+$expected+'], got ['+$actual+']')
    }
}

# Long loading: the same main HWND must be recovered on re-maximize.
Reset-Watch
Expect 'initial loading' (Step 0 $true @((Window 'main' $false $false))) 'MOVE:main'
for($i=1;$i -lt 400;$i++){
    $null=Step $i $true @((Window 'main' $false $true))
}
Expect 'same-HWND reset in loading' (Step 400 $true @((Window 'main' $false $false))) 'MOVE:main'
Expect 'manual move immediately after loading' (Step 401 $false @((Window 'main' $false $false))) 'LEAVE_USER_MOVE:main'
for($i=402;$i -lt 430;$i++){
    $null=Step $i $false @((Window 'main' $false $false))
}
Expect 'postload 15s stop' (Step 430 $false @((Window 'main' $false $false))) 'STOP'
Write-Output 'LONG_LOAD_SAME_HWND_RECOVERY_AND_POSTLOAD_USER_MOVE_PASS'

# Picker HWND is NOT a previously placed main HWND. The main can first appear
# after a 20-second or 60-second no-window gap.
foreach($delay in @(40,120)){
    Reset-Watch
    Expect 'picker placement' (Step 0 $true @((Window 'picker' $true $false))) 'MOVE:picker'
    for($i=1;$i -lt $delay;$i++){
        Expect ('empty gap '+$i) (Step $i $false @()) 'WAIT'
    }
    Expect ('late main at '+$delay) (Step $delay $false @((Window 'new-main' $false $false))) 'MOVE:new-main'
    Expect ('stop after first main at '+$delay) (Step ($delay+1) $false @((Window 'new-main' $false $true))) 'STOP'
}
Write-Output 'PICKER_TO_MAIN_GAPS_20S_60S_PASS'

# Windows can recycle an HWND after destroying a picker. Placement status
# belonging to the picker must never suppress first placement of a main HWND.
Reset-Watch
Expect 'picker same id placed' (Step 0 $true @((Window 'recycled' $true $false))) 'MOVE:recycled'
Expect 'main same id as picker' (Step 1 $false @((Window 'recycled' $false $false))) 'MOVE:recycled'
Write-Output 'PICKER_HWND_REUSED_AS_NEW_MAIN_FIRST_PLACEMENT_PASS'

# New HWND after loader disappearance gets a first placement while an old
# already-positioned HWND is left on the user's chosen monitor.
Reset-Watch
Expect 'initial main' (Step 0 $true @((Window 'old' $false $false))) 'MOVE:old'
Expect 'old main left alone' (Step 1 $false @((Window 'old' $false $false))) 'LEAVE_USER_MOVE:old'
Expect 'new main placed' (Step 2 $false @((Window 'new' $false $false))) 'MOVE:new'
Expect 'new main stop' (Step 30 $false @((Window 'new' $false $true))) 'STOP'
Write-Output 'RECREATED_HWND_FIRST_PLACEMENT_AND_MANUAL_MOVE_PASS'

# Without any observed loading form, a manual move of an already positioned
# HWND is preserved; the existing 90-second fallback still terminates.
Reset-Watch
Expect 'no-loader initial placement' (Step 0 $false @((Window 'main' $false $false))) 'MOVE:main'
Expect 'no-loader manual move' (Step 1 $false @((Window 'main' $false $false))) 'LEAVE_USER_MOVE:main'
Expect 'no-loader tick179' (Step 179 $false @((Window 'main' $false $false))) 'LEAVE_USER_MOVE:main'
Expect 'no-loader tick180' (Step 180 $false @((Window 'main' $false $false))) 'STOP'
Write-Output 'NO_LOADER_MANUAL_MOVE_AND_90S_FALLBACK_PASS'

# A newly visible loader refreshes the expiration, without overriding an
# already positioned main HWND during the preceding gap.
Reset-Watch
Expect 'initial picker' (Step 0 $true @((Window 'picker' $true $false))) 'MOVE:picker'
for($i=1;$i -lt 20;$i++){$null=Step $i $false @()}
Expect 'new loading form' (Step 20 $true @((Window 'main' $false $false))) 'MOVE:main'
Expect 'loading exited' (Step 21 $false @((Window 'main' $false $true))) 'ON_TARGET:main'
Expect 'expiration resets after new loading' (Step 49 $false @((Window 'main' $false $false))) 'LEAVE_USER_MOVE:main'
Expect 'expiration after reset' (Step 50 $false @((Window 'main' $false $false))) 'STOP'
Write-Output 'LOADER_REAPPEARS_AND_RESETS_GRACE_PASS'

# A continuously failing placement MUST NOT keep a 90s gap open for 20 min.
Reset-Watch
Expect 'loader seen' (Step 0 $true @()) 'WAIT'
for($i=1;$i -lt 180;$i++){
    Expect ('failed placement '+$i) (Step $i $false @((Window 'unplaced' $false $false $true))) 'FAILED_FIRST_PLACEMENT:unplaced'
}
Expect 'failed placement bounded' (Step 180 $false @((Window 'unplaced' $false $false $true))) 'STOP'
Write-Output 'PERSISTENT_UNPLACED_HWND_90S_UPPER_BOUND_PASS'

# At exactly 90s, no initial placement is attempted. This is a documented
# finite fallback, not a claim that actual BVE timing cannot exceed it.
Reset-Watch
$null=Step 0 $true @()
for($i=1;$i -lt 180;$i++){$null=Step $i $false @()}
Expect 'late after 90s' (Step 180 $false @((Window 'too-late' $false $false))) 'STOP'
Write-Output 'KNOWN_LIMITATION_MAIN_FIRST_APPEARS_AFTER_90S'

# While loading remains visibly active, the app cannot distinguish a manual
# move from BVE's own re-maximize of the same HWND; it will move either one.
Reset-Watch
$null=Step 0 $true @((Window 'ambiguous' $false $false))
Expect 'same HWND while loading' (Step 1 $true @((Window 'ambiguous' $false $false))) 'MOVE:ambiguous'
Write-Output 'KNOWN_LIMITATION_USER_VS_BVE_MOVE_WHILE_LOADING_UNDISTINGUISHABLE'
Write-Output 'HEADLESS_MONITOR_WATCH_EVENT_SERIES_PASS'
