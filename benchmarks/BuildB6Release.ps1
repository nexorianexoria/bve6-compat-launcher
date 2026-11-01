$ErrorActionPreference='Stop'
$root=if($PSScriptRoot){Split-Path -Parent $PSScriptRoot}else{(Get-Location).Path}
Set-Location $root
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$version='0.2.9.17'
$display='0.29b6'
$out=Join-Path $root 'downloads\0.29b6'
if(Test-Path -LiteralPath $out){throw 'The b6 output already exists; do not silently overwrite a published release'}
New-Item -ItemType Directory -Path $out | Out-Null
$build=Join-Path $root 'dist-b6-ui'
$names=@('BVE6互換ランチャー.exe','導入・更新.exe','LauncherStarter.exe','Uninstall.exe','launcher.ico','LICENSE.txt','README.txt','THIRD_PARTY_NOTICES.txt','bridge/Bve5VehicleCompatibility.exe','bridge/Bve5VehicleCompatibility_.dll','bridge/LICENSE.txt')
$staging=Join-Path $out 'package-staging'
$portable=Join-Path $out 'BVE6Launcher-0.29b6-Portable'
New-Item -ItemType Directory -Path $staging,$portable | Out-Null
try {
    foreach($name in $names){
        $source=if($name -in @('LICENSE.txt','README.txt','THIRD_PARTY_NOTICES.txt')){Join-Path $root $name}else{Join-Path $build $name}
        if(!(Test-Path -LiteralPath $source -PathType Leaf)){throw ('Required release file missing: '+$name)}
        $destination=Join-Path $staging $name
        New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
        if($name -in @('BVE6互換ランチャー.exe','導入・更新.exe','LauncherStarter.exe','Uninstall.exe')){
            $info=[Diagnostics.FileVersionInfo]::GetVersionInfo($source)
            if($info.FileVersion -ne $version -or $info.ProductVersion -ne $display){throw ('Binary version mismatch: '+$name)}
        }
        if($name -notin @('導入・更新.exe','LauncherStarter.exe','Uninstall.exe')){
            $port=Join-Path $portable $name
            New-Item -ItemType Directory -Path (Split-Path $port) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $port
        }
    }
    $hashes=@{}
    foreach($name in $names){$hashes[$name]=(Get-FileHash -LiteralPath (Join-Path $staging $name) -Algorithm SHA256).Hash}
    $index=[ordered]@{version=$version;displayVersion=$display;files=$hashes}
    [IO.File]::WriteAllText((Join-Path $staging 'package-index.json'),($index|ConvertTo-Json -Depth 7),[Text.UTF8Encoding]::new($false))
    $package=Join-Path $out 'BVE6Launcher-package.zip'
    $archive=[IO.Compression.ZipFile]::Open($package,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach($name in $names + @('package-index.json')){
            $source=Join-Path $staging $name
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$source,$name.Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal)
        }
    }finally{$archive.Dispose()}
    $portableZip=Join-Path $out 'BVE6Launcher-0.29b6-Portable.zip'
    Compress-Archive -Path $portable -DestinationPath $portableZip -CompressionLevel Optimal
    Copy-Item -LiteralPath (Join-Path $build 'BVE6Launcher-Setup.exe') -Destination $out
    $sha=(Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
    $manifest=[ordered]@{
        version=$version;displayVersion=$display
        url='https://raw.githubusercontent.com/nexorianexoria/bve6-compat-launcher/main/downloads/0.29b6/BVE6Launcher-package.zip'
        sha256=$sha
        notes='0.29b6：路線・車両の詳細選択と、設定画面の配布サイト・更新履歴・手動更新確認を追加しました。'
    }
    $json=$manifest|ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText((Join-Path $out 'package.json'),$json,[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $root 'update.json'),$json,[Text.UTF8Encoding]::new($false))
    # HEAD is the tested source commit; publishing assets is a follow-up commit.
    $sourceZip=Join-Path $out 'BVE6Launcher-0.29b6-Source.zip'
    & git archive --format=zip ('--output='+$sourceZip) HEAD
    if($LASTEXITCODE -ne 0){throw 'Could not archive the committed source'}
    $files=@('BVE6Launcher-0.29b6-Portable.zip','BVE6Launcher-0.29b6-Source.zip','BVE6Launcher-Setup.exe','BVE6Launcher-package.zip')
    $sums=foreach($file in $files){$hash=(Get-FileHash -LiteralPath (Join-Path $out $file) -Algorithm SHA256).Hash.ToLowerInvariant();$hash+'  downloads/0.29b6/'+$file}
    [IO.File]::WriteAllLines((Join-Path $out 'SHA256SUMS.txt'),[string[]]$sums,[Text.UTF8Encoding]::new($false))
    Write-Output 'B6_PACKAGE_CREATED'
    Write-Output ('PACKAGED_VERSION='+$display)
    Write-Output ('PACKAGE_SHA256='+$sha)
}finally{
    Remove-Item -LiteralPath $staging,$portable -Recurse -Force -ErrorAction SilentlyContinue
}
