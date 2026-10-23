param([Parameter(Mandatory=$true)][string]$FixtureRoot, [string]$ManagerVersion="0.9.5-experimental")
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'ReleasePaths.ps1')
$project=Split-Path -Parent $PSScriptRoot
$FixtureRoot=[IO.Path]::GetFullPath($FixtureRoot)
if (Get-Process bb-probe,bbport -ErrorAction SilentlyContinue) { throw 'Close the game before testing automatic manager attachment.' }
$manager=Join-Path (Get-ManagerReleaseDirectory -Version $ManagerVersion) 'BloodborneCheckpointManager.exe'
if (-not (Test-Path -LiteralPath $manager)) { throw 'Publish the matching manager first.' }
$args=@('--data-directory',('"'+$FixtureRoot+'"'))
$process=Start-Process -FilePath $manager -ArgumentList $args -WorkingDirectory (Split-Path -Parent $manager) -WindowStyle Normal -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $FixtureRoot 'manager.pid') -Encoding ASCII
$limit=[DateTime]::UtcNow.AddSeconds(10)
while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $limit) {
    if ($process.HasExited) { throw 'Manager exited before showing its window.' }
    Start-Sleep -Milliseconds 100; $process.Refresh()
}
if ($process.MainWindowHandle -eq 0) { throw 'Manager window did not become ready.' }
& (Join-Path $PSScriptRoot 'launch-external-fixture.ps1') -FixtureRoot $FixtureRoot
Write-Output ('Manager automatic watcher PID: '+$process.Id)
