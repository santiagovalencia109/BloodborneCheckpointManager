param(
    [string]$PortRoot = 'E:\Games\Bloodborne-Windows\bbport-windows',
    [string]$NativeExecutable = 'C:\bcm-build\port\out\bbport.exe',
    [Parameter(Mandatory=$true)][string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) { throw 'Use a new fixture directory; existing fixtures are never overwritten.' }
$originalExecutable = Join-Path $PortRoot 'bin\bb-probe.exe'
if (-not (Test-Path -LiteralPath $originalExecutable)) { throw 'Expected packaged native game executable missing.' }
if (Get-Process bb-probe,bbport -ErrorAction SilentlyContinue) { throw 'Close the game before copying its saves.' }
$source = Join-Path $PortRoot 'user\savedata'
if (-not (Test-Path -LiteralPath $source)) { throw 'Save source does not exist.' }
New-Item -ItemType Directory -Path (Join-Path $OutputRoot 'user') -Force | Out-Null
Copy-Item -LiteralPath $source -Destination (Join-Path $OutputRoot 'user\savedata') -Recurse
if (Get-Process bb-probe,bbport -ErrorAction SilentlyContinue) { throw 'Game started during fixture copy. Do not use this copy.' }
$settings = @{
    Executable=$NativeExecutable; Saves=(Join-Path $OutputRoot 'user\savedata'); Library=(Join-Path $OutputRoot 'checkpoints');
    ExtraProcessNames=''; LaunchPath=''; LaunchArguments=''; WorkingDirectory=$OutputRoot;
    RestartAfterRestore=$false; RequestWindowClose=$false; LiveCheckpoints=$true
}
$settings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputRoot 'settings.json') -Encoding UTF8
Set-Content -LiteralPath (Join-Path $OutputRoot 'pad.txt') -Value '' -Encoding ASCII
@('upscaler=fsr3','preset=0','render_res=1920x1080','output_res=1920x1080','show_fps=1','mk_enabled=0','skip_intro=1') | Set-Content -LiteralPath (Join-Path $OutputRoot 'bbport.ini') -Encoding ASCII
Write-Output ('Fixture created: ' + $OutputRoot)
Write-Output 'Original installation and saves were only read.'
