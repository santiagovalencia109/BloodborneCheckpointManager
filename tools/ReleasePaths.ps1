function Get-ManagerReleaseDirectory {
    param([ValidatePattern('^\d+\.\d+\.\d+(?:-experimental)?$')][string]$Version='0.9.4-experimental')
    $projectRoot=Split-Path -Parent $PSScriptRoot
    $candidates=@(
        (Join-Path $projectRoot ('artifacts\releases\'+$Version+'\windows')),
        (Join-Path $projectRoot ('artifacts\archive\releases\'+$Version+'\windows')),
        (Join-Path $projectRoot ('artifacts\windows-v'+$Version))
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'BloodborneCheckpointManager.exe')) { return $candidate }
    }
    throw ('No published manager for '+$Version+'. Publish to '+$candidates[0])
}
