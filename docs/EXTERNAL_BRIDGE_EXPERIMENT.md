# External bridge experiment log

Date: 2026-10-12. Original game executable: installed Supermedo `bin/bb-probe.exe`, SHA-256 `4AE2D15EAFCE351C61DC10B8498B2BAD61CDC3A5C15C92E274FD63A7A4FC312A`. No replacement port executable was used.

All writes and restoration targets were under `artifacts/external-fixture-01/user/savedata`. The fixture was prepared from immutable checkpoint `ef2f80f8623e40959060b3739ef0ea43` in the earlier isolated fixture. The original installation's save directory was not a restore target. The original packaged linked image, content profile and patches were read for launching the original runtime.

| Run | Result |
| --- | --- |
| PID 17160 | Separate observer DLL loaded into an already running game. Host fingerprint, source directory and guest code checks passed; hello reported observation only. |
| PID 9052 | Initial hook prototype refused activation; observation remained available. Added diagnostics. |
| PID 31764 | Diagnostics identified that the runtime resolver accepts NIDs rather than ordinary names. No live capability advertised. |
| PID 34964 | Host/export hooks were prepared, but the guest page was not executable during GPU startup. Live capability remained disabled. |
| PID 38672 | Deferred main-thread hook installed after executable-page protection. Capture checkpoint `6d0d7f3adbd440818ce966a8263944d9` with 2 vials. Used one: telemetry 1 vial. F9 restored that checkpoint and Continue reported 2 vials in the loaded world. |
| PID 3596 | Added header verification refused activation because a full PE-header comparison included loader-dependent fields. No saves restored during this refusal. |
| PID 14692 | Revised stable header verification passed. Capture checkpoint `084f0354c3844b55926593a1cf593fbe` with 2 vials. Used one: telemetry 1 vial. F9 with Cross held restored the checkpoint. Follow-up hello reported `title=true`, `world=false` while Cross remained held. After releasing and pressing again, the loaded world reported 2 vials. |
| PID 33292, manager PID 7968 | Published manager 0.5 started before the original runtime. The manager's own automatic watcher attached the final DLL. The manager log recorded F5 capture and automatic F9 restores at 17:09–17:10 local time. The user confirmed that the vial was recovered and the flow worked without Yes/No prompts. |

The later native revisions add pad-read nesting protection, fail-closed thread suspension/context checks, preserved suspension handles, a post-patch verification of guest instructions, and a callback ABI test. The manager checks the exact companion DLL hash and avoids loading a second bridge version into the same process.

Core verification: 49 tests passed, including all 9 real-pipe client tests. Native callback test passed for SysV register and stack arguments, floating-point arguments and return values across the Windows callback. English/Spanish UI tests passed with 12 rendered previews at two sizes and a synthetic Windows lifecycle test.

Manual F5/F9 in the published manager passed user acceptance: the user answered “si funciona” to the vial recovery / automatic restoration check. Programmatic operations above call the same `LiveBridgeClient` and `CheckpointStore` used by the UI. The final bridge DLL SHA-256 is `D77DAF57EC163B98D27EA8B739536F88DF6B5CED4D93F6FF2E445C50C8A86AF4`. No claim of universal port compatibility, changed-echo/bullet recovery, exact position fidelity, or exhaustive native failure testing is made.
