# Structural fallback for port updates

**Since 0.9.5.** Port updates no longer need a new manager release when they only change private implementation code.

## Why updates used to break the manager

The reviewed Supermedo contracts pin the masked SHA-256 of every function in the file/save helper graph (100 or 108 functions). They also pin every call target by `name#index`. Any change upstream makes the contract fail, including changes that do not affect the bridge:

- A new call inside `do_open`. For example, the Supermedo build `40FD5B03…` added `runtime_quit_on_open`, which moved the rest of `do_open` by 16 bytes.
- A function rewritten by the compiler. For example, `runtime_guest_errno` changed from a switch to a lookup table.
- One more static `lock` global in another translation unit. This renumbers `lock#4` to `lock#5`, so seven functions with identical bytes failed.

The rejected build still had the same file table, mutex, 560-byte record, helpers and export tables that the bridge uses.

## What the fallback checks

When no reviewed contract or exact build matches, `StructuralRuntime` reads the runtime's COFF symbols and checks the following:

| Bridge input | How it is established |
| --- | --- |
| `files` | Single named writable global, large enough for 1024 records. |
| Record stride / path offset | `IMUL r64, r, imm32` in the code that indexes `files`. Only reviewed pairs that the port's DLL also accepts are allowed: Supermedo 0x218/24 and 0x230/32, Awaken 0x218/24 and 0x228/36. Exactly one must appear. |
| File mutex | `LEA RCX,[lock]` immediately followed by a call to `pthread_mutex_lock` (Supermedo) or `AcquireSRWLockExclusive` (Awaken) in that code. The file mutex is the lock taken by every such function. For example, a prefetch lock taken only in `do_open` drops out. It must be a single named writable global. |
| `image`, `gpu_enabled`, `entered_game`, `user_root` | Named writable globals. A repeated name, such as `image`, is resolved by the code that also uses an anchor (`gpu_enabled`/`entered_game`, or `files`). |
| Helpers and `runtime_thread_attach_main` | Unique executable function symbols. Awaken uses only open/close/read, like the reviewed LTO ABI. |
| Export tables | `exports` tables that start with `sceKernelOpen`, `sceSaveDataInitialize` and `scePadInit`. Every name hooked by the native bridges must be present, and every target must be an executable function. |
| Loaded code | Every function that will be hooked is hashed from the disk image, with DIR64 relocations masked, and compared with the running process before injection. This is the same check as before. |

Names with an LTO `.llvm.<hash>` suffix are accepted. If anything is missing, ambiguous or of an unreviewed layout, the runtime is rejected, and the error lists both the reviewed-contract failure and the structural failure.

The result is marked `Reviewed=false`. Settings show a notice, and the live status line appends “unreviewed port version”. Emergency backups, restore journals, the save-root check, I/O barriers and handle counting are unchanged.

## Awaken

The Awaken build after 0.9.33 changed two things the bridge depends on:

- **File records:** 552-byte records (0x228). A request timestamp is at 0x18 and a flag at 0x20, so the path moved to offset 36 (`do_open`: `snprintf(record+0x24, 0x200, …)`). Both the resolver and `bcm-awaken.dll` accept 0x228/36.
- **Frame hook:** Awaken's frame hook was rewritten. `FrameHook`/`EmitPrologueStub` (callback at RVA 0x119f80) became `WriteStub` with an `OnFrame` callback, with a different stub header. `bcm-awaken.dll` used to compare the old 219-byte stub exactly. `engine_frame.h` now accepts the chain by its structure:
  - the entry is `jmp [rip+0]; dq stub; int3×3`;
  - the stub is private read/execute memory;
  - it starts by saving the argument registers;
  - it contains exactly one `movabs rax, callback; call rax`, where the callback is executable code inside the engine image;
  - it ends with the exact 17 displaced guest prologue bytes followed by `jmp [rip+0]; dq target+17`.

  The native test runs the chain through both the 0.9.33 header and the new header, and rejects mutations of the tail, saved registers, callback and entry.

## What this does not cover

The fallback trusts that the port's save transaction behaves like the reviewed one. If an update changes when or how saves are written, without changing the structures above, this check does not detect it. Run an isolated F5/F9 test after major port updates. If a structural build proves safe, add a reviewed contract with `tools/generate-runtime-contract.py` to return it to the verified tier.

## Validation

- 68 core tests pass (Linux run; Windows-only pipe tests not included), including five structural tests on a synthetic runtime: acceptance, implementation-only changes, repeated globals, unknown stride, two locks, missing export or helper, read-only table, stripped symbols, the Awaken SRW/LTO variant, Awaken 0x228/36 records and cross-port rejection.
- Real Supermedo build `40FD5B03A6A65E88F40417A85D66E9B4B2823523BADC5C274E559CCF5997509D` (rejected by both reviewed contracts) resolves in about 0.6 s. Its lock is 0xe8e030 (9 call sites), its files are at 0xedad50, its stride is 0x230 (13 sites), and its tables have 40/14/11 entries. These values match an independent symbol dump.
- Mutations of that build are rejected: stride changed to 0x240, one lock call pointed at another lock, and the `sceKernelUnlink` export renamed.
- Awaken 0.9.33 (`A4105627…`), forced through the structural path instead of its exact-hash contract, gives the same 26 layout values as the reviewed contract. Its lock comes from the code (3 `AcquireSRWLockExclusive` call sites, not the name fallback), and all 54 loaded-code hashes match the reviewed ones. The build after 0.9.33 (`EBB84FB2…`) is accepted through the structural path: record 0x228/36, lock 0x3a217f0 (taken in `do_open`, `do_close` and `do_getdents`; the prefetch `pf_lock` is excluded), and 40/14/11 export tables.
- Live acceptance on 2026-10-23, manager 0.9.5 (manager.log). Supermedo `40FD5B03…` connected in structural mode, and one checkpoint was created and restored. Awaken `EBB84FB2…` connected with capture and input barrier active, and two restores succeeded. The user reported normal behaviour.
