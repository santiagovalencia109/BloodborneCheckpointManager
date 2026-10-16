param([Parameter(Mandatory=$true)][string]$FixtureRoot,
      [ValidatePattern('^\d+\.\d+\.\d+-experimental$')][string]$Version='0.8.4-experimental')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'ReleasePaths.ps1')
$project=Split-Path -Parent $PSScriptRoot
$FixtureRoot=[IO.Path]::GetFullPath($FixtureRoot)
if ($FixtureRoot -ne (Join-Path $project 'artifacts\external-fixture-01')) { throw 'Only the known isolated fixture is accepted.' }
$manager=Join-Path (Get-ManagerReleaseDirectory -Version $Version) 'BloodborneCheckpointManager.exe'
if (-not (Test-Path -LiteralPath $manager)) { throw 'Publish the new manager before switching.' }
$profile=Get-Content -LiteralPath (Join-Path $FixtureRoot 'settings.json') -Raw | ConvertFrom-Json
if ($profile.Saves -ne (Join-Path $FixtureRoot 'user\savedata') -or $profile.Library -ne (Join-Path $FixtureRoot 'checkpoints')) { throw 'The profile is no longer isolated.' }
if (Test-Path -LiteralPath (Join-Path $profile.Library 'pending-restore.json')) { throw 'A pending restoration requires recovery first.' }
$oldId=[int](Get-Content -LiteralPath (Join-Path $FixtureRoot 'manager.pid'))
$old=Get-Process -Id $oldId -ErrorAction SilentlyContinue
if ($old) {
    $details=Get-CimInstance Win32_Process -Filter ('ProcessId='+$oldId)
    $artifactRoot=[IO.Path]::GetFullPath((Join-Path $project 'artifacts'))+'\'
    if (-not $details.ExecutablePath.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($details.ExecutablePath) -ne 'BloodborneCheckpointManager.exe' -or
        -not $details.CommandLine.Contains($FixtureRoot)) { throw 'Refusing to close an unrelated process.' }
    $oldDll=Join-Path (Split-Path -Parent $details.ExecutablePath) 'Native\bcm-external.dll'
    $newDll=Join-Path (Split-Path -Parent $manager) 'Native\bcm-external.dll'
    if ((Get-FileHash -LiteralPath $oldDll).Hash -ne (Get-FileHash -LiteralPath $newDll).Hash) { throw 'Native revisions differ; restart the game instead.' }
    if (-not $old.CloseMainWindow() -or -not $old.WaitForExit(10000)) { throw 'Manager did not close normally, possibly busy. No process was killed.' }
}
$opened=Start-Process -FilePath $manager -ArgumentList @('--data-directory',('"'+$FixtureRoot+'"')) -WorkingDirectory (Split-Path -Parent $manager) -WindowStyle Normal -PassThru
$opened.Id | Set-Content -LiteralPath (Join-Path $FixtureRoot 'manager.pid') -Encoding ASCII
Write-Output ('Isolated manager replaced, PID '+$opened.Id+'. Game process left running.')
