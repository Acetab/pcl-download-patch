$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
& (Join-Path $root 'build.ps1')
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$dist = Join-Path $root 'dist'
$exe = Join-Path $dist 'PCL-Download-Patch.exe'
$entries = @(
    @{File=$exe;Name='PCL-Download-Patch.exe'},
    @{File=(Join-Path $root 'docs\help.txt');Name='使用说明.txt'},
    @{File=(Join-Path $root 'VERIFICATION.md');Name='验证记录.md'},
    @{File=(Join-Path $root 'NOTICE.md');Name='许可与来源.md'},
    @{File=(Join-Path $root 'PCL-LICENCE.txt');Name='PCL-LICENCE.txt'},
    @{File=(Join-Path $root 'LICENSE');Name='LICENSE'},
    @{File=(Join-Path $root 'vendor\Mono.Cecil-LICENSE.txt');Name='Mono.Cecil-LICENSE.txt'}
)
function Write-Zip([string]$Path,$Entries) {
    $stream = [IO.File]::Open($Path,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try {
        $zip = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
        try { foreach($entry in $Entries) { [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$entry.File,$entry.Name,[IO.Compression.CompressionLevel]::Optimal) } }
        finally { $zip.Dispose() }
    } finally { $stream.Dispose() }
}
$usage = Join-Path $dist 'pcl-download-patch-v2.1.1-windows.zip'
Write-Zip $usage $entries
$source = @()
foreach($folder in @('src','tests','assets','docs','scripts','vendor','.github\workflows')) {
    foreach($file in Get-ChildItem -LiteralPath (Join-Path $root $folder) -File) { $source += @{File=$file.FullName;Name=($folder.Replace('\','/') + '/' + $file.Name)} }
}
foreach($relative in @('build.ps1','test.ps1','package.ps1','README.md','VERIFICATION.md','LICENSE','NOTICE.md','PCL-LICENCE.txt','CHANGELOG.md','RELEASE_NOTES.md','.gitignore','.editorconfig')) {
    $source += @{File=(Join-Path $root $relative);Name=$relative}
}
$sourceZip = Join-Path $dist 'pcl-download-patch-v2.1.1-source.zip'
Write-Zip $sourceZip $source
@($exe,$usage,$sourceZip) | ForEach-Object { ((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_)) } | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding UTF8
Write-Output 'PACKAGE_OK'
