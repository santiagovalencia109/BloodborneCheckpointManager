# Adaptive runtime detection — 0.7 experimental

**0.9.5:** when no reviewed contract matches, the manager falls back to a [structural check](STRUCTURAL_FALLBACK.md) of only what the bridge uses.

**Current behavior (0.8.4):** file/save code retains strict implementation checks (100 or 108 functions). Input is checked through the public Orbis ABI and executable export targets, so private keyboard/parser changes no longer require matching bytecode. See [0.8.4 details and validation](UPDATE_0_8_4.md). The 0.7 counts and pad helper checks below describe the earlier implementation.

This replaces the whole-executable identity requirement for the Supermedo C runtime family. The original exact-hash 4AE2D15E build retains its verified legacy profile. The installed 000C7C9B build is resolved through the adaptive contract; there is no allowlist entry for its whole-file hash. File hashes remain integrity/identity checks during attachment, not compatibility decisions.

## What can adapt

The manager parses bounded Windows x64 PE/COFF symbol tables, resolves runtime state and helper functions by name, and checks 132 critical function shapes. RIP-relative address operands and external call/jump displacements are normalized; named reference destinations are checked against the resolved symbols. Full function hashes otherwise must match. Three RuntimeExport tables must retain the expected names and function targets; the complete file/savedata/pad public helper surface is checked for additions or removals. The file mutex is identified through runtime_file_mount's reference, rather than a guessed global location.

Graphics/launcher changes outside that contract, moved sections/globals/functions, renamed executables, changed PE identity and unrelated file changes can pass without recompiling the manager or adding a new whole-file fingerprint. This is conditional compatibility, not arbitrary semantic adaptation. Changes to critical instructions, compilation that changes these code shapes, symbol stripping, incompatible helper/export surfaces, different file structure or game instructions fail closed. A compiler/runtime ABI change requires reviewing and regenerating a contract.

At first attachment, the manager locks the runtime file against replacement, revalidates the contract and identity, and reads critical code from the loaded process before injecting its own hash-verified DLL. The DLL compares loaded/disk PE identity and the supplied hash, checks layout bounds, and uses the resolved RVAs. ASLR uses the actual loaded base. Active bridge reconnection keeps its existing hooks instead of checking their patched prologues against pristine bytes. The manager and DLL must be upgraded together, with the game restarted if another bridge revision is already loaded.

Guest engine SaveRequest/tick/state checks remain verified against Bloodborne 1.09. Startup-only hook installation, actual runtime save-root validation, I/O and input barriers, leases, emergency backup and restoration journals remain required. Adaptive detection does not enable first attachment to a game that already entered guest execution. It also does not establish compatibility with other ports or arbitrary gameplay mods.

## Build and maintenance

The embedded contract is src/Checkpoint.Core/Compatibility/supermedo-runtime-v1.json. It contains symbol identities, code lengths, normalization positions, references and digests; no game assets or host executable are bundled. tools/generate-runtime-contract.py is a maintenance tool that needs Python and llvm-objdump. Never regenerate a contract just to approve an unknown update: review the helper/file ABI and hook coverage, run negative checks, then isolated live capture/restore tests. The end-user manager does not depend on Python/LLVM or downloadable profiles.

Managed-to-native activation configuration is protocol 2 (2220 bytes, 24 layout entries); pipe protocol stays 1. Native/layout.h documents the shared order. ExternalBridgeIdentity.g.cs binds the manager to its companion DLL.

## Validation so far

53 core tests passed before the public-surface guard was added; five focused contract checks now also pass, including synthetic PE section relocation, changed identity, broken function instructions/references, missing symbols, invalid export targets, malformed COFF and overlapping/truncated PE rejection. An actual-file check on the installed updated executable resolved all 132 functions, accepted an unrelated whole-file change and rejected a critical opcode mutation. Native tick ABI test passed; Release compilation completed without warnings/errors (vendored CMake emits its existing deprecation notice).

The 0.7 manager has now automatically connected to isolated updated runtime PID 43288, with verified guest instructions and live capabilities enabled (2026-10-12 22:06 local). The user subsequently confirmed checkpoint creation/restoration through UI buttons on the updated runtime. Prior 0.6 F11 and 0.5 live vial recovery were accepted by the user on the earlier runtime. These results must not be presented as proof of 0.7 live restoration until that test completes.

## Updated runtime acceptance and shortcut repair

The user confirmed checkpoint creation/restoration works on the updated runtime through the manager's buttons. The isolated log records creation at 22:07:12 and two successful restores at 22:07:24 and 22:07:49. The user reported global keys were unavailable and possibly another manager had been open. A later process snapshot showed only the isolated manager, so the exact initial owner of the keys is unconfirmed.

Version 0.7.1 adds one-second retries only for failed global registrations and a persistent key status in the checkpoint page, independent of operation/connection status. It never steals a key that another app still owns. A real Windows test with two synthetic owners and F21/F22/F23 confirmed conflicts remain blocked and freeing the first owner's keys allows the second to acquire them. The native DLL is unchanged from the accepted 0.7 run; manager-only reconnection can reuse it. The user then confirmed F5 creates a checkpoint and F11 opens the picker in 0.7.1. The manager was replaced without restarting the game, confirming reconnection to the matching bridge already loaded. An explicit fresh F9-key acceptance was not requested; its restoration workflow is the same guarded path verified through the UI.

## Atomic-save support in 0.8

See [UPDATE_0_8.md](UPDATE_0_8.md) for the reviewed atomic-save runtime, long COFF name fix and supported-mode migration. The original v1 contract remains available. Activation configuration is now protocol 3 (2228 bytes, 26 entries), including a validated file-record stride/path pair. The pipe protocol remains 1. Earlier protocol-2 sizes above describe 0.7 only.
