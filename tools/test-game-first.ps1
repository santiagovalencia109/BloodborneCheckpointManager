param([Parameter(Mandatory=$true)][string]$FixtureRoot,[string]$ManagerVersion="0.9.5-experimental")
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'ReleasePaths.ps1')
$project=Split-Path -Parent $PSScriptRoot
$FixtureRoot=[IO.Path]::GetFullPath($FixtureRoot)
$expectedFixture=Join-Path $project 'artifacts\external-fixture-01'
if ($FixtureRoot -ne $expectedFixture) { throw 'This test only accepts the existing isolated fixture.' }
if (Get-Process bb-probe,bbport -ErrorAction SilentlyContinue) { throw 'Close the game normally first; this test does not close or kill a game.' }
$managers=@(Get-CimInstance Win32_Process -Filter "Name='BloodborneCheckpointManager.exe'")
foreach ($details in $managers) {
    $expectedManager=Join-Path (Get-ManagerReleaseDirectory -Version $ManagerVersion) 'BloodborneCheckpointManager.exe'
    if ($details.ExecutablePath -ne $expectedManager -or -not $details.CommandLine.Contains($FixtureRoot)) { throw 'Another manager is running; refusing to close it.' }
    $owned=Get-Process -Id $details.ProcessId
    if (-not $owned.CloseMainWindow() -or -not $owned.WaitForExit(10000)) { throw 'The isolated manager did not close normally; no process was killed.' }
}
$probes=@(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object { $_.CommandLine -like '*Checkpoint.LiveProbe*' })
if ($probes.Count) { throw 'A live probe watcher is running; refuse an ambiguous game-first test.' }
& (Join-Path $PSScriptRoot 'launch-external-fixture.ps1') -FixtureRoot $FixtureRoot
$startedGame=[int](Get-Content -LiteralPath (Join-Path $FixtureRoot 'game.pid'))
$game=Get-Process -Id $startedGame
$limit=[DateTime]::UtcNow.AddSeconds(45)
$ready=$false
while ([DateTime]::UtcNow -lt $limit) {
    $game.Refresh()
    if ($game.HasExited) { throw 'Isolated game exited before guest startup.' }
    $output=Get-Content -LiteralPath (Join-Path $FixtureRoot 'game.stdout.log') -Raw
    if ($output -match "save data 'SPRJ0005' mounted") { $ready=$true; break }
    Start-Sleep -Milliseconds 250
}
if (-not $ready) { throw 'No savedata mount was observed; manager was not opened.' }
# This marker proves guest execution and savedata mounting preceded the manager.
$gameReadyAt=[DateTimeOffset]::Now
$manager=Join-Path (Get-ManagerReleaseDirectory -Version $ManagerVersion) 'BloodborneCheckpointManager.exe'
$opened=Start-Process -FilePath $manager -ArgumentList @('--data-directory',('"'+$FixtureRoot+'"')) -WorkingDirectory (Split-Path -Parent $manager) -WindowStyle Normal -PassThru
$opened.Id | Set-Content -LiteralPath (Join-Path $FixtureRoot 'manager.pid') -Encoding ASCII
$result=[ordered]@{ GamePid=$startedGame; GameReadyBeforeManager=$gameReadyAt.ToString('O'); ManagerPid=$opened.Id; ManagerOpened=[DateTimeOffset]::Now.ToString('O'); NoRestoreRequested=$true }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $FixtureRoot 'game-first-order.json') -Encoding UTF8
$result | ConvertTo-Json
