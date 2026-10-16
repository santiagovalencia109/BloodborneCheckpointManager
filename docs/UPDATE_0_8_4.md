# 0.8.4: separate input compatibility from save implementation

Supermedo 1.8.2 was rejected because the old contract recursively pinned keyboard configuration and parsing code. The installed binary changes `input_config_defaults`, `input_config_load` and `pad_read_state`. These are unrelated to the file-record layout and save transaction implementation. The upstream release includes keyboard sprint fixes: https://github.com/Supermedo/bloodborne_pc/releases/tag/windows-v1.8.2.

Schema 2 keeps the original reviewed save/file function bodies, reference checks, scope and export tables. It removes the pad implementation and its private helper graph from byte-for-byte matching. Input uses the public guest Orbis ABI (`orbis-pad-120-v1`): `scePadRead` and `scePadReadState`, 120-byte records, existing guest instruction checks, executable export targets, and the native input barrier. Export names and their symbol targets are still checked. Hook installation can fail; live capabilities remain disabled on failure.

This is a deliberate trust boundary: the adapter relies on the port implementing the game's standard input ABI correctly. It no longer claims to verify every internal input instruction or keyboard setting. It does not accept arbitrary save ABI changes, stripped binaries, multiplayer, or every injected mod.

The two embedded contracts retain their original baseline hashes and code shapes. Their reviewed file/save helper closures contain 100 and 108 functions respectively. No new 1.8.2 signature or baseline was added. Regenerating an inspection contract from 1.8.2 yields the same 108 function hashes and reference sets as the retained atomic-save contract.

Installed runtime inspected (without execution or save modification):

```text
SHA-256: 9AF1FBBF9CF52AD6492F4D166558F9F0FA40D5DC712BF0FBD38695CC7EA53618
File length: 17,213,440 bytes
File record: 560-byte stride, path at offset 32
```

66 automated core tests pass, including input implementation changes being accepted, changed save instructions being rejected, missing/non-executable input exports, unknown input ABI, and incomplete save coverage. An actual-file mutation test accepts an unrelated file change and rejects a modified critical opcode. The existing native DLL is unchanged.

On 2026-10-16 the user reported that the revision works with 1.8.2. The exact gameplay steps and save isolation were not specified. The automated isolated-game launch had refused to start because another game was open; this report is user acceptance, not an agent-observed isolated F5/F9 test.

This reduces unnecessary updates; it cannot eliminate all port dependencies. A lasting integration would expose a versioned checkpoint API in the port itself: request engine save, return to title, acquire/release save access, and reload persistent state. The manager could negotiate capabilities instead of inspecting private runtime structures. That requires upstream cooperation; no issue or pull request has been sent.
