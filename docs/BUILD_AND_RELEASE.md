# Build and release

## Requirements (once)

- **.NET 8 SDK x64**: <https://dotnet.microsoft.com/download/dotnet/8.0>. Check it with `dotnet --version`, which should print 8.x.
- **MSYS2 with the CLANG64 toolchain**, needed for the native bridge DLLs:
  1. `.\tools\Install-BuildTools.ps1` downloads and verifies MSYS2 into `C:\bcm-build\msys64`.
  2. Open `C:\bcm-build\msys64\clang64.exe` and run `bash tools/install-native-packages.sh` from the repository folder. At minimum you need `mingw-w64-clang-x86_64-toolchain`, `-cmake`, `-ninja` and `-nlohmann-json`.
- **ripgrep** (`rg`), used by the packaging script to list source files: `winget install BurntSushi.ripgrep.MSVC`.
- **GitHub CLI** (`gh`), optional, for publishing: `winget install GitHub.cli`, then `gh auth login`.

## Build a release

1. Set the version in `src/Checkpoint.App/Checkpoint.App.csproj` (`<Version>0.9.5-experimental</Version>`). The updater compares this number with the GitHub tags.
2. From the repository root, in PowerShell:

   ```powershell
   .\tools\Build-Release.ps1
   ```

   The script stops at the first failure. It does the following:
   - Builds `bcm-awaken.dll` with CLANG64, runs its three native tests, and regenerates `AwakenBridgeIdentity.g.cs` from the new DLL hash. Use `-SkipAwaken` when `native/awaken` did not change.
   - Reuses the Supermedo bridge from `artifacts/external-native-build/bcm-external.dll`, which is unchanged.
   - Builds and runs the core tests.
   - Publishes the manager to `artifacts/releases/<version>/windows`.
   - Creates `BloodborneCheckpointManager-v<version>-windows.zip` and `-source.zip`, and prints their SHA-256.

   If MSYS2 lives elsewhere, pass `-Clang64Bin 'D:\msys64\clang64\bin'`.

3. Try the published `artifacts/releases/<version>/windows/BloodborneCheckpointManager.exe` (F5/F9 with a test save) before publishing.

## Publish (this is what the auto-updater reads)

Create a **published** (non-draft) GitHub release:

- **Tag:** `v<version>`, for example `v0.9.5-experimental`.
- **Assets:** both ZIPs, with the names unchanged.

```powershell
gh release create v0.9.5-experimental `
  artifacts\releases\0.9.5-experimental\BloodborneCheckpointManager-v0.9.5-experimental-windows.zip `
  artifacts\releases\0.9.5-experimental\BloodborneCheckpointManager-v0.9.5-experimental-source.zip `
  --title "BB Checkpoint Manager 0.9.5" --notes-file artifacts\releases\0.9.5-experimental\release-notes.md
```

Uploading through the GitHub web page works too.

## How the updater works

- **Checking:** at startup, and with **Settings → Check for updates**, the manager reads `api.github.com/repos/santiagovalencia109/BloodborneCheckpointManager/releases`. It picks the newest non-draft release whose tag is higher than its own version and that has the asset `BloodborneCheckpointManager-v<version>-windows.zip`. When one exists, the header shows “Update vX available”.
- **Installing:**
  - Installing is refused while `bb-probe.exe` is running, because the game keeps the bridge DLL loaded.
  - The ZIP is downloaded only from this repository's release URLs. Its size and its SHA-256 must match the `digest` that GitHub publishes for every uploaded asset. A release without a digest is not installed automatically; the release page opens instead.
  - The package is extracted with path-traversal checks and must contain the manager and both bridge DLLs.
  - `apply-update.ps1` (in `%LOCALAPPDATA%\BloodborneCheckpointManager\updates`) waits for the manager to exit, backs up the current folder to `updates\previous`, and copies the new files. If the copy fails it restores the backup, and either way it reopens the manager. The result is logged in `manager.log`.
- **Data:** checkpoints, settings and logs live in `%LOCALAPPDATA%\BloodborneCheckpointManager` and are never touched.
- **Locked folders:** if the manager's folder is not writable (for example under `Program Files`), the manager opens the release page instead. Extract the ZIP over the folder manually.
- **First update:** versions before 0.9.5 have no updater. Install 0.9.5 manually once.
