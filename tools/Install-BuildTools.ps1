param([string]$Destination = 'C:\bcm-build')
$ErrorActionPreference = 'Stop'
if ($Destination -notmatch '^[A-Za-z]:[\\/]') { throw 'Destination must be absolute.' }
$Destination = [IO.Path]::GetFullPath($Destination)
if ($Destination -eq [IO.Path]::GetPathRoot($Destination)) { throw 'Refusing a drive root.' }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$release = Invoke-RestMethod 'https://api.github.com/repos/msys2/msys2-installer/releases/latest'
$asset = $release.assets | Where-Object { $_.name -match '^msys2-base-x86_64.*\.sfx\.exe$' } | Select-Object -First 1
if (-not $asset) { throw 'Official MSYS2 archive asset not found.' }
$archive = Join-Path $Destination $asset.name
if (-not (Test-Path -LiteralPath $archive)) {
    & curl.exe -L --fail --retry 3 --silent --show-error $asset.browser_download_url -o $archive
    if ($LASTEXITCODE -ne 0) { throw 'Archive download failed.' }
}
$expected = $asset.digest -replace '^sha256:', ''
if ($expected -notmatch '^[0-9a-fA-F]{64}$') {
    $checksumFile = $archive + '.sha256'
    & curl.exe -L --fail --retry 3 --silent --show-error ($asset.browser_download_url + '.sha256') -o $checksumFile
    if ($LASTEXITCODE -ne 0) { throw 'Official checksum download failed.' }
    $expected = ((Get-Content -LiteralPath $checksumFile -Raw).Trim() -split '\s+')[0]
}
$actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
if ($expected -notmatch '^[0-9a-fA-F]{64}$' -or $actual -ne $expected) { throw 'Archive checksum mismatch.' }
$process = Start-Process -FilePath $archive -ArgumentList @('-y', ('-o' + $Destination)) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'MSYS2 extraction failed.' }
Write-Output ('Verified MSYS2 portable archive: ' + $asset.name)
Write-Output ('Build environment: ' + (Join-Path $Destination 'msys64'))
