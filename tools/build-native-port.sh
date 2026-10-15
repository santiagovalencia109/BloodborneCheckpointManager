#!/usr/bin/env bash
set -euo pipefail
export PATH="/clang64/bin:$PATH"
bcm_script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
cd /c/bcm-build/port
python "$bcm_script_dir/prepare-clang-compat.py" "$PWD"
python "$bcm_script_dir/patch-pad-input.py" "$PWD"
cp "$bcm_script_dir/../native/visy/checkpoint_bridge.cpp" gpu/shim/checkpoint_bridge.cpp
cp "$bcm_script_dir/../native/visy/checkpoint_io.h" gpu/shim/checkpoint_io.h
cp "$bcm_script_dir/../native/visy/checkpoint_title.h" gpu/shim/checkpoint_title.h
clang++ -std=c++17 "$bcm_script_dir/../native/visy/test_checkpoint_title.cpp" -o out/test_checkpoint_title.exe
out/test_checkpoint_title.exe
clang -std=c99 "$bcm_script_dir/../native/visy/test_checkpoint_io.c" src/runtime_checkpoint_io.c -Isrc -o out/test_checkpoint_io.exe
out/test_checkpoint_io.exe
cmake -S /c/bcm-build/miniz-current -B /c/bcm-build/miniz-current/build -G Ninja -DCMAKE_BUILD_TYPE=Release -DCMAKE_C_COMPILER=clang -DCMAKE_POLICY_VERSION_MINIMUM=3.5 -DBUILD_SHARED_LIBS=OFF -DBUILD_EXAMPLES=OFF -DBUILD_TESTS=OFF
cmake --build /c/bcm-build/miniz-current/build
mkdir -p out/atrac9
for file in third_party/LibAtrac9/C/src/*.c; do
  clang -std=c99 -O2 -w -c "$file" -o "out/atrac9/$(basename "${file%.c}").o"
done
llvm-ar rcs out/libatrac9.a out/atrac9/*.o
for patch in gpu/patches/fsr-vulkan/*.patch; do
  if ! git -c safe.directory=C:/bcm-build/port/gpu/third_party/fsr-vulkan -C gpu/third_party/fsr-vulkan apply --reverse --check "$PWD/$patch" 2>/dev/null; then
    git -c safe.directory=C:/bcm-build/port/gpu/third_party/fsr-vulkan -C gpu/third_party/fsr-vulkan apply "$PWD/$patch"
  fi
done
if [[ -f patches/fsr_vulkan_mingw.patch ]] && git -c safe.directory=C:/bcm-build/port/gpu/third_party/fsr-vulkan -C gpu/third_party/fsr-vulkan apply --check "$PWD/patches/fsr_vulkan_mingw.patch" 2>/dev/null; then
  git -c safe.directory=C:/bcm-build/port/gpu/third_party/fsr-vulkan -C gpu/third_party/fsr-vulkan apply "$PWD/patches/fsr_vulkan_mingw.patch"
fi
bcm_compat=$(printf '%s\n' '#pragma once' '#include <mutex>' '#include <cstdlib>' '#ifndef _countof' '#define _countof(a) (sizeof(a)/sizeof((a)[0]))' '#endif')
if [[ ! -f src/checkpoint_build_compat.h || "$(< src/checkpoint_build_compat.h)" != "$bcm_compat" ]]; then
  printf '%s\n' "$bcm_compat" > src/checkpoint_build_compat.h
fi
cmake -S gpu -B out/gpu -G Ninja -DCMAKE_BUILD_TYPE=Release -DBB_PGO=off -DBB_LTO=OFF -DCMAKE_C_COMPILER=clang -DCMAKE_CXX_COMPILER=clang++ -DMAGIC_ENUM_INC=C:/bcm-build/magic_enum/include -DVMA_INC=C:/bcm-build/msys64/clang64/include/vma '-DMINIZ_INC=C:/bcm-build/miniz-current;C:/bcm-build/miniz-current/build' -DMINIZ_LIB=C:/bcm-build/miniz-current/build/libminiz.a "-DCMAKE_CXX_FLAGS=-include C:/bcm-build/port/src/checkpoint_build_compat.h -IC:/bcm-build/xbyak"
cmake --build out/gpu --target bbport -- -j2
