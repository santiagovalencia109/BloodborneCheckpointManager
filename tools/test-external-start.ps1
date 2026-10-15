param([Parameter(Mandatory=$true)][string]$FixtureRoot,[switch]$CloseOnly)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$FixtureRoot=[IO.Path]::GetFullPath($FixtureRoot)
$pidFile=Join-Path $FixtureRoot 'game.pid'
if (Test-Path -LiteralPath $pidFile) {
    $testPid=[int](Get-Content -LiteralPath $pidFile)
    $testProcess=Get-Process -Id $testPid -ErrorAction SilentlyContinue
    if ($testProcess) {
        $details=Get-CimInstance Win32_Process -Filter ('ProcessId='+$testPid)
        if ($details.ExecutablePath -ne 'E:\Games\Bloodborne-Windows\bbport-windows\bin\bb-probe.exe' -or -not $details.CommandLine.Contains((Join-Path $FixtureRoot 'user'))) { throw 'Refusing to close a process that is not this isolated fixture.' }
        $null=$testProcess.CloseMainWindow()
        if (-not $testProcess.WaitForExit(5000)) { Stop-Process -Id $testPid; $testProcess.WaitForExit() }
    }
}
if ($CloseOnly) { Write-Output 'Only the isolated game was closed.'; return }
$probe=Join-Path $project 'tests\Checkpoint.LiveProbe\bin\Release\net8.0-windows'
New-Item -ItemType Directory -Path (Join-Path $probe 'Native') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $project 'artifacts\external-native-build\bcm-external.dll') -Destination (Join-Path $probe 'Native\bcm-external.dll')
$watchArguments=@(('"'+(Join-Path $probe 'Checkpoint.LiveProbe.dll')+'"'),('"'+(Join-Path $FixtureRoot 'settings.json')+'"'),'watch')
$watch=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList $watchArguments -WorkingDirectory $FixtureRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $FixtureRoot 'watch.stdout.log') -RedirectStandardError (Join-Path $FixtureRoot 'watch.stderr.log') -PassThru
$watch.Id | Set-Content -LiteralPath (Join-Path $FixtureRoot 'watch.pid') -Encoding ASCII
& (Join-Path $PSScriptRoot 'launch-external-fixture.ps1') -FixtureRoot $FixtureRoot
Write-Output ('Watcher PID: '+$watch.Id)
