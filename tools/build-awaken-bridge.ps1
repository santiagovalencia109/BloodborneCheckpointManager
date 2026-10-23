param([Parameter(Mandatory=$true)][string]$Clang64Bin)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$Clang64Bin=[IO.Path]::GetFullPath($Clang64Bin); $env:PATH=$Clang64Bin+';'+$env:PATH
$build=Join-Path $project 'artifacts\awaken-native-build'
# A cache configured from another checkout path (another PC or a moved folder) cannot be reused by CMake.
$cache=Join-Path $build 'CMakeCache.txt'
if(Test-Path -LiteralPath $cache) {
    $configured=(Select-String -LiteralPath $cache -Pattern '^CMAKE_HOME_DIRECTORY:INTERNAL=(.*)$' | Select-Object -First 1).Matches.Groups[1].Value
    $source=[IO.Path]::GetFullPath((Join-Path $project 'native\awaken')).TrimEnd('\')
    if(-not $configured -or [IO.Path]::GetFullPath($configured).TrimEnd('\') -ne $source) {
        # Generated build folder only (CMake/Ninja state and outputs); it is recreated below.
        Write-Output "Removing stale build folder configured for $configured"
        Remove-Item -LiteralPath $build -Recurse -Force
    }
}
& (Join-Path $Clang64Bin 'cmake.exe') -S (Join-Path $project 'native\awaken') -B $build -G Ninja -DCMAKE_BUILD_TYPE=Release -DCMAKE_C_COMPILER=clang -DCMAKE_CXX_COMPILER=clang++ -DCMAKE_ASM_COMPILER=clang
if($LASTEXITCODE) { throw 'Awaken configuration failed' }
& (Join-Path $Clang64Bin 'cmake.exe') --build $build
if($LASTEXITCODE) { throw 'Awaken build failed' }
& (Join-Path $Clang64Bin 'ctest.exe') --test-dir $build --output-on-failure
if($LASTEXITCODE) { throw 'Awaken ABI verification failed' }
$dll=Join-Path $build 'bcm-awaken.dll'; $hash=(Get-FileHash -LiteralPath $dll).Hash
$identity='namespace Checkpoint.Core;'+[Environment]::NewLine+'internal static class AwakenBridgeIdentity { internal const string Sha256="'+$hash+'"; }'+[Environment]::NewLine
[IO.File]::WriteAllText((Join-Path $project 'src\Checkpoint.Core\AwakenBridgeIdentity.g.cs'),$identity)
Write-Output ('Built Awaken bridge '+$hash+'. Supermedo DLL unchanged.')
