# External bridge feasibility

**Implementation update, 0.5 experimental:** the separate DLL, fingerprinted Supermedo adapter and automatic manager watcher are now implemented. Live capture, return to title, restore and recovery of 2 vials after spending one passed isolated tests with the original executable. Initial activation requires the manager to attach during startup; already-running sessions remain observation only. See [usage and limits](EXTERNAL_BRIDGE_USAGE.md) and [experiment log](EXTERNAL_BRIDGE_EXPERIMENT.md). The original analysis below is retained as background.

Analysis date: 2026-10-12. This document proposes a new integration; it does not mean that the existing manager supports live checkpoints on the original Supermedo executable. No DLL was attached to the game during this analysis and no original saves were modified.

## Conclusion

Launcher-independent operation is feasible. A native bridge loaded into a supported game process is a promising way to keep the original port executable and its launcher, graphics options, and updates. Universal compatibility with arbitrary executables or future builds cannot be promised. Process discovery and reading game memory alone do not establish a safe save/restore boundary.

The manager remains responsible for checkpoints, selection, names, backups, verification, and recovery. The bridge is responsible for operations that require cooperation from the running game: save requests on the game thread, confirmed return to title, input blocking, and complete savedata access control. These responsibilities already exist in the integrated visy bridge; the new work is making them available through a separate native adapter.

## Evidence inspected locally

- Installed launcher script: `E:\Games\Bloodborne-Windows\bbport-windows\run.py`. It selects `BB_PROBE` or `bb-probe.exe`, generates linked game images and patches, selects the user directory, and supplies `--user` to the native process. The native process must be identified separately from the Python/frozen launcher. The save path must be confirmed from the running runtime, rather than inferred from its executable directory.
- Installed native executable: `E:\Games\Bloodborne-Windows\bbport-windows\bin\bb-probe.exe`. SHA-256: `4AE2D15EAFCE351C61DC10B8498B2BAD61CDC3A5C15C92E274FD63A7A4FC312A`.
- LLVM PE inspection reports an empty export directory. This executable provides no PE-exported checkpoint API for ordinary exported-function lookup.
- LLVM COFF symbol inspection retains names including `runtime_file_user_dir`, `runtime_file_open`, `runtime_file_write`, `runtime_file_close`, `runtime_file_resolve`, `runtime_savedata_resolve`, `runtime_pad_resolve`, and static savedata handlers. This gives concrete starting points for a build-specific adapter. Symbols are not a supported API and do not establish coverage of every save operation.
- The current `LiveBridgeClient.cs` connects only to the configured exact executable path and to a pipe served by that PID. It checks source directory, session, lease, restore capability, and input barrier. Discovery would need extending without weakening these checks.
- The integrated `native/visy/checkpoint_bridge.cpp` schedules engine requests on the game thread and checks game code bytes. Its savedata and input barriers depend on additional runtime changes. Packaging that file as a DLL alone would not recreate those changes in Supermedo.
- The successful live restore tests apply to our prepared visy build. They establish reusable behavior and acceptance criteria, not compatibility with the installed Supermedo binary. Local source checkouts have pinned commits; no exact source-to-installed-binary match was established in this analysis.

## Proposed architecture

1. **Process discovery in the .NET manager.** Observe running processes independently of how they were launched. Identify the actual runtime by executable identity and validated build information, with configurable explicit selection. Handle multiple instances, exits, and launcher-driven restarts. Do not trust only a filename such as `bbport.exe`.
2. **Native bridge DLL owned by the manager.** Load a bridge into a supported process, preferably through a port-provided plugin entry point if one becomes available. A build-specific attach/hook mechanism is the alternative under investigation. It preserves the original executable on disk, while necessarily changing the running process in memory.
3. **Versioned adapters.** Each adapter verifies the host build, the loaded game image, relevant instructions, calling conventions, and required save/input paths before enabling capabilities. Native host addresses must account for ASLR. Guest-game addresses must use the actual loaded guest base. No offsets from the visy build should be applied blindly to Supermedo.
4. **Existing checkpoint protocol.** Reuse the authenticated local pipe, PID ownership check, canonical source path, session IDs, and transaction leases. Add adapter/build identity and explicit capabilities so the manager can explain whether live capture and live restore are supported.

Windows supports DLL code mapped into a process address space; this is the execution model for the proposed native component, not evidence that this game has an attachment API. See [Microsoft: Run-Time Dynamic Linking](https://learn.microsoft.com/en-us/windows/win32/dlls/run-time-dynamic-linking).

## Required behavior

**F5:** enqueue the engine's save request on its verified main thread; establish completion of all relevant save operations; acquire a barrier preventing further changes while the manager copies and verifies the complete save tree; release the barrier. A quiet filesystem interval alone is insufficient proof of completion.

**F9:** block controls; request return to title on the game thread; confirm world unload and a safe title state; account for open handles, outstanding operations, metadata and memory-backed savedata; acquire the restore barrier; create an emergency backup and restore the selected checkpoint using the existing journal and verification; release only when the complete destination is verified. Continue must then read the restored data. Held buttons must not accidentally activate Continue early.

If an attachment cannot account for a handle opened before it attached, it must withhold the live operation until a verified safe boundary is reached. Installing hooks into running code also requires a safe installation strategy; simply finding a symbol is insufficient.

Unsupported builds retain closed-game checkpoint operations. An interrupted restore after filesystem mutation must retain the protective barrier and require recovery, as in the existing implementation. Initial support is single-player only.

## Recommended first implementation and acceptance gates

1. Target the exact installed Supermedo binary fingerprint above. In an isolated user/save directory, prove discovery, read-only telemetry, native/guest build checks, actual save-root reporting, and a pipe handshake. Verify attachment lifetime and safe installation before enabling commands.
2. Implement live capture independently. Validate both the main-thread request and full save-I/O coverage, including operations and handles that predate attachment. Confirm a checkpoint reflects current inventory and position.
3. Add return-to-title and input barriers. Prove that input remains blocked until the restore has completed and that savedata access is quiescent. Only then enable filesystem restoration.
4. Test a checkpoint A, spending a vial and gaining/changing echoes at B, F9 to A, Continue, and a subsequent normal autosave. Confirm both the loaded inventory/position and the resulting save on disk. Repeat cycles and test disconnects, process restarts, early Continue input, and unsupported builds.
5. Add other known builds through adapters. Investigate signature-based matching to reduce maintenance, while still validating each enabled capability. A future stable upstream plugin/API would reduce per-build maintenance further.

The first proof should preserve Supermedo's own launcher and runtime. Replacing its runtime with our visy executable would reintroduce linked-image and configuration compatibility issues and would not prove the proposed architecture.
