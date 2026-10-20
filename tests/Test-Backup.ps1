param([Parameter(Mandatory=$true)][string]$OriginalLauncher)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
[void][Reflection.Assembly]::LoadFile((Join-Path $root 'dist\PCL-Download-Patch.exe'))
$sourcePlan=[PatchEngine]::Inspect($OriginalLauncher)
if($sourcePlan.State -ne '可应用补丁'){throw 'Provide an unmodified compatible launcher or its original backup.'}
$original=[IO.File]::ReadAllBytes($OriginalLauncher)
$hash=[PatchEngine]::Hash($original)
$folder=Join-Path $root ('.test-work\'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($folder)
$target=Join-Path $folder 'Plain Craft Launcher.exe'
[IO.File]::WriteAllBytes($target,$original)
[void][PatchEngine]::Apply($target)
$backupFolder=[PatchEngine]::BackupFolder($target)
$files=@(Get-ChildItem -LiteralPath $backupFolder -Filter '*.bak')
if($files.Count -ne 1 -or $files[0].Name -notmatch '^Plain Craft Launcher_v.+_原版备份_\d{8}_\d{6}\.bak$'){throw 'Invalid backup name or location.'}
if((Get-FileHash -LiteralPath $files[0].FullName).Hash.ToLowerInvariant() -ne $hash){throw 'Backup hash mismatch.'}
$patchedHash=[PatchEngine]::Hash([IO.File]::ReadAllBytes($target))
[void][PatchEngine]::Apply($target)
if([PatchEngine]::Hash([IO.File]::ReadAllBytes($target)) -ne $patchedHash){throw 'Repeated apply changed the file.'}
if(@(Get-ChildItem -LiteralPath $backupFolder -Filter '*.bak').Count -ne 1){throw 'Duplicate backup created.'}
[void][PatchEngine]::Restore($target)
if([PatchEngine]::Hash([IO.File]::ReadAllBytes($target)) -ne $hash){throw 'Restore hash mismatch.'}
[void][PatchEngine]::Apply($target)
if(@(Get-ChildItem -LiteralPath $backupFolder -Filter '*.bak').Count -ne 1){throw 'Existing backup was not reused.'}
$legacyFolder=Join-Path $folder 'legacy';[void][IO.Directory]::CreateDirectory($legacyFolder)
$legacy=Join-Path $legacyFolder 'Plain Craft Launcher.exe'
[IO.File]::WriteAllBytes($legacy,[IO.File]::ReadAllBytes($target))
[IO.File]::WriteAllBytes(($legacy+'.download-patch.'+$hash+'.bak'),$original)
[void][PatchEngine]::Restore($legacy)
if([PatchEngine]::Hash([IO.File]::ReadAllBytes($legacy)) -ne $hash){throw 'Legacy backup restore failed.'}
[IO.File]::WriteAllBytes(($target+'.download-patch.'+$hash+'.bak'),$original)
[void][PatchEngine]::Restore($target)
if([PatchEngine]::Hash([IO.File]::ReadAllBytes($target)) -ne $hash){throw 'Identical backups prevented restore.'}
[void][PatchEngine]::Apply($target)
$before=[PatchEngine]::Hash([IO.File]::ReadAllBytes($target))
# Both possible matching backup copies are deliberately corrupted in the test folder.
[IO.File]::WriteAllBytes($files[0].FullName,[byte[]](0,1,2,3))
[IO.File]::WriteAllBytes(($target+'.download-patch.'+$hash+'.bak'),[byte[]](0,1,2,3))
$rejected=$false;try{[void][PatchEngine]::Restore($target)}catch{$rejected=$true}
if(!$rejected -or [PatchEngine]::Hash([IO.File]::ReadAllBytes($target)) -ne $before){throw 'Corrupt backups were not safely rejected.'}
$resources=[Reflection.Assembly]::LoadFile((Join-Path $root 'dist\PCL-Download-Patch.exe')).GetManifestResourceNames()
if($resources -notcontains 'patch.ico' -or $resources -notcontains 'help.txt'){throw 'Missing embedded help or icon.'}
if([PatchEngine]::Hash([IO.File]::ReadAllBytes($OriginalLauncher)) -ne $hash){throw 'The source fixture changed.'}
Write-Output 'PASS: apply, idempotency, readable backup names, reuse, exact restore, legacy compatibility, duplicate backups, corrupt backup rejection, embedded resources, source unchanged.'
