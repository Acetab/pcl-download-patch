param([Parameter(Mandatory=$true)][string]$OriginalLauncher,[string]$WorkDirectory)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (!$WorkDirectory) { $WorkDirectory = Join-Path $root ('.test-work\' + [guid]::NewGuid().ToString('N')) }
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
[void][IO.Directory]::CreateDirectory($WorkDirectory)
$bin = Join-Path $WorkDirectory 'tools'
[void][IO.Directory]::CreateDirectory($bin)
& (Join-Path $root 'build.ps1')
$exe = Join-Path $root 'dist\PCL-Download-Patch.exe'
Copy-Item -LiteralPath $exe,(Join-Path $root 'vendor\Mono.Cecil.dll') -Destination $bin
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs = @('/reference:System.Core.dll','/reference:System.Web.Extensions.dll',"/reference:$exe",('/reference:' + (Join-Path $bin 'Mono.Cecil.dll')))
foreach ($name in @('EngineTests','DispatchTests','VerifyRuntime','ThemeTests','VerifyThemeRuntime')) {
    & $compiler /nologo /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $refs ('/out:' + (Join-Path $bin "$name.exe")) (Join-Path $root "tests\$name.cs")
    if ($LASTEXITCODE -ne 0) { throw "Test compilation failed: $name" }
}
$cases = Join-Path $WorkDirectory 'engine'
& (Join-Path $bin 'EngineTests.exe') $OriginalLauncher $cases | Tee-Object -FilePath (Join-Path $WorkDirectory 'engine.txt')
if ($LASTEXITCODE -ne 0) { throw 'Engine tests failed.' }
$fixture = Join-Path $cases 'copy\PCL.repaired.exe'
& (Join-Path $bin 'VerifyRuntime.exe') $fixture | Tee-Object -FilePath (Join-Path $WorkDirectory 'jit.txt')
if ($LASTEXITCODE -ne 0) { throw 'CLR JIT validation failed.' }
& (Join-Path $bin 'DispatchTests.exe') $fixture (Join-Path $WorkDirectory 'dispatch') | Tee-Object -FilePath (Join-Path $WorkDirectory 'dispatch.txt')
if ($LASTEXITCODE -ne 0) { throw 'Dispatch tests failed.' }
$themeCases = Join-Path $WorkDirectory 'themes'
& (Join-Path $bin 'ThemeTests.exe') $OriginalLauncher $themeCases | Tee-Object -FilePath (Join-Path $WorkDirectory 'themes.txt')
if ($LASTEXITCODE -ne 0) { throw 'Theme tests failed.' }
& (Join-Path $bin 'VerifyThemeRuntime.exe') (Join-Path $themeCases 'copy\PCL.themes.exe') | Tee-Object -FilePath (Join-Path $WorkDirectory 'theme-jit.txt')
if ($LASTEXITCODE -ne 0) { throw 'Theme JIT validation failed.' }
# Prove that the deliverable runs with no loose Cecil DLL beside it.
$standalone = Join-Path $WorkDirectory 'standalone'
[void][IO.Directory]::CreateDirectory($standalone)
$single = Join-Path $standalone 'Repair.exe'
Copy-Item -LiteralPath $exe -Destination $single
foreach ($item in @(@('--check',$OriginalLauncher,0),@('--check',$fixture,2))) {
    $process = Start-Process -FilePath $single -ArgumentList @($item[0],('"' + $item[1] + '"')) -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $standalone ('out-' + $item[2] + '.txt')) -RedirectStandardError (Join-Path $standalone ('err-' + $item[2] + '.txt'))
    if ($process.ExitCode -ne $item[2]) { throw 'Standalone CLI/dependency validation failed.' }
}
Write-Output "ALL_TESTS_OK: $WorkDirectory"
