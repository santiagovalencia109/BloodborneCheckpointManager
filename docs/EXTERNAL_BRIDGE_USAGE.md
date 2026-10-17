# External Supermedo bridge — 0.7 experimental

The manager can attach its own native bridge to the original Supermedo runtime. Keep using your normal launcher and its graphics, mods and configuration UI. The manager does not replace the game executable on disk or generate a different linked game image. Attachment adds code and hooks to the running process in memory.

Runtime support now uses a relocation-independent contract of critical functions and symbols, plus a legacy exact-hash profile. The Bloodborne 1.09 guest instructions are still verified. See ADAPTIVE_DETECTION.md for the coverage and update limits. It is not universal support for arbitrary ports.

## Setup

1. Close the game. Extract the complete manager package to its own folder; keep `Native/bcm-external.dll` beside it. Install the .NET 8 Desktop Runtime x64 if needed.
2. In **Settings**, choose the native game executable and the complete save directory used by that installation. Choose a separate checkpoint library. The tested installation uses `bin/bb-probe.exe`; the frozen launcher is a separate process. Paths are confirmed against the running runtime, and mismatched saves are rejected.
3. Choose **Supermedo Live Mode** under **Checkpoint mode** and save settings. Use **Closed Game Mode** for checkpoints with the game closed. Advanced controls are hidden; previously saved options are preserved.
4. Leave the manager open, then start Bloodborne from your usual launcher or shortcut. Wait for **Supermedo Live Mode ready**. No special environment flag is required.
5. In a loaded single-player game, press **F5** to request a real engine save and create a checkpoint. Press **F9** to restore the latest manual checkpoint. Wait for **Checkpoint restored**, then choose Continue in the game. Restoring through the library or the **F11 floating picker** uses the selected checkpoint instead.

F9 is automatic once configured; it does not add a Yes/No dialog. Checkpoint deletion still asks for confirmation because it permanently deletes a library copy.

The legacy originally tested runtime is (not a required identity for the adaptive family):

```text
SHA-256: 4AE2D15EAFCE351C61DC10B8498B2BAD61CDC3A5C15C92E274FD63A7A4FC312A
File length: 15,272,960 bytes
Adapter: supermedo-4ae2d15e-experimental-v1
```

Detection validates runtime symbols, critical code, references and export tables rather than only its filename. Normal `bb-probe`/`bbport` names and the configured executable name are checked frequently; other names are also discovered periodically. For a renamed runtime, configure its actual executable before starting it so the fast startup watcher can find it.

## Current limits

- **Open the manager before starting the game.** This first adapter establishes filesystem coverage before guest execution. Connecting to a game that has already started provides observation only. Restart the game with the manager open to activate live checkpoints. Reopening the manager works if its matching bridge was already connected in that process, with the same save root. Attaching safely to an existing in-world session that has no bridge remains future work.
- A fast start can miss the activation boundary. The manager will report observation mode and refuse live operations. It does not enable restoration based on a quiet filesystem interval or a guessed title state.
- Supported Windows x64 builds only. Updates retaining the validated runtime contract may pass automatically; changed critical instructions, removed symbols, multiplayer/party, and arbitrary injected mods are rejected or not verified. Graphics options remain managed by the original launcher, but combinations beyond the tested fixture have not all been exercised.
- Checkpoints contain persistent files, not a RAM snapshot. The engine chooses what position and world state its saves retain. Frame-exact position, RNG and enemy state are not promised.
- Keep the DLL and manager from the same build together. An incompatible bridge already loaded in the game requires a game restart. A timed-out attachment is not forcibly killed or retried automatically on that instance.
- All savedata access is gated during the lease. The game can briefly wait while copying/restoring. This uses an I/O barrier, not suspension of gameplay threads for the entire copy. Thread suspension is used briefly for verified hook installation.
- After a disconnect during restoration **following the mutation marker**, the bridge retains its file/input barrier. Close the game and use the manager's recovery workflow. Never resume access to a partially restored directory.
- Named process blocking and configured aliases still matter for other installations/unsupported runtimes. Automatic attachment does not establish universal discovery of every possible game process.

## Build

Build the manager normally for offline operation. For the external bridge, use MSYS2 CLANG64 with Clang, CMake, Ninja and nlohmann-json installed, plus the .NET SDK. The vendored MinHook source is included. The Windows compiler must support `__attribute__((sysv_abi))`; MSVC alone is not sufficient for this adapter.

From the project directory:

```powershell
dotnet restore src/Checkpoint.App/Checkpoint.App.csproj -r win-x64
dotnet restore tests/Checkpoint.LiveProbe/Checkpoint.LiveProbe.csproj
./tools/build-external-bridge.ps1 -Clang64Bin 'YOUR_CLANG64_BIN_DIRECTORY'
./tools/build-awaken-bridge.ps1 -Clang64Bin 'YOUR_CLANG64_BIN_DIRECTORY'
dotnet publish src/Checkpoint.App/Checkpoint.App.csproj -c Release -r win-x64 --self-contained false -o artifacts/releases/0.9.4-experimental/windows --no-restore
```

The native build script runs the callback ABI test, generates `ExternalBridgeIdentity.g.cs` from the actual DLL hash, builds the manager and live probe, and copies the DLL to their `Native` folders. Rebuilding a DLL independently requires updating that generated identity and rebuilding the .NET components. The complete source package includes the corresponding native source and third-party notices.

For the DLL by itself, run CMake against `native/external` with the CLANG64 toolchain, then build and run CTest. See its `CMakeLists.txt`. No original game assets are needed to compile it.

## Validation

On 2026-10-12 the original Supermedo executable ran with an isolated save root. The manager's discovery and attachment succeeded without replacing that executable. A checkpoint with **2 vials** was created; one vial was used; restoration returned to the title; Continue loaded **2 vials** again. This was repeated on a later native revision. Holding Cross during restoration left the game at the title until the button was released and pressed again.

The final UI watcher is tested separately from the earlier command-line watcher. Its connection events are written to `manager.log`. Manual F5/F9 acceptance in the final manager is tracked in [EXTERNAL_BRIDGE_EXPERIMENT.md](EXTERNAL_BRIDGE_EXPERIMENT.md).

These checks establish a working experimental save/restore path on this build. They do not establish exhaustive native crash recovery, long-session stability, restored bullets/echo differences, exact position fidelity, or compatibility with every launcher option/mod. The core lease/journal failure paths remain covered by the automated suite.
