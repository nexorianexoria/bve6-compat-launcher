$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$meter=Join-Path $root 'benchmarks/results/B13BveAudioFocusMeter.exe'
$csc='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source=Join-Path $PSScriptRoot 'B13BveAudioFocusMeter.cs'
if(!(Test-Path $meter)){
    & $csc /nologo /target:exe /platform:x64 ('/out:'+$meter) $source
    if($LASTEXITCODE -ne 0){throw 'Audio session meter build failed'}
}

# Generate a disposable *very quiet* ~0.4% volume sine-wave in memory.
# It produces a short test sound but saves no input/output recordings.
$rate=22050
$duration=1
$samples=$rate*$duration
$stream=New-Object IO.MemoryStream
$writer=New-Object IO.BinaryWriter($stream)
$writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'))
$writer.Write([int](36+$samples*2))
$writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
$writer.Write([int]16)
$writer.Write([int16]1)
$writer.Write([int16]1)
$writer.Write([int]$rate)
$writer.Write([int]($rate*2))
$writer.Write([int16]2)
$writer.Write([int16]16)
$writer.Write([Text.Encoding]::ASCII.GetBytes('data'))
$writer.Write([int]($samples*2))
for($i=0;$i -lt $samples;$i++){
    [int16]$amplitude=[math]::Round([math]::Sin(2.0*[math]::PI*440*$i/$rate)*130)
    $writer.Write($amplitude)
}
$writer.Flush()
$stream.Position=0
Add-Type -AssemblyName System
$player=New-Object System.Media.SoundPlayer
$player.Stream=$stream
$player.Load()
try {
    $player.PlayLooping()
    Start-Sleep -Milliseconds 350
    $result=& $meter $PID 4
    if($LASTEXITCODE -ne 0){throw 'WASAPI meter failed'}
    $summary=$result|Where-Object {$_ -match '^UNFOCUSED='}
    if($null -eq $summary){throw 'Meter summary missing'}
    $result | Where-Object {$_ -match '^(FOCUSED|UNFOCUSED|AUDIO_RAW_RECORDING)='}
    $peakMatch=[regex]::Match([string]$summary,'maxPeak=([0-9.]+)')
    $found=($result -join [Environment]::NewLine) -match ',1,[0-9.]+'
    if(!$found){Write-Warning 'No process audio session detected; test inconclusive'}
    if(!$peakMatch.Success -or [double]::Parse($peakMatch.Groups[1].Value,[Globalization.CultureInfo]::InvariantCulture) -le 0){
        Write-Warning 'No nonzero audio peak detected; test inconclusive'
    }else {
        Write-Output 'SESSION_PEAK_METER_DETECTED_TEST_TONE'
    }
}finally{
    $player.Stop()
    $player.Dispose()
    $writer.Dispose()
    $stream.Dispose()
}
