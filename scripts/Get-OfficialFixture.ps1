param([Parameter(Mandatory=$true)][string]$WorkDirectory)
$ErrorActionPreference = 'Stop'
[void][IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($WorkDirectory))
$exe = Join-Path $WorkDirectory 'PCL.original.exe'
$expected = '6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386'
if (!(Test-Path -LiteralPath $exe)) {
    $zip = Join-Path $WorkDirectory 'official.zip'
    Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/Meloong-Git/PCL/6b9a50df911c9d68b69b24c979efba477fb14b67/%E6%9C%80%E6%96%B0%E6%AD%A3%E5%BC%8F%E7%89%88.zip' -OutFile $zip
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $entry = $archive.GetEntry('Plain Craft Launcher 2.exe')
        if (!$entry -or $entry.Length -ne 5980160) { throw 'Unexpected fixture archive.' }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$exe,$false)
    } finally { $archive.Dispose() }
}
if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $expected) { throw 'Official fixture hash mismatch.' }
return $exe
