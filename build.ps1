$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
[void][IO.Directory]::CreateDirectory((Join-Path $root 'dist'))
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path (Split-Path $compiler) 'WPF'
$exe = Join-Path $root 'dist\PCL-Download-Patch.exe'
$icon = Join-Path $root 'assets\patch.ico'
$help = Join-Path $root 'docs\help.txt'
$cecil = Join-Path $root 'vendor\Mono.Cecil.dll'
$palette = Join-Path $root 'assets\theme-palette.json'
if ((Get-FileHash -LiteralPath $cecil -Algorithm SHA256).Hash -ne 'C41BDB9FFD3C5F6E17D2382C1012D73703E035E3F1100245FDD4E08C8DC6EB5B') { throw 'Mono.Cecil dependency hash mismatch.' }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/reference:$wpf\PresentationFramework.dll" "/reference:$wpf\PresentationCore.dll" "/reference:$wpf\WindowsBase.dll" "/reference:$cecil" "/resource:$cecil,Mono.Cecil.dll" "/resource:$palette,theme-palette.json" "/resource:$icon,patch.ico" "/resource:$help,help.txt" "/win32icon:$icon" "/out:$exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output $exe
