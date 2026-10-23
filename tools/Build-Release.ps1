# Builds a complete release: Awaken bridge DLL (+ its native tests), core tests, manager publish and the
# two release ZIPs. The Supermedo bridge is reused from artifacts\external-native-build unless rebuilt there.
# Usage (PowerShell, from the repository root):
#   .\tools\Build-Release.ps1
#   .\tools\Build-Release.ps1 -Clang64Bin 'C:\bcm-build\msys64\clang64\bin' -SkipAwaken
param(
    [string]$Clang64Bin='C:\bcm-build\msys64\clang64\bin',
    [switch]$SkipAwaken
)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$csproj=Join-Path $project 'src\Checkpoint.App\Checkpoint.App.csproj'
$Version=([xml](Get-Content -LiteralPath $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if($Version -notmatch '^\d+\.\d+\.\d+(?:-experimental)?$') { throw "Unexpected <Version> in Checkpoint.App.csproj: $Version" }
Write-Output "== Bloodborne Checkpoint Manager $Version"

if(-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 8 SDK (x64) first.' }
$supermedoDll=Join-Path $project 'artifacts\external-native-build\bcm-external.dll'
if(-not (Test-Path -LiteralPath $supermedoDll)) { throw "Missing $supermedoDll (Supermedo bridge). Build it or copy it from the previous release's Native folder." }

if(-not $SkipAwaken) {
    if(-not (Test-Path -LiteralPath (Join-Path $Clang64Bin 'clang++.exe'))) { throw "CLANG64 toolchain not found in $Clang64Bin." }
    Write-Output '== Awaken bridge (native build + tests, regenerates AwakenBridgeIdentity.g.cs)'
    & (Join-Path $PSScriptRoot 'build-awaken-bridge.ps1') -Clang64Bin $Clang64Bin
}

Write-Output '== Core tests'
& dotnet build (Join-Path $project 'tests\Checkpoint.Tests\Checkpoint.Tests.csproj') -c Release --nologo -v quiet
if($LASTEXITCODE) { throw 'Test build failed.' }
& (Join-Path $project 'tests\Checkpoint.Tests\bin\Release\net8.0\Checkpoint.Tests.exe')
if($LASTEXITCODE) { throw 'Core tests failed.' }

Write-Output '== Publish manager'
$out=Join-Path $project "artifacts\releases\$Version\windows"
if(Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
& dotnet publish $csproj -c Release -r win-x64 --self-contained false -o $out --nologo -v quiet
if($LASTEXITCODE) { throw 'Publish failed.' }
foreach($file in 'BloodborneCheckpointManager.exe','Native\bcm-external.dll','Native\bcm-awaken.dll') {
    if(-not (Test-Path -LiteralPath (Join-Path $out $file))) { throw "Published output is missing $file." }
}

Write-Output '== Package'
& (Join-Path $PSScriptRoot 'package-external-release.ps1') -Clang64Root (Split-Path -Parent $Clang64Bin) -Version $Version -PublishDirectory $out
$release=Join-Path $project "artifacts\releases\$Version"
Get-ChildItem -LiteralPath $release -Filter '*.zip' | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName).Hash,$_.Name }
Write-Output ''
Write-Output "Done. Publish on GitHub (tag v$Version, not a draft) with both ZIPs, for example:"
Write-Output "  gh release create v$Version `"$release\BloodborneCheckpointManager-v$Version-windows.zip`" `"$release\BloodborneCheckpointManager-v$Version-source.zip`" --title `"BB Checkpoint Manager $($Version -replace '-experimental','')`" --notes-file `"$release\release-notes.md`""
