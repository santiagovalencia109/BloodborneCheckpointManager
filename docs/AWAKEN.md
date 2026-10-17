# Awaken / BloodBorneButPc support — 0.9 experimental

Choose **Game port → Awaken · BloodBorneButPc** before configuring paths. Use **Awaken Live Mode** for live checkpoints, or **Closed Game Mode** with the game closed. Start the manager before launching the game normally.

The reviewed package is Awaken **0.9.33**, engine **bbhost-0.4.0-fps66**. Its native process is `engine/out/bb-probe.exe`; `Bloodborne Launcher.exe` and `Bloodborne.exe` are launchers, not the native engine. Select the complete `savedata` folder actually used by the launcher's current player profile. Its default is under the package's `user`, but the launcher supports custom user directories and player profiles. Do not assume the default when a custom profile is in use. The live connection checks the configured save root against the running engine.

## Separate adapters and profiles

Existing `settings.json` files default to Supermedo. Their paths are preserved. Each port has independent paths and options in `port-profiles.json`; `settings.json` retains the active profile for compatibility with existing tooling. Switching ports clears the visible list and restores that port's settings. The default new library is `Checkpoints/<port>` under the manager's data directory. Choose separate checkpoint libraries.

Supermedo retains its adaptive contracts and the same `Native/bcm-external.dll` (SHA-256 `F4081491C7F1B67EEB262D8E1427C1B5FFC7A0A8E10B181D4A561F2FA7AB902C`). Its native source is unchanged. Awaken uses a separate resolver, code and hash-verified `Native/bcm-awaken.dll`. Discovery caches include the selected port; there is no fallback from one adapter to another. Process blocking remains conservative when another engine is running.

## Reviewed engine

```text
Package: 0.9.33 / bbhost-0.4.0-fps66
Native SHA-256: A41056275EE9D01C7DF0CA4FA6606628CD04DC3C84F00A0EC5BD41908B2DB6D7
Length: 41,331,200 bytes
File table: 1024 records, 536-byte stride, path at offset 24
File lock: SRWLOCK (not Supermedo's pthread mutex)
Exports: file 40, savedata 14, pad 11
Loaded-code checks: 64, with PE base relocations masked
```

Source review confirms the layout in DarkIzuku's runtime_file.c plus Awaken's patch. LTO removes six host helpers; only the three remaining host ABI helpers are hooked, together with all guest file/save exports. A SysV guest export must never be substituted for a removed Windows ABI host helper.

The engine reserves nearby guest address space, preventing MinHook from allocating its short-range tick relay. Awaken instead installs a 14-byte absolute jump across an exactly verified 17-byte complete-instruction prologue. The trampoline contains no relative operands and may be allocated anywhere. This happens only inside the reviewed main-thread startup boundary, before linked module initialization and guest entry. Changed bytes are rejected. The separate native tests verify register preservation and original/detour execution. Mid-session first attachment remains unsupported.

**Awaken compatibility currently pins this exact engine build.** Unlike Supermedo, it does not yet have an adaptive LTO contract. A future Awaken update may require review. Do not advertise automatic support for all future releases. Multiplayer and arbitrary injected mods remain unsupported.

## Build

After restoring the .NET projects, with the same MSYS2 CLANG64 dependencies as the Supermedo adapter:

```powershell
./tools/build-awaken-bridge.ps1 -Clang64Bin 'YOUR_CLANG64_BIN_DIRECTORY'
dotnet publish src/Checkpoint.App/Checkpoint.App.csproj -c Release -r win-x64 --self-contained false -o artifacts/releases/0.9.4-experimental/windows
```

Build or supply both corresponding DLLs when packaging the combined app. `tools/inspect-awaken-runtime.py` is a maintainer-only layout/code-check generator, not an automatic compatibility approval tool. A new host hash requires source/ABI review and isolated live tests. No Python or compiler is required by users of the packaged app.

## Validation

70 core tests passed, including legacy migration, independent profile persistence, invalid port rejection and adapter selection. Supermedo's installed runtime still passes its positive and critical-opcode rejection checks. The original native DLL remains byte-identical. All three Awaken native tests pass.

An isolated Awaken instance connected with verified guest instructions, capture/restore enabled and an input barrier. Its save root is under `artifacts/awaken-fixture-01/user/savedata`. The test uses existing prepared guest resources read-only from the Supermedo installation; it does not replace Awaken's executable. Initial testing exposed a Vulkan device-lost error; the test was retried with conservative GPU options. This is not yet a claim that every normal launcher setting is validated.

On 2026-10-16 the user confirmed F5/F9/F11 worked in the isolated Awaken instance. The manager log records two creations and successful restores at 14:51:31 and 14:52:27 (America/Bogota). Original save files were copied for the test; no restoration targeted the original installation.

Version 0.9.1 fixes the port dropdown flicker: background discovery no longer toggles the control's enabled state. Opening the dropdown postpones discovery/attachment; stale discovery results are discarded if settings changed. Port changes are blocked only during an actual connection, not every timer tick. Native adapters are unchanged.

Version 0.9.2 makes Browse open the path currently shown in its field. The executable is preselected when it exists. Missing paths fall back to their nearest existing parent, and empty/invalid fields open Documents (or the app folder if Documents is unavailable).

## Startup connection fix (0.9.3)

A normal Awaken launcher session can install its own FrameHook at the same guest entry used by the manager. The previous adapter rejected it at the startup boundary but kept reporting startup, leaving the UI waiting indefinitely. The adapter now accepts only the reviewed 0.9.33 engine stub: all 219 bytes are compared, with the exact engine callback and original resume address. It chains through that stub so the port's menus, controls and other frame work still execute. Unknown jumps remain rejected. Attachment still happens before guest execution; this does not enable attaching for the first time during a running session.

Permanent startup failures now leave the waiting state and report the actual reason. Changed reasons are logged even when the visible startup message stays the same. Hook activation has three bounded retries for transient startup thread-freeze failures, always subject to the existing pre-guest checks. The Supermedo adapter and its DLL are unchanged.

Native tests cover original entry, chained engine execution, register preservation and rejection of every single-byte alteration in the engine stub. On 2026-10-17 the isolated instance used Awaken�s own prepared boot/content/patch resources, its native PC menu assets, graphics controls and widescreen settings. It connected successfully; the log records readiness at 11:45:00 and restoration at 11:45:35 (America/Bogota). The user confirmed F5/F9/F11 work. All 70 core tests and three native tests pass. Test saves remained separate from the original installation. This validates the reproduced conflict, not every possible launcher setting or future engine update.
