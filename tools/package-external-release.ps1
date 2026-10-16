param([string]$Clang64Root, [ValidatePattern("^\d+\.\d+\.\d+(?:-experimental)?$")][string]$Version="0.9.2-experimental",[string]$PublishDirectory)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$artifacts=Join-Path $project 'artifacts'
$release=Join-Path $artifacts ("releases\"+$Version)
$package=if($PublishDirectory) { [IO.Path]::GetFullPath($PublishDirectory) } else { Join-Path $release "windows" }
if (-not (Test-Path -LiteralPath (Join-Path $package 'Native\bcm-external.dll'))) { throw 'Publish the matching manager and DLL before packaging.' }
$identity=Get-Content -LiteralPath (Join-Path $project 'src\Checkpoint.Core\ExternalBridgeIdentity.g.cs') -Raw
$hash=(Get-FileHash -LiteralPath (Join-Path $package 'Native\bcm-external.dll')).Hash
if (-not $identity.Contains($hash)) { throw 'The published DLL does not match the generated identity.' }
$awakenDll=Join-Path $package 'Native\bcm-awaken.dll'
$awakenHash=(Get-FileHash -LiteralPath $awakenDll).Hash
if(-not (Get-Content -LiteralPath (Join-Path $project 'src\Checkpoint.Core\AwakenBridgeIdentity.g.cs') -Raw).Contains($awakenHash)) { throw 'Awaken DLL identity mismatch' }
Copy-Item -LiteralPath (Join-Path $project 'README.md') -Destination (Join-Path $package 'README.md')
Copy-Item -LiteralPath (Join-Path $project 'docs') -Destination $package -Recurse -Force
$native=Join-Path $project 'native\external'
$nativeSource=Join-Path $package 'NativeSource\external'
New-Item -ItemType Directory -Path $nativeSource -Force | Out-Null
foreach ($name in @('bridge.cpp','hooks.cpp','hooks.h','layout.h','tick.S','test_tick.cpp','CMakeLists.txt','THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $native $name) -Destination (Join-Path $nativeSource $name)
}
$awakenSource=Join-Path $package 'NativeSource\awaken'
New-Item -ItemType Directory -Path $awakenSource -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $project 'native\awaken') -File | Copy-Item -Destination $awakenSource -Force
$vendor=Join-Path $nativeSource 'vendor\minhook'
New-Item -ItemType Directory -Path $vendor -Force | Out-Null
foreach ($name in @('src','include','cmake','CMakeLists.txt','LICENSE.txt')) {
    Copy-Item -LiteralPath (Join-Path $native ('vendor\minhook\'+$name)) -Destination $vendor -Recurse -Force
}
$licenses=Join-Path $package 'Licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
$gpl=Join-Path $project 'Licenses\GPL-3.0.txt'
if (-not (Test-Path -LiteralPath $gpl)) { $gpl=Join-Path (Split-Path -Parent $project) 'upstream-reference\LICENSE' }
Copy-Item -LiteralPath $gpl -Destination (Join-Path $licenses 'GPL-3.0.txt')
Copy-Item -LiteralPath (Join-Path $native 'vendor\minhook\LICENSE.txt') -Destination (Join-Path $licenses 'MinHook.txt')
if ($Clang64Root) {
    foreach ($component in @(@('json','LICENSE.MIT','nlohmann-json.txt'),@('libc++','LICENSE','LLVM-libcxx.txt'),@('compiler-rt','LICENSE','LLVM-compiler-rt.txt'))) {
        Copy-Item -LiteralPath (Join-Path $Clang64Root ('share\licenses\'+$component[0]+'\'+$component[1])) -Destination (Join-Path $licenses $component[2])
    }
}
Copy-Item -LiteralPath (Join-Path $native 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $licenses 'THIRD_PARTY_NOTICES.md')
$metadata=@{
    Version=$Version; NativeBridgeSha256=$hash; AwakenBridgeSha256=$awakenHash;
    CompatibilityFamilies=@('supermedo-runtime-c-v1','supermedo-runtime-c-atomic-v2'); CriticalFunctionShapes=@(100,108); InputAbi='orbis-pad-120-v1'; LegacyVerifiedHostSha256='4AE2D15EAFCE351C61DC10B8498B2BAD61CDC3A5C15C92E274FD63A7A4FC312A';
    Ports=@('supermedo','awaken'); AwakenEngine='0.9.33 / bbhost-0.4.0-fps66';
    InitialActivation='Manager open before game startup'; CoreTests=70; AwakenNativeTests=2;
    ManualAcceptance='User confirmed Supermedo 1.8.2 and Awaken 0.9.33 live checkpoints. Isolated Awaken F5/F9/F11 accepted on 2026-10-16; two restores recorded. 0.9.1 fixes dropdown discovery behavior; 0.9.2 fixes Browse paths. Native adapters unchanged. See docs/AWAKEN.md.';
    GameAssetsIncluded=$false; SavesIncluded=$false
}
$metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'release.json') -Encoding UTF8
$zip=Join-Path $release ("BloodborneCheckpointManager-v"+$Version+"-windows.zip")
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip -Force
# Archive the explicit source list directly: no duplicate extracted project or staging directory.
Push-Location $project
try {
    $files=@(& rg --files --no-require-git src tests tools native docs -g '!**/bin/**' -g '!**/obj/**' -g '!**/.git/**')
    if ($LASTEXITCODE) { throw 'Could not enumerate source files.' }
    $files+=@('README.md','Directory.Build.props','BloodborneCheckpointManager.sln','.gitignore')
    $entries=@($files | ForEach-Object { [pscustomobject]@{ Source=(Join-Path $project $_); Entry=$_.Replace('\','/') } })
    $entries+=@(Get-ChildItem -LiteralPath $licenses -File -Recurse | ForEach-Object {
        [pscustomobject]@{ Source=$_.FullName; Entry=('Licenses/'+[IO.Path]::GetRelativePath($licenses,$_.FullName).Replace('\','/')) }
    })
} finally { Pop-Location }
$sourceZip=Join-Path $release ("BloodborneCheckpointManager-v"+$Version+"-source.zip")
$stream=[IO.File]::Open($sourceZip,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::None)
$archive=$null
try {
    $archive=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
    foreach ($entry in $entries) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$entry.Source,$entry.Entry,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { if ($archive) { $archive.Dispose() }; $stream.Dispose() }
Get-FileHash -LiteralPath $zip,$sourceZip -Algorithm SHA256 | Select-Object Path,Hash
