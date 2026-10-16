[CmdletBinding(SupportsShouldProcess=$true)]
param([ValidatePattern('^\d+\.\d+\.\d+(?:-experimental)?$')][string]$CurrentVersion='0.9.2-experimental')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$prefix=$artifactRoot+[IO.Path]::DirectorySeparatorChar
function Assert-ArtifactChild([string]$Path) {
    $resolved=[IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw ('Path outside artifacts: '+$resolved) }
    return $resolved
}
# A loaded executable or DLL may retain its old absolute path. Never move its directory.
$active=@(Get-Process BloodborneCheckpointManager,bb-probe,bbport -ErrorAction SilentlyContinue)
if ($active.Count) { throw 'Close the manager and game normally before organizing published builds. No process was closed.' }
$plan=@()
foreach ($item in Get-ChildItem -LiteralPath $artifactRoot -Force) {
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw ('Refusing a linked artifact: '+$item.FullName) }
    $target=$null
    if ($item.PSIsContainer -and $item.Name -match '^windows-v(?<version>\d+\.\d+\.\d+(?:-experimental)?)$') {
        $version=$Matches.version
        $category=if ($version -eq $CurrentVersion) { 'releases' } else { 'archive\releases' }
        $target=Join-Path $artifactRoot ($category+'\'+$version+'\windows')
    } elseif ($item.PSIsContainer -and $item.Name -match '^source-v(?<version>\d+\.\d+\.\d+(?:-experimental)?)$') {
        $target=Join-Path $artifactRoot ('archive\source-snapshots\'+$Matches.version)
    } elseif (-not $item.PSIsContainer -and $item.Name -match '^BloodborneCheckpointManager-v(?<version>\d+\.\d+\.\d+(?:-experimental)?)-(windows|source)\.zip$') {
        $version=$Matches.version
        if ($version -eq '0.5.0') { $version='0.5.0-experimental' }
        $category=if ($version -eq $CurrentVersion) { 'releases' } else { 'archive\releases' }
        $target=Join-Path $artifactRoot ($category+'\'+$version+'\'+$item.Name)
    } elseif ($item.PSIsContainer -and $item.Name -match '^(preview(?:-.*)?|ui-.*)$') {
        $target=Join-Path $artifactRoot ('previews\'+$item.Name)
    } elseif ($item.PSIsContainer -and $item.Name -match '^BloodborneCheckpointManager-v.*-windows$') {
        $target=Join-Path $artifactRoot ('archive\extracted-packages\'+$item.Name)
    } elseif ($item.Name -in @('windows','BloodborneCheckpointManager-windows.zip')) {
        $target=Join-Path $artifactRoot ('archive\unversioned\'+$item.Name)
    } elseif ($item.Name -in @('audit-catalogs.py','checkpoint-bridge.o','checkpoint-io-test.exe','current-symbols.txt','MainForm-prefix.txt','migrate-localization.py','runtime-resolution.json')) {
        $target=Join-Path $artifactRoot ('archive\development-files\'+$item.Name)
    }
    if ($target) {
        $from=Assert-ArtifactChild $item.FullName
        $to=Assert-ArtifactChild $target
        if (Test-Path -LiteralPath $to) { throw ('Destination already exists; nothing was moved: '+$to) }
        $plan+=[pscustomobject]@{From=$from;To=$to}
    }
}
# Validate every final absolute path and collision before the first recursive directory move.
foreach ($move in $plan) {
    if ($PSCmdlet.ShouldProcess($move.From,'Move to '+$move.To)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $move.To) -Force | Out-Null
        Move-Item -LiteralPath $move.From -Destination $move.To
    }
}
if (-not $WhatIfPreference -and $plan.Count) {
    $history=Join-Path $artifactRoot ('archive\organization-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.json')
    $plan | ConvertTo-Json | Set-Content -LiteralPath $history -Encoding UTF8
}
Write-Output ($plan.Count.ToString()+' artifact entries organized. Save fixtures and native build cache stay at their existing paths.')
