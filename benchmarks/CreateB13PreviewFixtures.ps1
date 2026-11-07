# Synthetic preview selection cases. Writes exclusively under ignored results/.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$target=Join-Path $PSScriptRoot 'results\preview-fixtures'
if(Test-Path -LiteralPath $target){throw 'Fixture folder already exists. Do not overwrite a comparison in progress.'}
New-Item -Path $target -ItemType Directory -Force|Out-Null
function Make-Png([string]$name,[int]$width,[int]$height){
    $bitmap=New-Object System.Drawing.Bitmap($width,$height)
    try{
        $bitmap.Save((Join-Path $target $name),[Drawing.Imaging.ImageFormat]::Png)
    }finally{$bitmap.Dispose()}
}
function Make-Case([string]$name,[string]$panelText){
    [IO.File]::WriteAllText((Join-Path $target ('vehicle-'+$name+'.txt')),
        ("BveTs Vehicle 2.00:shift_jis`r`nPanel = panel-"+$name+".txt`r`n"),
        [Text.Encoding]::GetEncoding(932))
    [IO.File]::WriteAllText((Join-Path $target ('panel-'+$name+'.txt')),
        $panelText,[Text.Encoding]::GetEncoding(932))
}
Make-Png 'tie-first.png' 150 100
Make-Png 'tie-second.png' 150 100
Make-Png 'larger.png' 300 280
Make-Png 'night.png' 170 140
Make-Png 'corrupt-large.png' 800 900
$bad=[IO.File]::ReadAllBytes((Join-Path $target 'corrupt-large.png'))
[IO.File]::WriteAllBytes((Join-Path $target 'corrupt-large.png'),
    [byte[]]($bad[0..([Math]::Min(50,$bad.Length-1))]))
Make-Case 'tie' @'
[This]
[PilotLamp]
Subject = true
DaytimeImage = tie-first.png
[PilotLamp]
Subject = true
DaytimeImage = tie-second.png
'@
Make-Case 'largest' @'
[PilotLamp]
Subject = true
DaytimeImage = tie-first.png
[PilotLamp]
Subject = true
DaytimeImage = larger.png
'@
Make-Case 'broken' @'
[PilotLamp]
Subject = true
DaytimeImage = corrupt-large.png
[PilotLamp]
Subject = true
DaytimeImage = larger.png
'@
Make-Case 'night' @'
[This]
NighttimeImage = night.png
[PilotLamp]
Subject = true
DaytimeImage = larger.png
'@
Write-Output ('PREVIEW_FIXTURES_CREATED='+$target)
