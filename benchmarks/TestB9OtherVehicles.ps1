$ErrorActionPreference='Stop'
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'dist-b9-release/BVE6互換ランチャー.exe'))
$flags=[Reflection.BindingFlags]'NonPublic,Public,Instance,Static'
$detail=$assembly.GetType('ScenarioDetailDialog',$true)
$picker=$assembly.GetType('AdditionalVehicleDialog',$true)
$itemType=$assembly.GetType('CatalogItem',$true)
$entryType=$assembly.GetType('Program+Entry',$true)
$scratch=Join-Path $root ('benchmarks/b9-vehicle-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
try{
    $enc=[Text.Encoding]::GetEncoding(932)
    $scenario=Join-Path $scratch 'scenario.txt'
    $route=Join-Path $scratch 'route.map'
    $scenarioVehicle=Join-Path $scratch 'scenario-vehicle.txt'
    $otherVehicle=Join-Path $scratch 'external-vehicle.txt'
    [IO.File]::WriteAllText($route,'map',$enc)
    [IO.File]::WriteAllText($scenarioVehicle,"BveTs Vehicle 2.00:shift_jis`r`n",$enc)
    [IO.File]::WriteAllText($otherVehicle,"BveTs Vehicle 2.00:shift_jis`r`n",$enc)
    [IO.File]::WriteAllText($scenario,"BveTs Scenario 2.00:shift_jis`r`nRoute = route.map`r`nVehicle = scenario-vehicle.txt`r`n",$enc)
    $catalogType=$assembly.GetType('CatalogData',$true)
    $build=$catalogType.GetMethods($flags) | Where-Object { $_.Name -eq 'Build' -and $_.GetParameters().Length -eq 3 } | Select-Object -First 1
    $entries=[Array]::CreateInstance($entryType,2)
    foreach($pair in @(@($scenarioVehicle,0),@($otherVehicle,1))){
        $entry=[Activator]::CreateInstance($entryType)
        $entryType.GetField('Path',$flags).SetValue($entry,$pair[0])
        $entryType.GetField('Title',$flags).SetValue($entry,[IO.Path]::GetFileNameWithoutExtension($pair[0]))
        $entries.SetValue($entry,[int]$pair[1])
    }
    $cards=$build.Invoke($null,[object[]]@($entries,[string]$scratch,$true))
    # The strong type is IEnumerable<CatalogItem>, not IEnumerable<object>.
    $listType=([Collections.Generic.IEnumerable[string]]).GetGenericTypeDefinition().MakeGenericType($itemType)
    $create=$detail.GetConstructor([Reflection.BindingFlags]'NonPublic,Public,Instance',$null,[Type[]]@([string],$listType),$null)
    if($null -eq $create){throw 'Detail dialog does not accept the regular vehicle catalog'}
    $form=$create.Invoke([object[]]@([string]$scenario,$cards))
    try{
        $other=$detail.GetField('otherVehicle',$flags).GetValue($form)
        if($other.Text -ne '他の車両から選ぶ'){throw 'Other vehicle button not visible'}
        $pick=$picker.GetConstructor([Reflection.BindingFlags]'NonPublic,Public,Instance',$null,[Type[]]@($listType),$null)
        $argsPicker=New-Object 'object[]' 1
        $argsPicker[0]=$cards
        $window=$pick.Invoke($argsPicker)
        try{
            $gallery=$picker.GetField('gallery',$flags).GetValue($window)
            if($gallery.ItemCount -ne 2 -or !$gallery.SelectPath($otherVehicle)){throw 'Regular vehicle catalog is not selectable'}
            $accept=$picker.GetMethod('AcceptVehicle',$flags)
            $accept.Invoke($window,[object[]]@())
            $chosen=$picker.GetProperty('VehiclePath',$flags).GetValue($window)
            if($chosen -ne $otherVehicle){throw ('External vehicle did not resolve: '+$chosen)}
        }finally{$window.Dispose()}
    }finally{$form.Dispose()}
    $program=$assembly.GetType('Program',$true)
    $program.GetField('Data',$flags).SetValue($null,(Join-Path $scratch 'runs'))
    $normal=$program.GetMethods($flags) | Where-Object { $_.Name -eq 'PrepareNormal' -and $_.GetParameters().Length -eq 4 } | Select-Object -First 1
    $prepared=$normal.Invoke($null,[object[]]@([string]$scenario,[string]$otherVehicle,$null,$false))
    $file=[string]$prepared.GetType().GetField('Scenario',$flags).GetValue($prepared)
    if(-not ([IO.File]::ReadAllText($file,$enc).Contains($otherVehicle))){throw 'External vehicle not present in generated launch scenario'}
    Write-Output 'B9_OTHER_VEHICLE_PICKER_AND_LAUNCH_TEST_OK'
}finally{
    if(Test-Path $scratch){Remove-Item -LiteralPath $scratch -Recurse -Force}
}
