param(
    [Parameter(Mandatory=$true)][string]$FixtureRoot,
    [string]$PortRoot = 'E:\Games\Bloodborne-Windows\bbport-windows',
    [string]$GameRoot = 'D:\Games\Bloodborne\CUSA03173',
    [string]$BuildRoot = 'C:\bcm-build'
)
$ErrorActionPreference = 'Stop'
$FixtureRoot = [IO.Path]::GetFullPath($FixtureRoot)
if (Get-Process bb-probe,bbport -ErrorAction SilentlyContinue) { throw 'Close the normal game before starting the isolated test.' }
$profilePath = Join-Path $FixtureRoot 'settings.json'
$profile = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json
$expectedSaves = Join-Path $FixtureRoot 'user\savedata'
if ($profile.Saves -ne $expectedSaves) { throw 'The test profile points outside the isolated fixture.' }
if (-not (Test-Path -LiteralPath $profile.Executable)) { throw 'The native checkpoint port has not finished compiling.' }
$imageRoot = Join-Path $FixtureRoot 'out'
foreach ($file in @('boot-linked.bin', 'content.bin')) {
    if (-not (Test-Path -LiteralPath (Join-Path $imageRoot $file))) { throw ('Missing compatible fixture image: ' + $file) }
}
if (-not (Test-Path -LiteralPath (Join-Path $GameRoot 'eboot.bin'))) { throw 'GameRoot does not contain the decrypted game.' }
$env:PATH = (Join-Path $BuildRoot 'msys64\clang64\bin') + ';' + $env:PATH
$env:BB_CHECKPOINT = '1'
$env:BB_CHECKPOINT_EXPERIMENTAL_RESTORE = '1'
$env:BB_PARTY = '0'
$env:BB_GPU_USER_DIR = Join-Path $FixtureRoot 'user'
$env:BB_CONFIG = Join-Path $FixtureRoot 'bbport.ini'
$env:BB_SAVE_TRACE = '1'
$env:BB_PAD_FILE = Join-Path $FixtureRoot 'pad.txt'
$arguments = @(
    ('"' + (Join-Path $imageRoot 'boot-linked.bin') + '"'),
    '--content-profile', ('"' + (Join-Path $imageRoot 'content.bin') + '"'),
    '--patches', ('"' + (Join-Path $PortRoot 'out\patches.bin') + '"'),
    '--app0', ('"' + $GameRoot + '"'),
    '--user', ('"' + (Join-Path $FixtureRoot 'user') + '"'), '--timeout', '0'
)
$process = Start-Process -FilePath $profile.Executable -ArgumentList $arguments -WorkingDirectory $FixtureRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $FixtureRoot 'game.stdout.log') -RedirectStandardError (Join-Path $FixtureRoot 'game.stderr.log') -PassThru
Write-Output ('Isolated native game PID: ' + $process.Id)
Write-Output ('Logs and test saves: ' + $FixtureRoot)
