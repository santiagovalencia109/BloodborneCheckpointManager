param([Parameter(Mandatory=$true)][string]$FixtureRoot,
      [ValidatePattern('^\d+\.\d+\.\d+-experimental$')][string]$Version='0.9.2-experimental')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'ReleasePaths.ps1')
$project=Split-Path -Parent $PSScriptRoot
$FixtureRoot=[IO.Path]::GetFullPath($FixtureRoot)
$knownFixtures=@{ (Join-Path $project 'artifacts\external-fixture-01')='supermedo'; (Join-Path $project 'artifacts\awaken-fixture-01')='awaken' }
if (-not $knownFixtures.ContainsKey($FixtureRoot)) { throw 'Only known isolated fixtures are accepted.' }
$manager=Join-Path (Get-ManagerReleaseDirectory -Version $Version) 'BloodborneCheckpointManager.exe'
if (-not (Test-Path -LiteralPath $manager)) { throw 'Publish the new manager before switching.' }
$profile=Get-Content -LiteralPath (Join-Path $FixtureRoot 'settings.json') -Raw | ConvertFrom-Json
$port=if($profile.Port) { $profile.Port } else { 'supermedo' }
if($port -ne $knownFixtures[$FixtureRoot]) { throw 'Fixture port identity mismatch' }
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
    $bridgeName=if($port -eq 'awaken') { 'bcm-awaken.dll' } else { 'bcm-external.dll' }
    $oldDll=Join-Path (Split-Path -Parent $details.ExecutablePath) ('Native\'+$bridgeName)
    $newDll=Join-Path (Split-Path -Parent $manager) ('Native\'+$bridgeName)
    if ((Get-FileHash -LiteralPath $oldDll).Hash -ne (Get-FileHash -LiteralPath $newDll).Hash) { throw 'Native revisions differ; restart the game instead.' }
    if (-not $old.CloseMainWindow() -or -not $old.WaitForExit(10000)) { throw 'Manager did not close normally, possibly busy. No process was killed.' }
}
$opened=Start-Process -FilePath $manager -ArgumentList @('--data-directory',('"'+$FixtureRoot+'"')) -WorkingDirectory (Split-Path -Parent $manager) -WindowStyle Normal -PassThru
$opened.Id | Set-Content -LiteralPath (Join-Path $FixtureRoot 'manager.pid') -Encoding ASCII
Write-Output ('Isolated manager replaced, PID '+$opened.Id+'. Game process left running.')
