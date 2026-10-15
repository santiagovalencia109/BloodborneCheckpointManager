# External native bridge notices

The bridge sources are licensed GPL-3.0-or-later, as indicated by their SPDX headers. Guest engine state/inventory/save references are based on the GPL-3.0-or-later visy port at `c67ce7808f1b5be9013348f0e5119d7615c0babc` (`gpu/shim/party/game_state.cpp`, `coop_hooks.cpp`), which documents its bbhost references. The installed Supermedo binary was inspected to verify the host adapter. No original game code or assets are distributed here.

MinHook is vendored from TsudaKageyu/minhook commit `8af6b4acae5a9388fd742b56fa79ece89d96f823`. Its license is reproduced in `vendor/minhook/LICENSE.txt`. Local changes to `src/hook.c` add a startup-boundary callback, fail-closed suspension/context checks and retained thread handles. Those changes are included in the corresponding source.

nlohmann-json is MIT licensed, Copyright 2013–2025 Niels Lohmann. Its license is included with the packaged native licenses. LLVM libc++ and compiler runtime licenses used by the static CLANG64 build are also included. Windows system libraries are supplied by Windows.

Build dependencies and commands are documented in `docs/EXTERNAL_BRIDGE_USAGE.md`; the native `CMakeLists.txt` builds the DLL and callback ABI test. The published package includes matching source in `NativeSource`, and the separate source archive includes the .NET manager and native sources. Neither archive includes game images, game saves or checkpoints.
