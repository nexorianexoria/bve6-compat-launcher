# Runs the unmodified BveWindowOptions.cs in an isolated, synthetic WinForms
# harness. No BVE binary, process or window is started or manipulated.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
# Some hosts point TEMP at a system-owned folder. CodeDom requires a
# writable per-user temp location for its disposable compilation files.
$safeTemp=Join-Path $env:LOCALAPPDATA 'Temp'
if(!(Test-Path -LiteralPath $safeTemp)){throw 'User temp directory is unavailable'}
$env:TEMP=$safeTemp
$env:TMP=$safeTemp

$root=Split-Path -Parent $PSScriptRoot
$original=Join-Path $root 'src\BveWindowOptions.cs'
$source=[IO.File]::ReadAllText($original)
$stubs=@'
internal static class BveLaunchDisplay {
    internal sealed class WindowTarget {
        internal System.IntPtr Handle=System.IntPtr.Zero;
        internal bool ScenarioPicker=false;
    }
    internal static System.Collections.Generic.List<WindowTarget>
        FindBveWindows(int pid) {
        return new System.Collections.Generic.List<WindowTarget>();
    }
}
internal static class BvePreferencesService {
    internal sealed class Snapshot { internal bool Windowed=true; }
    internal static Snapshot Read(int version) { return new Snapshot(); }
}
internal static class Diagnostics {
    internal static void Write(string category,string message) {}
}
internal static class NativePopupAudit {
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern System.IntPtr GetLastActivePopup(System.IntPtr owner);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern bool IsWindowEnabled(System.IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern System.IntPtr GetActiveWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern System.IntPtr SetActiveWindow(System.IntPtr window);
}
'@
$compiled=Add-Type -TypeDefinition ($source+[Environment]::NewLine+$stubs) `
    -ReferencedAssemblies @('System.Xml.Linq.dll','System.Xml.dll','System.Core.dll') -PassThru
$type=$compiled | Where-Object { $_.Name -eq 'BveWindowOptions' } | Select-Object -First 1
if($null -eq $type){throw 'Cannot resolve BveWindowOptions from the original source'}
$flags=[Reflection.BindingFlags]'Static,NonPublic,Public'
$predicate=$type.GetMethod('ShouldRaiseBveDialog',$flags)
$find=$type.GetMethod('FindModalDialogs',$flags)
if($null -eq $predicate -or $null -eq $find){throw 'Modal APIs are missing'}
$popupType=$compiled | Where-Object { $_.Name -eq 'NativePopupAudit' } | Select-Object -First 1
$getLastPopup=$popupType.GetMethod('GetLastActivePopup',$flags)
$nativeEnabled=$popupType.GetMethod('IsWindowEnabled',$flags)
$getActive=$popupType.GetMethod('GetActiveWindow',$flags)
$setActive=$popupType.GetMethod('SetActiveWindow',$flags)

function CheckRule([string]$name,[bool[]]$signals,[bool]$expected) {
    $vector=[object[]]@()
    foreach($item in $signals){$vector+=$item}
    $actual=[bool]$predicate.Invoke($null,$vector)
    if($actual -ne $expected){
        throw ('PREDICATE_FAIL '+$name+': expected='+$expected+' got='+$actual)
    }
    Write-Output ('RULE_OK '+$name)
}

# Arguments: sameProcess,visible,isMainOrPicker,ownedByBve,
#            mainDisabled,errorTitle,standardDialogClass.
CheckRule 'owned_nondialog_with_disabled_main_excluded' ([bool[]]@($true,$true,$false,$true,$true,$false,$false)) $false
CheckRule 'owned_standard_dialog' ([bool[]]@($true,$true,$false,$true,$false,$false,$true)) $true
CheckRule 'owned_error_title' ([bool[]]@($true,$true,$false,$true,$false,$true,$false)) $true
CheckRule 'unowned_error_with_disabled_main_fallback' ([bool[]]@($true,$true,$false,$false,$true,$true,$false)) $true
CheckRule 'unowned_error_while_main_enabled' ([bool[]]@($true,$true,$false,$false,$false,$true,$false)) $false
CheckRule 'unowned_standard_dialog' ([bool[]]@($true,$true,$false,$false,$true,$false,$true)) $false
CheckRule 'owned_normal_window_while_main_enabled' ([bool[]]@($true,$true,$false,$true,$false,$false,$false)) $false
CheckRule 'wrong_pid' ([bool[]]@($false,$true,$false,$true,$true,$true,$true)) $false
CheckRule 'hidden_window' ([bool[]]@($true,$false,$false,$true,$true,$true,$true)) $false
CheckRule 'main_or_picker' ([bool[]]@($true,$true,$true,$true,$true,$true,$true)) $false

function SyntheticWindow([string]$name) {
    $window=New-Object Windows.Forms.Form
    $window.Text=$name
    $window.StartPosition=[Windows.Forms.FormStartPosition]::Manual
    $window.Location=New-Object Drawing.Point(-20000,-20000)
    $window.Size=New-Object Drawing.Size(400,300)
    $window.ShowInTaskbar=$false
    return $window
}

$forms=New-Object 'System.Collections.Generic.List[System.Windows.Forms.Form]'
try {
    $main=SyntheticWindow 'BVE Trainsim ISOLATED TEST MAIN'
    $forms.Add($main)
    $main.Show()

    $jpError=([char]0x30A8)+([char]0x30E9)+([char]0x30FC)
    $errorWindow=SyntheticWindow ('Synthetic '+$jpError)
    $forms.Add($errorWindow)
    $errorWindow.Show($main)

    $hud=SyntheticWindow 'Synthetic HUD'
    $forms.Add($hud)
    $hud.Show($main)

    $unownedError=SyntheticWindow 'Synthetic Error notes'
    $forms.Add($unownedError)
    $unownedError.Show()

    $unownedNeutral=SyntheticWindow 'Synthetic neutral tool'
    $forms.Add($unownedNeutral)
    $unownedNeutral.Show()

    $processId=[Diagnostics.Process]::GetCurrentProcess().Id
    $mainArray=[IntPtr[]]@($main.Handle)
    $excluded=[IntPtr[]]@($main.Handle)
    function FindSynthetic {
        $raw=$find.Invoke($null,[object[]]@([int]$processId,$mainArray,$excluded))
        return [IntPtr[]]@($raw)
    }
    $before=FindSynthetic
    if($before -notcontains $errorWindow.Handle){throw 'Owned error not detected'}
    if($before -contains $hud.Handle){throw 'Neutral owned HUD unexpectedly detected while main enabled'}
    if($before -contains $unownedError.Handle){throw 'Unowned error unexpectedly detected while main enabled'}
    if($before -contains $unownedNeutral.Handle){throw 'Unowned neutral tool unexpectedly detected'}
    Write-Output 'HWND_OK_owner_chain_owned_error_only_when_main_enabled'

    $main.Enabled=$false
    $after=FindSynthetic
    if($after -notcontains $errorWindow.Handle){throw 'Owned error not detected while main disabled'}
    if($after -notcontains $unownedError.Handle){throw 'Unowned error fallback not detected'}
    if($after -contains $unownedNeutral.Handle){throw 'Unowned neutral tool unexpectedly detected'}
    Write-Output 'HWND_OK_disabled_main_error_fallback'
    if($after -contains $hud.Handle){
        throw 'Owned neutral HUD still detected while main disabled'
    }
    Write-Output 'HWND_OK_owned_neutral_HUD_excluded_when_main_disabled'
    $lastPopup=$getLastPopup.Invoke($null,[object[]]@($main.Handle))
    $popupName=if($lastPopup -eq $hud.Handle){'HUD'}elseif($lastPopup -eq $errorWindow.Handle){'ERROR'}elseif($lastPopup -eq $main.Handle){'MAIN'}else{'OTHER'}
    Write-Output ('LAST_ACTIVE_POPUP_DISABLED_MAIN='+$popupName)
    $null=$setActive.Invoke($null,[object[]]@($hud.Handle))
    $hudPopupAfterActivation=$getLastPopup.Invoke($null,[object[]]@($main.Handle))
    $forcedName=if($hudPopupAfterActivation -eq $hud.Handle){'HUD'}elseif($hudPopupAfterActivation -eq $main.Handle){'MAIN'}else{'OTHER'}
    Write-Output ('LAST_ACTIVE_POPUP_AFTER_SYNTHETIC_HUD_ACTIVATION='+$forcedName)

    # Real WinForms ShowDialog disables its owner and starts its own modal
    # message pump; inspect it from the Shown event before closing it.
    $main.Enabled=$true
    $modal=SyntheticWindow 'Synthetic generic modal (no error marker)'
    $forms.Add($modal)
    $script:popupDuringModal=[IntPtr]::Zero
    $script:popupAfterModalActivation=[IntPtr]::Zero
    $script:ownerDisabledDuringModal=$false
    $modal.Add_Shown({
        $null=$modal.BeginInvoke([Action]{
            $script:ownerDisabledDuringModal=(![bool]$nativeEnabled.Invoke($null,[object[]]@($main.Handle)))
            $script:popupDuringModal=$getLastPopup.Invoke($null,[object[]]@($main.Handle))
            $null=$setActive.Invoke($null,[object[]]@($modal.Handle))
            $script:popupAfterModalActivation=$getLastPopup.Invoke($null,[object[]]@($main.Handle))
            $modal.Close()
        })
    })
    [void]$modal.ShowDialog($main)
    if(!$script:ownerDisabledDuringModal){throw 'Synthetic modal did not disable its owner'}
    $duringName=if($script:popupDuringModal -eq $modal.Handle){'MODAL'}elseif($script:popupDuringModal -eq $main.Handle){'MAIN'}elseif($script:popupDuringModal -eq $hud.Handle){'HUD'}elseif($script:popupDuringModal -eq $errorWindow.Handle){'ERROR'}else{'OTHER'}
    Write-Output ('LAST_ACTIVE_POPUP_WHILE_REAL_MODAL_OPEN='+$duringName)
    $forcedModalName=if($script:popupAfterModalActivation -eq $modal.Handle){'MODAL'}elseif($script:popupAfterModalActivation -eq $main.Handle){'MAIN'}elseif($script:popupAfterModalActivation -eq $hud.Handle){'HUD'}else{'OTHER'}
    Write-Output ('LAST_ACTIVE_POPUP_AFTER_SYNTHETIC_MODAL_ACTIVATION='+$forcedModalName)
    if($script:popupDuringModal -ne $modal.Handle){
        Write-Output 'OBSERVED_GET_LAST_ACTIVE_POPUP_FAILED_TO_IDENTIFY_MODAL'
    }else{
        Write-Output 'HWND_OK_get_last_active_popup_identified_true_generic_modal'
    }

    $wrongPid=[int]($processId+100000)
    $foreign=$find.Invoke($null,[object[]]@($wrongPid,$mainArray,$excluded))
    if(@($foreign).Count -ne 0){throw 'Wrong-PID synthetic test unexpectedly returned windows'}
    Write-Output 'HWND_OK_wrong_pid_filtered'

    $picker=SyntheticWindow 'Synthetic scenario picker'
    $forms.Add($picker)
    $picker.Show()

    $pickerError=SyntheticWindow 'Synthetic Error from picker'
    $forms.Add($pickerError)
    $pickerError.Show($picker)
    $pickerOnly=[IntPtr[]]@($picker.Handle)
    $emptyMains=[IntPtr[]]@()
    $missed=$find.Invoke($null,[object[]]@([int]$processId,$emptyMains,$pickerOnly))
    $foundViaPicker=$find.Invoke($null,[object[]]@([int]$processId,$pickerOnly,$pickerOnly))
    if(@($missed).Count -eq 0 -and @($foundViaPicker) -contains $pickerError.Handle){
        Write-Output 'HWND_OK_picker_anchor_dialog_detected'
    }else{
        throw 'Picker-only control/experiment did not behave as expected'
    }
}finally{
    for($i=$forms.Count-1;$i -ge 0;$i--){
        try{$forms[$i].Close()}finally{$forms[$i].Dispose()}
    }
}
Write-Output 'B19_ISOLATED_MODAL_AUDIT_FINISHED'
