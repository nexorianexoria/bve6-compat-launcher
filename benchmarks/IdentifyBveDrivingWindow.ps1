param(
 [ValidateRange(5,120)][int]$Seconds=15,
 [string]$OutDir=''
)
$ErrorActionPreference='Stop'
$scriptDir=$PSScriptRoot
if([string]::IsNullOrWhiteSpace($OutDir)){
 $OutDir=Join-Path $scriptDir ('results\driving-isolation-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$fps=Join-Path $scriptDir 'CaptureBveFps.ps1'
$analysis=Join-Path $scriptDir 'CompareBveWindows.ps1'
$principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if(!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
 Write-Warning 'PresentMon requires an administrator PowerShell for ETW capture.'
 Write-Output 'Run again from your own elevated PowerShell. This script does not elevate itself.'
 exit 5
}
$processes=@(Get-Process -Name BveTs -ErrorAction SilentlyContinue)
if($processes.Count -ne 1){
 Write-Warning 'Exactly one BVE process is required. Open BVE, load the scenario, and close extra BVE instances.'
 exit 2
}
Write-Output 'This test does NOT minimize, activate, close, or change any windows by itself.'
Write-Output 'If minimizing BVE also hides PanelViewEX, CANCEL this test because it cannot isolate the windows.'
Write-Output 'For safety, conduct it while the train is stopped in a test scene.'
Write-Output ('Each phase records '+$Seconds+' seconds. Keep the same scene and viewpoint.')
$testBvePid=$processes[0].Id
$response=Read-Host 'Phase 1: BOTH BVE driving window and PanelViewEX visible, train stopped. Press Enter (q cancels)'
if($response -eq 'q'){exit 0}
New-Item -ItemType Directory -Path $OutDir -Force|Out-Null
$normal=Join-Path $OutDir '01-both-visible.csv'
$isolated=Join-Path $OutDir '02-driving-minimized.csv'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $fps -Seconds $Seconds -OutputPath $normal
if($LASTEXITCODE -ne 0){throw 'First capture failed.'}
 $current=Get-Process -Id $testBvePid -ErrorAction SilentlyContinue
 if($null -eq $current -or $current.ProcessName -ne 'BveTs'){throw 'BVE process exited or changed between capture phases; start again.'}
$response=Read-Host 'Phase 2: minimize ONLY the BVE DRIVING window manually. PanelViewEX MUST remain visible; otherwise type q. Press Enter (q cancels)'
if($response -eq 'q'){exit 0}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $fps -Seconds $Seconds -OutputPath $isolated
if($LASTEXITCODE -ne 0){throw 'Second capture failed.'}
 $current=Get-Process -Id $testBvePid -ErrorAction SilentlyContinue
 if($null -eq $current -or $current.ProcessName -ne 'BveTs'){throw 'BVE process exited or changed between capture phases; start again.'}
$response=Read-Host 'Phase 3: RESTORE BVE driving window manually. Same stationary scene. Press Enter (q cancels)'
if($response -eq 'q'){exit 0}
$restored=Join-Path $OutDir '03-driving-restored.csv'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $fps -Seconds $Seconds -OutputPath $restored
if($LASTEXITCODE -ne 0){throw 'Third capture failed.'}
 $current=Get-Process -Id $testBvePid -ErrorAction SilentlyContinue
 if($null -eq $current -or $current.ProcessName -ne 'BveTs'){throw 'BVE process exited or changed between capture phases; start again.'}
$comparison=Join-Path $OutDir 'window-comparison.csv'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $analysis -BothVisibleCsv $normal -PanelMinimizedCsv $isolated -PanelRestoredCsv $restored -TargetWindow "BVE driving window" -OutputPath $comparison
if($LASTEXITCODE -ne 0){throw 'Analysis failed.'}
Write-Output ('Three recordings and comparison saved under '+$OutDir)