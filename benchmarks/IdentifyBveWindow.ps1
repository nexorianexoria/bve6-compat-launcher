param(
 [ValidateRange(5,120)][int]$Seconds=15,
 [string]$OutDir=''
)
$ErrorActionPreference='Stop'
$scriptDir=$PSScriptRoot
if([string]::IsNullOrWhiteSpace($OutDir)){
 $OutDir=Join-Path $scriptDir ('results\window-isolation-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
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
if($processes.Count -eq 0){
 Write-Warning 'BVE is not running. Open BVE and load a test scenario before starting.'
 exit 2
}
Write-Output 'This test does NOT minimize, activate, close, or change any windows by itself.'
Write-Output 'For safety, conduct it while the train is stopped in a test scene.'
Write-Output ('Each phase records '+$Seconds+' seconds. Keep the same scene and viewpoint.')
$response=Read-Host 'Phase 1: show BOTH BVE and PanelViewEX. Press Enter when ready (type q to cancel)'
if($response -eq 'q'){exit 0}
New-Item -ItemType Directory -Path $OutDir -Force|Out-Null
$normal=Join-Path $OutDir '01-both-visible.csv'
$isolated=Join-Path $OutDir '02-panel-minimized.csv'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $fps -Seconds $Seconds -OutputPath $normal
if($LASTEXITCODE -ne 0){throw 'First capture failed.'}
$response=Read-Host 'Phase 2: manually minimize ONLY PanelViewEX. Keep BVE visible and the same scene. Press Enter (q cancels)'
if($response -eq 'q'){exit 0}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $fps -Seconds $Seconds -OutputPath $isolated
if($LASTEXITCODE -ne 0){throw 'Second capture failed.'}
$response=Read-Host 'Phase 3: RESTORE PanelViewEX manually, with the same scene. Press Enter (q cancels)'
if($response -eq 'q'){exit 0}
$restored=Join-Path $OutDir '03-panel-restored.csv'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $fps -Seconds $Seconds -OutputPath $restored
if($LASTEXITCODE -ne 0){throw 'Third capture failed.'}
$comparison=Join-Path $OutDir 'window-comparison.csv'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $analysis -BothVisibleCsv $normal -PanelMinimizedCsv $isolated -PanelRestoredCsv $restored -OutputPath $comparison
if($LASTEXITCODE -ne 0){throw 'Analysis failed.'}
Write-Output ('Three recordings and comparison saved under '+$OutDir)