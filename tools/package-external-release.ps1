param([string]$Clang64Root, [ValidatePattern("^\d+\.\d+\.\d+(?:-experimental)?$")][string]$Version="0.9.5-experimental",[string]$PublishDirectory)
$ErrorActionPreference='Stop'
# Windows PowerShell 5.1 (.NET Framework) has no Path.GetRelativePath and may not preload the zip types.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
function Get-RelativePath([string]$Base,[string]$Path) {
    $root=[IO.Path]::GetFullPath($Base).TrimEnd('\')+'\'; $full=[IO.Path]::GetFullPath($Path)
    if(-not $full.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)) { throw "$Path is outside $Base" }
    return $full.Substring($root.Length)
}
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
    CompatibilityFamilies=@('supermedo-runtime-c-v1','supermedo-runtime-c-atomic-v2','supermedo-structural-v1','awaken-0.9.33-lto-srw-v1','awaken-structural-v1'); CriticalFunctionShapes=@(100,108); InputAbi='orbis-pad-120-v1'; LegacyVerifiedHostSha256='4AE2D15EAFCE351C61DC10B8498B2BAD61CDC3A5C15C92E274FD63A7A4FC312A';
    Ports=@('supermedo','awaken'); AwakenEngine='0.9.33 reviewed; later builds via structural check (552-byte file records supported)';
    InitialActivation='Manager open before game startup'; CoreTests='all passed (Build-Release.ps1 stops on any failure)'; AwakenNativeTests=3; Updater='GitHub releases, SHA-256 asset digest required';
    ManualAcceptance='User confirmed Supermedo 1.8.2 and Awaken 0.9.33 live checkpoints. Isolated Awaken F5/F9/F11 accepted on 2026-10-16; two restores recorded. 0.9.3 fixes the Awaken engine FrameHook conflict; user confirmed isolated F5/F9/F11 on 2026-10-17 with Awaken native menus active. 0.9.4 displays the manager version in the header. Native adapters unchanged since 0.9.3. See docs/AWAKEN.md.';
    GameAssetsIncluded=$false; SavesIncluded=$false
}
$metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'release.json') -Encoding UTF8
$zip=Join-Path $release ("BloodborneCheckpointManager-v"+$Version+"-windows.zip")
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip -Force
# Archive the explicit source list directly: no duplicate extracted project or staging directory.
Push-Location $project
try {
    # Tracked plus new (not ignored) files from git; ripgrep or a plain listing when git is unavailable.
    $roots=@('src','tests','tools','native','docs')
    if ((Get-Command git -ErrorAction SilentlyContinue) -and (Test-Path -LiteralPath (Join-Path $project '.git'))) {
        $files=@(& git -c core.quotepath=off ls-files --cached --others --exclude-standard -- @roots)
        if ($LASTEXITCODE) { throw 'Could not enumerate source files.' }
    } elseif (Get-Command rg -ErrorAction SilentlyContinue) {
        $files=@(& rg --files --no-require-git @roots -g '!**/bin/**' -g '!**/obj/**' -g '!**/.git/**')
        if ($LASTEXITCODE) { throw 'Could not enumerate source files.' }
    } else {
        $files=@(Get-ChildItem -LiteralPath $roots -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.git)[\\/]' } |
            ForEach-Object { (Get-RelativePath $project $_.FullName) })
    }
    $files=@($files | Where-Object { $_ -and (Test-Path -LiteralPath (Join-Path $project $_) -PathType Leaf) } | Sort-Object -Unique)
    $files+=@('README.md','Directory.Build.props','BloodborneCheckpointManager.sln','.gitignore')
    $entries=@($files | ForEach-Object { [pscustomobject]@{ Source=(Join-Path $project $_); Entry=$_.Replace('\','/') } })
    $entries+=@(Get-ChildItem -LiteralPath $licenses -File -Recurse | ForEach-Object {
        [pscustomobject]@{ Source=$_.FullName; Entry=('Licenses/'+(Get-RelativePath $licenses $_.FullName).Replace('\','/')) }
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
