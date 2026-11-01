$ErrorActionPreference='Stop'
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'dist-b6-ui\BVE6互換ランチャー.exe'))
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$program=$assembly.GetType('Program',$true)
$scenarioChoices=$assembly.GetType('ScenarioChoices',$true)
$candidateDialog=$assembly.GetType('ScenarioDetailDialog',$true)
$history=$assembly.GetType('UpdateHistory',$true)
$launcher=$assembly.GetType('Launcher',$true)
function Find-Buttons($control){
    foreach($child in $control.Controls){
        if($child -is [System.Windows.Forms.Button]){$child.Text}
        Find-Buttons $child
    }
}
$temporary=Join-Path $root ('benchmarks\b6-test-'+[guid]::NewGuid().ToString('N'))
$madeRuns=New-Object System.Collections.Generic.List[string]
New-Item -ItemType Directory -Path $temporary | Out-Null
$program.GetField('Data',$flags).SetValue($null,(Join-Path $temporary 'runs'))
$program.GetField('Home',$flags).SetValue($null,(Join-Path $root 'dist-b6-ui'))
$enc=[Text.Encoding]::GetEncoding(932)
function Invoke-Static($type,$method,$values){
    $matches=@($type.GetMethods($flags)|Where-Object{$_.Name -eq $method -and $_.GetParameters().Length -eq $values.Length})
    if($matches.Count -ne 1){throw ('Method unavailable/ambiguous: '+$method)}
    return $matches[0].Invoke($null,[object[]]$values)
}
function Read-Prepared($prepared){
    $path=[string]$prepared.GetType().GetField('Scenario',$flags).GetValue($prepared)
    if($path.StartsWith($temporary,[StringComparison]::OrdinalIgnoreCase)){
        return [IO.File]::ReadAllText($path,$enc)
    }
    $madeRuns.Add((Split-Path $path))
    return [IO.File]::ReadAllText($path,$enc)
}
try{
    $routeA=Join-Path $temporary 'routeA.map'
    $routeB=Join-Path $temporary 'routeB.map'
    $vehicleA=Join-Path $temporary 'vehicleA.txt'
    $vehicleB=Join-Path $temporary 'vehicleB.txt'
    $scenario=Join-Path $temporary 'testScenario.txt'
    [IO.File]::WriteAllText($routeA,'dummy-route-a',$enc)
    [IO.File]::WriteAllText($routeB,'dummy-route-b',$enc)
    foreach($vehicle in @($vehicleA,$vehicleB)){
        [IO.File]::WriteAllText($vehicle,"BveTs Vehicle 2.00:shift_jis`r`n",$enc)
    }
    [IO.File]::WriteAllText($scenario,("BveTs Scenario 2.00:shift_jis`r`nRoute = routeA.map * 1 | routeB.map * 3`r`nVehicle = vehicleA.txt * 1 | vehicleB.txt * 2`r`n"),$enc)
    $routes=Invoke-Static $candidateDialog 'Available' @([string]$scenario,'routeA.map * 1 | routeB.map * 3',$false)
    $vehicles=Invoke-Static $candidateDialog 'Available' @([string]$scenario,'vehicleA.txt * 1 | vehicleB.txt * 2',$true)
    if($routes.Count -ne 2 -or $vehicles.Count -ne 2){throw 'Candidate enumeration failed'}
    $normalNeither=Invoke-Static $program 'PrepareNormal' @([string]$scenario,$null,$null,$false)
    if($normalNeither.Scenario -ne $scenario){throw 'Normal launch with no overrides must retain the original scenario'}
    $normalRoute=Invoke-Static $program 'PrepareNormal' @([string]$scenario,$null,[string]$routeA,$false)
    $data=Read-Prepared $normalRoute
    if(!$data.Contains('Route = '+$routeA) -or !$data.Contains($vehicleA) -or !$data.Contains($vehicleB)){throw 'Route-only choice discarded random vehicle candidates'}
    $normalVehicle=Invoke-Static $program 'PrepareNormal' @([string]$scenario,[string]$vehicleB,$null,$false)
    $data=Read-Prepared $normalVehicle
    if(!$data.Contains('Vehicle = '+$vehicleB) -or !$data.Contains($routeA) -or !$data.Contains($routeB)){throw 'Vehicle-only choice discarded random route candidates'}
    $normalBoth=Invoke-Static $program 'PrepareNormal' @([string]$scenario,[string]$vehicleB,[string]$routeB,$false)
    $data=Read-Prepared $normalBoth
    if(!$data.Contains('Vehicle = '+$vehicleB) -or !$data.Contains('Route = '+$routeB)){throw 'Both-selected choice failed'}
    $compatNeither=Invoke-Static $program 'Prepare' @([string]$scenario,$null,$null)
    $data=Read-Prepared $compatNeither
    if(!$data.Contains('Route = '+$routeA) -and !$data.Contains('Route = '+$routeB)){throw 'Compatibility mode lost random route selection'}
    $compatBoth=Invoke-Static $program 'Prepare' @([string]$scenario,[string]$vehicleB,[string]$routeA)
    $data=Read-Prepared $compatBoth
    if(!$data.Contains('Route = '+$routeA)){throw 'Compatibility mode lost selected route'}
    $compatVehicleOnly=Invoke-Static $program 'Prepare' @([string]$scenario,[string]$vehicleB,$null)
    $data=Read-Prepared $compatVehicleOnly
    if(!$data.Contains('Route = '+$routeA) -and !$data.Contains('Route = '+$routeB)){throw 'Compatibility mode did not select a random route'}
    $compatRouteOnly=Invoke-Static $program 'Prepare' @([string]$scenario,$null,[string]$routeB)
    $data=Read-Prepared $compatRouteOnly
    if(!$data.Contains('Route = '+$routeB)){throw 'Compatibility mode route-only choice failed'}
    $rejected=$false
    try{$null=Invoke-Static $scenarioChoices 'Select' @([string]$temporary,'routeA.map * 1 | routeB.map * 3',[string]$vehicleA,$false)}
    catch{$rejected=$true}
    if(!$rejected){throw 'A route not listed by the scenario was incorrectly accepted'}

    $createHistory=$history.GetMethod('CreateForm',$flags)
    $form=$createHistory.Invoke($null,[object[]]@())
    try{if($form.Text -notmatch '更新履歴'){throw 'History window not created'}}finally{$form.Dispose()}
    $createDetail=$candidateDialog.GetConstructor([Reflection.BindingFlags]'NonPublic,Instance',$null,[Type[]]@([string]),$null)
    $dialog=$createDetail.Invoke([object[]]@([string]$scenario))
    try{
        if($dialog.Text -notmatch '詳細選択'){throw 'Candidate selection window not created'}
        $listBoxes=New-Object System.Collections.Generic.List[object]
        function Collect-Lists($container,$target){
            foreach($control in $container.Controls){
                if($control -is [System.Windows.Forms.ListBox]){$target.Add($control)}
                Collect-Lists $control $target
            }
        }
        Collect-Lists $dialog $listBoxes
        if($listBoxes.Count -eq 2){
            if($listBoxes[0].Items.Count -ne 3 -or $listBoxes[1].Items.Count -ne 3){
                throw 'B6 route / vehicle candidates missing'
            }
            if($listBoxes[0].SelectedIndex -ne 0 -or $listBoxes[1].SelectedIndex -ne 0){
                throw 'B6 default selections changed'
            }
        }elseif($listBoxes.Count -eq 1){
            $catalogType=$assembly.GetType('CatalogView',$true)
            function Find-Cards($control){
                foreach($child in $control.Controls){
                    if($child -is $catalogType){$child}
                    Find-Cards $child
                }
            }
            $cards=@(Find-Cards $dialog)
            if($listBoxes[0].Items.Count -ne 3 -or $cards.Count -ne 1 -or $cards[0].ItemCount -ne 2){
                throw 'B7 route list or image-backed vehicle gallery missing'
            }
            if($listBoxes[0].SelectedIndex -ne 0 -or $null -ne $cards[0].SelectedItem){
                throw 'B7 must preserve random/default selection initially'
            }
        }else{throw 'Independent route and vehicle selection not found'}
    }finally{$dialog.Dispose()}
    $createMain=$launcher.GetConstructor([Reflection.BindingFlags]'Public,NonPublic,Instance',$null,[Type[]]@([bool]),$null)
    $main=$createMain.Invoke([object[]]@($false))
    try{
        $selectionButton=$launcher.GetField('detailSelectionButton',$flags).GetValue($main)
        if($selectionButton -eq $null -or $selectionButton.Enabled){
            throw 'Detailed selection must be disabled until a scenario is selected'
        }
        $createSettings=$launcher.GetMethod('CreateSettings',$flags)
        $settings=$createSettings.Invoke($main,[object[]]@())
        try{
            $buttons=@(Find-Buttons $settings)
            foreach($needed in @('配布サイトを開く','更新履歴','更新を確認','ログを表示','利用規約','閉じる')){
                if($buttons -notcontains $needed){throw ('Settings button missing: '+$needed)}
            }
        }finally{$settings.Dispose()}
    }finally{$main.Dispose()}
    Write-Output 'B6_INDEPENDENT_CANDIDATES_TESTS_OK'
}finally{
    foreach($run in $madeRuns|Select-Object -Unique){
        if($run -and (Split-Path -Leaf $run) -like 'run-*' -and (Test-Path $run)){
            Remove-Item -LiteralPath $run -Recurse -Force
        }
    }
    if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Recurse -Force}
}
