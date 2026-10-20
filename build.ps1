$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found.' }
$dist = Join-Path $root 'dist'
[void][IO.Directory]::CreateDirectory($dist)
$exe = Join-Path $dist 'PCL下载补丁.exe'
$icon = Join-Path $root 'assets\patch.ico'
$help = Join-Path $root 'docs\help.txt'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/win32icon:$icon" "/resource:$icon,patch.ico" "/resource:$help,help.txt" "/out:$exe" (Join-Path $root 'src\PclDownloadPatch.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
$zip = Join-Path $dist 'pcl-download-patch-v1.1.0-windows.zip'
Compress-Archive -LiteralPath $exe,(Join-Path $root 'LICENSE'),(Join-Path $root 'NOTICE.md') -DestinationPath $zip -Force
Get-FileHash -LiteralPath $exe,$zip -Algorithm SHA256 | ForEach-Object { '{0}  {1}' -f $_.Hash.ToLowerInvariant(),(Split-Path $_.Path -Leaf) } | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii
Write-Output $exe
