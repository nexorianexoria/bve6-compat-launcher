$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'dist-b11-options/BVE6互換ランチャー.exe'))
$flags=[Reflection.BindingFlags]'NonPublic,Public,Instance,Static'
$prefs=$assembly.GetType('BvePreferencesService',$true)
$launchDisplay=$assembly.GetType('BveLaunchDisplay',$true)
$devices=$assembly.GetType('NumerousControllerSettings',$true)
$dialogType=$assembly.GetType('BveOptionsDialog',$true)
$documents=Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'BveTs/Settings'
$origPrefs=Join-Path $documents 'BveTs6.Preferences.xml'
$origControllers=Join-Path $documents 'Kusaanko.NumerousControllerInterface.xml'
$originalHashes=@{
    preferences=(Get-FileHash $origPrefs).Hash
    controllers=(Get-FileHash $origControllers).Hash
}
$scratch=Join-Path $root ('benchmarks/b11-settings-test-'+[Guid]::NewGuid().ToString('N'))
New-Item $scratch -ItemType Directory|Out-Null
try{
    $prefsCopy=Join-Path $scratch 'BveTs6.Preferences.xml'
    $controllerCopy=Join-Path $scratch 'Kusaanko.NumerousControllerInterface.xml'
    $displayCopy=Join-Path $scratch 'launch-displays.xml'
    Copy-Item $origPrefs $prefsCopy
    Copy-Item $origControllers $controllerCopy
    [xml]$before=Get-Content $prefsCopy -Raw -Encoding UTF8
    $assignment=$before.Preferences.KeyAssignment.OuterXml
    $get=$prefs.GetMethod('ReadFile',$flags)
    $savePrefs=$prefs.GetMethod('SaveFile',$flags)
    $value=$get.Invoke($null,[object[]]@([string]$prefsCopy))
    $snapshotType=$value.GetType()
    $snapshotType.GetField('Width',$flags).SetValue($value,1280)
    $snapshotType.GetField('Height',$flags).SetValue($value,720)
    $snapshotType.GetField('DrawDistance',$flags).SetValue($value,400)
    $snapshotType.GetField('BackDrawDistance',$flags).SetValue($value,30)
    $snapshotType.GetField('Anisotropy',$flags).SetValue($value,2)
    $snapshotType.GetField('Windowed',$flags).SetValue($value,$true)
    $pl=New-Object 'System.Collections.Generic.List[string]'
    $pl.Add('bveex.caller.inputdevice')
    $pl.Add('zkns_001interface')
    $snapshotType.GetField('InputPlugins',$flags).SetValue($value,$pl)
    $savePrefs.Invoke($null,[object[]]@([string]$prefsCopy,$value))|Out-Null
    $roundtrip=$get.Invoke($null,[object[]]@([string]$prefsCopy))
    if($snapshotType.GetField('Width',$flags).GetValue($roundtrip) -ne 1280 -or
       $snapshotType.GetField('DrawDistance',$flags).GetValue($roundtrip) -ne 400) {
        throw 'Window and graphics settings failed to persist'
    }
    [xml]$after=Get-Content $prefsCopy -Raw -Encoding UTF8
    if($after.Preferences.KeyAssignment.OuterXml -ne $assignment) {
        throw 'Existing BVE key mappings were modified'
    }
    $backup=@(Get-ChildItem $scratch -File -Filter 'BveTs6.Preferences.xml.launcher-*.bak')
    if($backup.Count -ne 1){throw 'Preferences safety backup missing'}
    $readDevices=$devices.GetMethod('Read',$flags)
    $saveDevices=$devices.GetMethod('Save',$flags)
    $items=$readDevices.Invoke($null,[object[]]@([string]$controllerCopy))
    if($items.Count -ne 2){throw 'The existing controller types were not discovered'}
    $controllerType=$assembly.GetType('ControllerSwitch',$true)
    $nameField=$controllerType.GetField('Name',$flags)
    $enabledField=$controllerType.GetField('Enabled',$flags)
    $first=$items|Where-Object {$nameField.GetValue($_) -eq 'MultiTrainController P5B5'}|Select-Object -First 1
    if(!$first -or !$enabledField.GetValue($first)){throw 'The currently enabled controller was not found'}
    $beforeProfileMap=(Get-Content $controllerCopy -Raw -Encoding UTF8) -replace '(?s)^.*(<ProfileMap.+?</ProfileMap>).*$', '$1'
    $enabledField.SetValue($first,$false)
    $saveDevices.Invoke($null,[object[]]@([string]$controllerCopy,$items))|Out-Null
    $roundtripDevices=$readDevices.Invoke($null,[object[]]@([string]$controllerCopy))
    $updated=$roundtripDevices|Where-Object {$nameField.GetValue($_) -eq 'MultiTrainController P5B5'}|Select-Object -First 1
    if(!$updated -or $enabledField.GetValue($updated)){throw 'Controller toggle did not persist'}
    $afterProfileMap=(Get-Content $controllerCopy -Raw -Encoding UTF8) -replace '(?s)^.*(<ProfileMap.+?</ProfileMap>).*$', '$1'
    if($beforeProfileMap -ne $afterProfileMap){throw 'Unrelated device profiles were modified'}
    $readProfiles=$devices.GetMethod('ReadProfiles',$flags)
    $saveAll=$devices.GetMethod('SaveAll',$flags)
    $profiles=$readProfiles.Invoke($null,[object[]]@([string]$controllerCopy))
    if($profiles.Count -lt 2){throw 'Registered controller profiles not discovered'}
    $profileType=$assembly.GetType('ControllerProfile',$true)
    $profileName=$profileType.GetField('Name',$flags)
    $profileValue=$profileType.GetField('Profile',$flags)
    $profileTarget=$profiles | Where-Object {$profileName.GetValue($_) -eq 'MultiTrainController P5B5'} | Select-Object -First 1
    if(!$profileTarget){throw 'Missing P5B5 profile mapping'}
    $different=$profiles | Where-Object {
        $profileValue.GetValue($_) -ne $profileValue.GetValue($profileTarget)
    } | Select-Object -First 1
    $expected=$profileValue.GetValue($different)
    $profileValue.SetValue($profileTarget,$expected)
    $saveAll.Invoke($null,[object[]]@([string]$controllerCopy,$roundtripDevices,$profiles)) | Out-Null
    $afterProfiles=$readProfiles.Invoke($null,[object[]]@([string]$controllerCopy))
    $actual=$afterProfiles|Where-Object {$profileName.GetValue($_) -eq 'MultiTrainController P5B5'}|Select-Object -First 1
    if($profileValue.GetValue($actual) -ne $expected){throw 'Controller profile selection not persisted'}
    $profileDialogType=$assembly.GetType('NumerousProfilesDialog',$true)
    $profileConstructor=$profileDialogType.GetConstructors($flags)|Select-Object -First 1
    $profileArgs=New-Object 'object[]' 1
    $profileArgs[0]=$afterProfiles
    $profileForm=$profileConstructor.Invoke($profileArgs)
    try {
        $dropdowns=$profileDialogType.GetField('choices',$flags).GetValue($profileForm)
        if($dropdowns.Count -ne $afterProfiles.Count){throw 'Profile chooser does not list all controller profiles'}
    }finally{$profileForm.Dispose()}
    if(@(Get-ChildItem $scratch -Filter '*NumerousControllerInterface.xml.launcher-*.bak').Count -lt 2){
        throw 'Controller safety backup missing'
    }
    $saveDisplay=$launchDisplay.GetMethod('SaveFile',$flags)
    $readDisplay=$launchDisplay.GetMethod('ReadFile',$flags)
    $saveDisplay.Invoke($null,[object[]]@([string]$displayCopy,6,'\\.\DISPLAY2'))|Out-Null
    if($readDisplay.Invoke($null,[object[]]@([string]$displayCopy,6)) -ne '\\.\DISPLAY2'){
        throw 'Monitor choice did not persist'
    }
    $saveDisplay.Invoke($null,[object[]]@([string]$displayCopy,5,'\\.\DISPLAY3'))|Out-Null
    if($readDisplay.Invoke($null,[object[]]@([string]$displayCopy,6)) -ne '\\.\DISPLAY2'){
        throw 'BVE5 option overwrote the BVE6 monitor choice'
    }
    # No process launch needed: exercise the same Win32 window positioning
    # helper against a hidden, synthetic form with a negative-coordinate
    # secondary display work area.
    $move=$launchDisplay.GetMethod('MoveWindow',$flags)
    $dummy=New-Object Windows.Forms.Form
    try{
        $dummy.StartPosition=[Windows.Forms.FormStartPosition]::Manual
        $dummy.Location=[Drawing.Point]::new(10,10)
        $handle=$dummy.Handle
        $area=[Drawing.Rectangle]::new(-3840,-1080,2194,1234)
        $moved=$move.Invoke($null,[object[]]@($handle,$area,1280,720))
        if(!$moved){throw 'Win32 moving a test window failed'}
        if($dummy.Left -ne -3383 -or $dummy.Top -ne -823 -or
           $dummy.Width -ne 1280 -or $dummy.Height -ne 720){
            throw ('Window placement incorrect: '+$dummy.Bounds.ToString())
        }
    }finally{$dummy.Dispose()}
    $ctor=$dialogType.GetConstructor($flags,$null,[Type[]]@([string],[string]),$null)
    $form=$ctor.Invoke([object[]]@([string]'C:\Program Files (x86)\mackoy\BveTs5\BveTs.exe',
        [string]'C:\Program Files\mackoy\BveTs6\BveTs.exe'))
    try{
        function Find-Tab([Windows.Forms.Control]$c){
            if($c -is [Windows.Forms.TabControl]){$c}
            foreach($child in $c.Controls){Find-Tab $child}
        }
        $tabs=@(Find-Tab $form)
        if($tabs.Count -ne 1 -or $tabs[0].TabPages.Count -ne 3){throw 'Settings tabs missing'}
        $button=$dialogType.GetField('save',$flags).GetValue($form)
        if($prefs.GetMethod('IsGameRunning',$flags).Invoke($null,@()) -and $button.Enabled){
            throw 'Settings save must be disabled while BVE is running'
        }
    }finally{$form.Dispose()}
    if((Get-FileHash $origPrefs).Hash -ne $originalHashes.preferences -or
       (Get-FileHash $origControllers).Hash -ne $originalHashes.controllers){
        throw 'Live BVE settings were modified by the tests'
    }
    Write-Output 'B11_PREFERENCES_BACKUP_PRESERVATION_AND_ROUNDTRIP_OK'
    Write-Output 'B11_INPUT_PLUGINS_CONTROLLER_MONITORS_AND_SETTINGS_UI_OK'
    Write-Output 'B11_CONTROLLER_PROFILE_EDIT_AND_BACKUP_OK'
    Write-Output 'B11_LIVE_BVE_SETTINGS_UNCHANGED'
}finally{
    if(Test-Path $scratch){Remove-Item $scratch -Recurse -Force}
}
