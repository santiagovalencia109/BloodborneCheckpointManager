# Awaken adapter

GPL-3.0-or-later. Derived from this project's external adapter; guest engine state/save references retain the provenance documented in `../external/THIRD_PARTY_NOTICES.md`.

Awaken sources inspected: https://github.com/Awakenchan/BloodBorneButPc, `engine/bbhost-changes.patch`, based on DarkIzuku/bloodborne_pc tag `v0.4.0-darkizuku-preview.1`, commit `8ecba9a4abdd54747e31d7fee54982e422b5f8e2`. Awaken's engine is GPL-3.0-or-later. No engine executable or game assets are distributed here.

MinHook sources and local modifications are shared from `../external/vendor/minhook`; its license and the dependency notices there apply. The build also uses nlohmann-json (MIT) and LLVM runtimes; licenses are included in the release's `Licenses` folder. See `docs/AWAKEN.md` for build steps and the ABI review.

`engine_frame.h` validates the emitted FrameHook stub from `gpu/shim/engine/engine_hooks.cpp` (EmitPrologueStub, GPL-3.0-or-later) in the reviewed Awaken engine. Its register-save/restore byte sequence retains that source’s bbhost thunk provenance. The bridge calls through the verified stub; it does not distribute the engine or its prepared assets.
