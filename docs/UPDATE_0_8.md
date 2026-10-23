# 0.8.1 — atomic-save runtime and batch deletion

## Updated Supermedo runtime

The verified installation at E:/Games/Bloodborne-Windows/bbport-windows now contains runtime SHA-256 09E1E524A773ECB2684AB64193E4AC74F0E0712B285A8BB210D0C85BEB3DFB77. Compatibility uses the atomic-v2 contract, not this hash as an allowlist. The October 15 update changes save I/O to temporary-copy writes committed on close. Its File record is 560 bytes with the guest path at offset 32, compared with the previous 536/24 pair. This was checked in runtime_file.c and installed do_open/do_close disassembly. The additional runtime_savedata_mounted query was inspected; the full new I/O contract contains 135 critical function shapes. The v1 contract and exact-hash legacy fallback remain supported.

The bridge now receives the reviewed record layout instead of assuming one stride forever. Configuration protocol 3 is 2228 bytes with 26 entries; pipe protocol stays 1. Unknown layouts and changed critical code remain rejected. The contract generator requires an explicit matching stride/path pair to prevent accidentally publishing a contract with the wrong file layout.

The previous symbol reader capped names at 512 bytes. The new executable includes valid names up to 747 bytes, so an unrelated graphics symbol caused a false rejection. Names are now read within the actual COFF string-table bounds, with malformed/unterminated strings still rejected.

The user's normal profile also selected the old prepared-port mode and referenced another downloaded executable while retaining the E: save root. The supported UI now offers only offline and automatic Supermedo modes; existing live profiles migrate in memory to the latter without changing save/library paths. The old BB_CHECKPOINT instruction is replaced by an appropriate connection message. The other downloaded runtime is not treated as compatible with the E: runtime's contract.

## Checkpoint selection and deletion

Ctrl/Shift selects multiple rows; Ctrl+A selects only visible rows. Delete or the Delete button asks once for the captured selection, including its count/names. Rename and restore require exactly one selected checkpoint. Selection survives language changes/refresh while matching entries remain visible; filtering never deletes hidden rows.

Batch deletion takes one library/target lock and validates all selected IDs, source roots and linked-file checks before any removal. IDs are deduplicated. It unpublishes/deletes only checkpoint directories, never the live save root. A pending restore blocks deletion. Cleanup failures retain hidden .deleted directories and are reported; a later move failure reports the actual partial count. The operation is not advertised as a multi-directory atomic transaction.

## Validation

57 core checks passed before the isolated game was opened, including the new symbol/layout/profile regressions. Six batch-delete checks were then added and passed with the other storage/contract checks (54 checks in that run); nine simulated live-pipe checks were explicitly skipped while the real game was open, and had passed in the earlier 57-check run. Native tick ABI test and Release builds passed.

Manager 0.8 connected to isolated updated runtime PID 21376 with live capabilities enabled. The user confirmed creation and restoration work again. Native bridge SHA-256: F4081491C7F1B67EEB262D8E1427C1B5FFC7A0A8E10B181D4A561F2FA7AB902C. GUI automation requested during this turn was declined and was not rerun. The user then confirmed that selecting multiple checkpoints in 0.8.1 asks once and deletes only the selected copies. The native DLL was unchanged between the 0.8 runtime test and the 0.8.1 UI update, so the manager reconnected without restarting the isolated game.

As before, start the manager before a fresh game session. Changes to critical file I/O or the game engine may still require a reviewed adapter update; relocation support does not guarantee compatibility with arbitrary semantic changes.
