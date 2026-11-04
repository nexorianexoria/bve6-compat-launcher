$ErrorActionPreference='Stop'
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
$exe=Join-Path $root 'dist-b12-mita-candidate/BVE6互換ランチャー.exe'
$assembly=[Reflection.Assembly]::LoadFrom($exe)
$program=$assembly.GetType('Program',$true)
$flags=[Reflection.BindingFlags]'NonPublic,Public,Static,Instance'
$originalDir=Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'BveTs/Scenarios'
$vehicle=Join-Path $originalDir 'TKK3020/3121.txt'
$sound=Join-Path $originalDir 'TKK3020/Sound.txt'
$scenarios=@(
    (Join-Path $originalDir '地下鉄三田線_北行2023_1037G.txt'),
    (Join-Path $originalDir '地下鉄三田線_北行2023_1343T.txt')
)
$originals=@($sound,$vehicle)+$scenarios
$originalHashes=@{}
foreach($p in $originals){$originalHashes[$p]=(Get-FileHash $p).Hash}
$scratch=Join-Path $root ('benchmarks/b12-mita-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -Path $scratch -ItemType Directory|Out-Null
$program.GetField('Data',$flags).SetValue($null,$scratch)
$prepareNormal=$program.GetMethods($flags)|Where-Object {
    $_.Name -eq 'PrepareNormal' -and $_.GetParameters().Length -eq 4
}|Select-Object -First 1
$prepareCompat=$program.GetMethods($flags)|Where-Object {
    $_.Name -eq 'Prepare' -and $_.GetParameters().Length -eq 3
}|Select-Object -First 1
$parse=$program.GetMethod('Read',$flags)
$value=$program.GetMethod('Value',$flags)
$expected=@{200=204;201=205;202=204;203=205}
$encoding=[Text.Encoding]::GetEncoding(932)
try{
    foreach($scenario in $scenarios){
        foreach($kind in @('Normal','Compat')){
            $args=if($kind -eq 'Normal'){
                [object[]]@([string]$scenario,[string]$vehicle,$null,$false)
            }else{[object[]]@([string]$scenario,[string]$vehicle,$null)}
            $method=if($kind -eq 'Normal'){$prepareNormal}else{$prepareCompat}
            $prepared=$method.Invoke($null,$args)
            $scenePath=[string]$prepared.GetType().GetField('Scenario',$flags).GetValue($prepared)
            $scene=$parse.Invoke($null,[object[]]@($scenePath))
            $shVehicle=[string]$value.Invoke($null,[object[]]@($scene,'Vehicle'))
            if(!$shVehicle -or $shVehicle -eq $vehicle -or !(Test-Path $shVehicle)){
                throw ('Missing staged vehicle for '+$scenario+' '+$kind)
            }
            $shLines=$parse.Invoke($null,[object[]]@($shVehicle))
            $shSound=[string]$value.Invoke($null,[object[]]@($shLines,'Sound'))
            if(!(Test-Path $shSound)){throw ('Missing staged sound for '+$kind)}
            $lines=[IO.File]::ReadAllLines($shSound,$encoding)
            $soundEntries=@{}
            $section=''
            foreach($line in $lines){
                if($line.Trim() -match '^\[(.+)\]$'){$section=$Matches[1];continue}
                if($section -ne 'Ats'){continue}
                if($line -match '^\s*(20[0-5])\s*=\s*(.+)$'){
                    $soundEntries[[int]$Matches[1]]=$Matches[2].Trim()
                }
            }
            foreach($entry in $expected.GetEnumerator()){
                if($soundEntries[$entry.Key] -ne $soundEntries[$entry.Value]){
                    throw ('Unexpected audio channel '+$entry.Key+' in '+$kind)
                }
            }
            $temporaryPluginMap=Join-Path (Split-Path $shVehicle) 'VehiclePluginUsing.xml'
            if(!(Test-Path $temporaryPluginMap)){
                throw ('BveEX configuration missing '+$kind)
            }
            [xml]$originalPluginConfig=Get-Content (Join-Path $originalDir 'TKK3020/VehiclePluginUsing.xml') -Raw -Encoding UTF8
            [xml]$temporaryPluginConfig=Get-Content $temporaryPluginMap -Raw -Encoding UTF8
            $originalPluginNames=@($originalPluginConfig.BveExPluginUsing.Assembly |
                ForEach-Object {[IO.Path]::GetFileName(($_.Path -replace '\\','/'))})
            $temporaryPluginNames=@($temporaryPluginConfig.BveExPluginUsing.Assembly |
                ForEach-Object {[IO.Path]::GetFileName(($_.Path -replace '\\','/'))})
            $crashPlugin='BveEx.Plugins.ExtractionHelper.dll'
            if($originalPluginNames -notcontains $crashPlugin -or
                $temporaryPluginNames -contains $crashPlugin){
                throw ('Crash-prone BveEX overlay is not properly isolated '+$kind)
            }
            $expectedPlugins=@($originalPluginNames | Where-Object {$_ -ne $crashPlugin})
            if(@(Compare-Object $expectedPlugins $temporaryPluginNames).Count -ne 0){
                throw ('Other BveEX vehicle plug-ins were lost '+$kind)
            }
            if(!(Test-Path (Join-Path (Split-Path $shVehicle) 'BveEXクラッシュ回避.txt'))){
                throw ('Temporary crash workaround notice missing '+$kind)
            }
            $origSoundLines=[IO.File]::ReadAllText($sound,[Text.Encoding]::UTF8)
            if(!$origSoundLines.Contains('200 = Sound\Train Railway Next\NewB_L.wav')){
                throw 'Original stock sound map unexpectedly changed'
            }
            $routeSpec=[string]$value.Invoke($null,[object[]]@($scene,'Route'))
            if($kind -eq 'Normal' -and $routeSpec -notmatch 'mita_v2023'){
                throw 'Normal mode route changed unexpectedly'
            }
            Write-Output ('PASS_MITA_'+$kind+'_'+(Split-Path $scenario -Leaf))
            Write-Output ('  STAGED_SOUND='+$shSound)
            Write-Output ('  ATS200='+$soundEntries[200])
            Write-Output ('  ATS202='+$soundEntries[202])
        }
    }
    foreach($path in $originals){
        if($originalHashes[$path] -ne (Get-FileHash $path).Hash){
            throw ('Original source file modified: '+$path)
        }
    }
    Write-Output 'ALL_FOUR_MITA_PREPARATIONS_AND_ORIGINAL_FILE_PROTECTION_OK'
}finally{
    if(Test-Path $scratch){Remove-Item $scratch -Recurse -Force}
}
