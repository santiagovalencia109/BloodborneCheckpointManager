param([Parameter(Mandatory=$true)][string]$FixtureRoot,
 [Parameter(Mandatory=$true)][string]$PortRoot,
 [Parameter(Mandatory=$true)][string]$PreparedRoot,
 [Parameter(Mandatory=$true)][string]$GameRoot,
 [string]$Version='0.9.5-experimental')
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$FixtureRoot=[IO.Path]::GetFullPath($FixtureRoot)
if($FixtureRoot -ne (Join-Path $project 'artifacts\awaken-fixture-01')) { throw 'Only the isolated Awaken fixture is accepted' }
$p=Get-Content -LiteralPath (Join-Path $FixtureRoot 'settings.json') -Raw | ConvertFrom-Json
$native=Join-Path $PortRoot 'engine\out\bb-probe.exe'
if($p.Port -ne 'awaken' -or $p.Saves -ne (Join-Path $FixtureRoot 'user\savedata') -or $p.Executable -ne $native -or $p.Library -ne (Join-Path $FixtureRoot 'checkpoints')) { throw 'Profile is not isolated' }
if(Get-Process bb-probe,bbport -ErrorAction SilentlyContinue) { throw 'Close the game normally before starting this test' }
foreach($name in @('boot-linked.bin','content.bin','patches.bin')) { if(-not (Test-Path -LiteralPath (Join-Path $PreparedRoot $name))) { throw ('Missing prepared resource: '+$name) } }
$inspection=& dotnet (Join-Path $project 'tests\Checkpoint.LiveProbe\bin\Release\net8.0-windows\Checkpoint.LiveProbe.dll') inspect-runtime $native awaken
if($LASTEXITCODE) { throw 'Unsupported Awaken engine' }
$inspection | Set-Content -LiteralPath (Join-Path $FixtureRoot 'runtime-inspection.json') -Encoding UTF8
$manager=Join-Path $project ('artifacts\releases\'+$Version+'\windows\BloodborneCheckpointManager.exe')
$m=Start-Process -FilePath $manager -ArgumentList @('--data-directory',('"'+$FixtureRoot+'"')) -WorkingDirectory (Split-Path -Parent $manager) -WindowStyle Normal -PassThru
$m.Id | Set-Content -LiteralPath (Join-Path $FixtureRoot 'manager.pid')
$limit=[DateTime]::UtcNow.AddSeconds(10)
while(-not $m.MainWindowHandle -and [DateTime]::UtcNow -lt $limit) { if($m.HasExited) { throw 'Manager exited' }; Start-Sleep -Milliseconds 100; $m.Refresh() }
if(-not $m.MainWindowHandle) { throw 'Manager window is not ready' }
$env:BB_CONFIG=Join-Path $FixtureRoot 'bbport.ini'; $env:BB_DATA_DIR=$FixtureRoot
$env:BB_USER_DIR=Join-Path $FixtureRoot 'user'; $env:BB_GPU_USER_DIR=$env:BB_USER_DIR
$env:BB_PAD_FILE=Join-Path $FixtureRoot 'pad.txt'; $env:BB_ONLINE='0'; $env:BB_PARTY='0'
$arguments=@(('"'+(Join-Path $PreparedRoot 'boot-linked.bin')+'"'),'--content-profile',('"'+(Join-Path $PreparedRoot 'content.bin')+'"'),'--patches',('"'+(Join-Path $PreparedRoot 'patches.bin')+'"'),'--app0',('"'+$GameRoot+'"'),'--user',('"'+$env:BB_USER_DIR+'"'),'--timeout','0')
$g=Start-Process -FilePath $native -ArgumentList $arguments -WorkingDirectory (Split-Path -Parent $native) -WindowStyle Hidden -RedirectStandardOutput (Join-Path $FixtureRoot 'game.stdout.log') -RedirectStandardError (Join-Path $FixtureRoot 'game.stderr.log') -PassThru
$g.Id | Set-Content -LiteralPath (Join-Path $FixtureRoot 'game.pid')
Write-Output ('Awaken isolated game PID '+$g.Id+', manager PID '+$m.Id)
