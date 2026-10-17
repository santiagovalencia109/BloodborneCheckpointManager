# Project layout

`BloodborneCheckpointManager/` is the working source project. Open `BloodborneCheckpointManager.sln` to edit or build it.

```text
BloodborneCheckpointManager/
  BloodborneCheckpointManager.sln
  src/
    Checkpoint.App/           Windows app, UI and keyboard shortcuts
    Checkpoint.Core/          Saves, checkpoints and runtime detection
  native/external/           Native game bridge and vendored MinHook
  tests/                     Core, UI and game-bridge checks
  tools/                     Build, packaging and isolated test helpers
  docs/                      Guides and development notes
  artifacts/                 Generated files; not the working source
```

## Generated files

```text
artifacts/
  releases/0.9.4-experimental/
    windows/                 Ready-to-run app
    ...-windows.zip          App download
    ...-source.zip           Source project download
  archive/
    releases/                Earlier builds and packages
    extracted-packages/      Previously unpacked downloads
    source-snapshots/        Previous extracted source copies
    development-files/       Old diagnostic and scratch files
    unversioned/             Early outputs without a version
  previews/                  UI screenshots and preview outputs
  external-native-build/     Active CMake native build cache
  external-fixture-01/       Supermedo test saves and checkpoints
  live-fixture-01/           Earlier integrated-port test data
```

The two test-data folders keep their original locations because checkpoint manifests and recovery records contain absolute paths. The native build cache also retains its path because CMake records it. These folders are development data and are not included in release ZIPs. The files synced under the workspace's `sources/` folder are reference material, not this project.

`Abrir-manager-prueba.cmd` opens the current app using the isolated Supermedo test profile. It does not launch the game. For normal play, open the app in `releases/0.9.4-experimental/windows/` without that test shortcut.

## Build and package

See [native build instructions](EXTERNAL_BRIDGE_USAGE.md#build) for the required toolchain. Once the matching native bridge is built, publish from the project directory:

```powershell
dotnet publish src/Checkpoint.App/Checkpoint.App.csproj -c Release -r win-x64 --self-contained false -o artifacts/releases/0.9.4-experimental/windows --no-restore
./tools/package-external-release.ps1 -Version 0.9.4-experimental -Clang64Root 'YOUR_CLANG64_DIRECTORY'
```

Packaging writes both ZIPs alongside the published app. The source ZIP contains the solution, source, tests, tools, documentation and license notices. It excludes compiled `bin/obj` folders, `artifacts`, saves and game assets; no duplicate extracted source folder is created.

`tools/organize-artifacts.ps1 -WhatIf` previews how legacy outputs would be filed. Running it without `-WhatIf` moves those outputs after checking paths and conflicts. It does not delete files, close processes or move test save folders. The organization log is stored in `artifacts/archive/`.
