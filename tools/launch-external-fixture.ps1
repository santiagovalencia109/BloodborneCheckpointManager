param(
    [Parameter(Mandatory=$true)][string]$FixtureRoot,
    [string]$PortRoot='E:\Games\Bloodborne-Windows\bbport-windows',
    [string]$GameRoot='D:\Games\Bloodborne\CUSA03173'
)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$FixtureRoot=[IO.Path]::GetFullPath($FixtureRoot)
$profile=Get-Content -LiteralPath (Join-Path $FixtureRoot 'settings.json') -Raw | ConvertFrom-Json
$native=Join-Path $PortRoot 'bin\bb-probe.exe'
if ($profile.Executable -ne $native -or $profile.Saves -ne (Join-Path $FixtureRoot 'user\savedata')) { throw 'Only the isolated external fixture may be launched.' }
if (Get-Process bb-probe,bbport -ErrorAction SilentlyContinue) { throw 'Close the game before starting the isolated test.' }
$probe=Join-Path $project 'tests\Checkpoint.LiveProbe\bin\Release\net8.0-windows\Checkpoint.LiveProbe.dll'
$inspection=& dotnet $probe inspect-runtime $native
if ($LASTEXITCODE -ne 0) { throw 'The installed runtime does not satisfy the current ABI contract.' }
$inspection | Set-Content -LiteralPath (Join-Path $FixtureRoot 'runtime-inspection.json') -Encoding UTF8
foreach ($path in @((Join-Path $PortRoot 'out\boot-linked.bin'),(Join-Path $PortRoot 'out\content.bin'),(Join-Path $PortRoot 'out\patches.bin'),(Join-Path $GameRoot 'eboot.bin'))) {
    if (-not (Test-Path -LiteralPath $path)) { throw ('Missing verified input: '+$path) }
}
$env:BB_CONFIG=Join-Path $FixtureRoot 'bbport.ini'
$env:BB_GPU_USER_DIR=Join-Path $FixtureRoot 'user'
$env:BB_USER_DIR=$env:BB_GPU_USER_DIR
$env:BB_PAD_FILE=Join-Path $FixtureRoot 'pad.txt'
$env:BB_ONLINE='0'
$env:BB_PARTY='0'
$arguments=@(
    ('"'+(Join-Path $PortRoot 'out\boot-linked.bin')+'"'),
    '--content-profile',('"'+(Join-Path $PortRoot 'out\content.bin')+'"'),
    '--patches',('"'+(Join-Path $PortRoot 'out\patches.bin')+'"'),
    '--app0',('"'+$GameRoot+'"'),
    '--user',('"'+(Join-Path $FixtureRoot 'user')+'"'),'--timeout','0'
)
$process=Start-Process -FilePath $native -ArgumentList $arguments -WorkingDirectory $PortRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $FixtureRoot 'game.stdout.log') -RedirectStandardError (Join-Path $FixtureRoot 'game.stderr.log') -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $FixtureRoot 'game.pid') -Encoding ASCII
Write-Output ('Original Supermedo runtime, isolated saves, PID: '+$process.Id)
