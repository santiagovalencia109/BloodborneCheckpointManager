"""Narrow build compatibility edits for the pinned development port, never the installed game."""
from pathlib import Path
import re
import sys
root = Path(sys.argv[1]).resolve()
path = root / 'gpu/shadps4/video_core/texture_cache/texture_cache.h'
old = 'std::atomic_ref<const u32>(reinterpret_cast<const u32&>(image.flags))'
new = 'std::atomic_ref<u32>(const_cast<u32&>(reinterpret_cast<const u32&>(image.flags)))'
text = path.read_text(encoding='utf-8')
if text.count(old) == 2:
    path.write_text(text.replace(old, new), encoding='utf-8', newline='\n')
elif text.count(new) != 2:
    raise RuntimeError('Texture-cache compatibility anchor mismatch')
path = root / 'gpu/shadps4/video_core/renderer_vulkan/vk_rasterizer.cpp'
text = path.read_text(encoding='utf-8')
old = 'for (size_t block = dirty.blocks._Find_first(); block < dirty.blocks.size();\n         block = dirty.blocks._Find_next(block)) {'
new = 'for (size_t block = 0; block < dirty.blocks.size(); ++block) {\n        if (!dirty.blocks.test(block)) continue;'
if text.count(old) == 1:
    path.write_text(text.replace(old, new), encoding='utf-8', newline='\n')
elif text.count(new) != 1:
    raise RuntimeError('Bitset compatibility anchor mismatch')
path = root / 'gpu/shim/party/party_crypto.cpp'
text = path.read_text(encoding='utf-8')
old = '        rand_s(&v);'
new = '        if (BCryptGenRandom(nullptr, reinterpret_cast<PUCHAR>(&v), sizeof(v), BCRYPT_USE_SYSTEM_PREFERRED_RNG) != 0) std::abort();'
if text.count(new) == 1:
    text = text.replace(new, old).removeprefix('#ifdef _WIN32\n#include <windows.h>\n#include <bcrypt.h>\n#endif\n')
    path.write_text(text, encoding='utf-8', newline='\n')
elif text.count(old) != 1:
    raise RuntimeError('Windows random-source compatibility anchor mismatch')
text = path.read_text(encoding='utf-8')
updated = text.replace('#if defined(_WIN32)\n#define _CRT_RAND_S', '#if defined(_WIN32) && !defined(_CRT_RAND_S)\n#define _CRT_RAND_S')
if updated != text:
    path.write_text(updated, encoding='utf-8', newline='\n')
path = root / 'gpu/CMakeLists.txt'
text = path.read_text(encoding='utf-8')
marker = 'set_property(SOURCE shim/party/party_crypto.cpp APPEND PROPERTY COMPILE_DEFINITIONS _CRT_RAND_S)'
if marker not in text:
    path.write_text(text + '\n' + marker + '\n', encoding='utf-8', newline='\n')
text = path.read_text(encoding='utf-8')
marker = 'target_link_libraries(bbport PRIVATE winpthread)'
if marker not in text:
    path.write_text(text + '\nif(WIN32)\n  ' + marker + '\nendif()\n', encoding='utf-8', newline='\n')
path = root / 'src/runtime_savedata.c'
text = path.read_text(encoding='utf-8')
updated = re.sub(r'^static ABI int32_t ck_.*$', lambda match: match.group(0).replace('int32_t r=', 'int32_t bcm_result=').replace('if (!r)', 'if (!bcm_result)').replace('return r;', 'return bcm_result;').replace('=save_terminate(void);', '=save_terminate();'), text, flags=re.MULTILINE)
if updated != text:
    path.write_text(updated, encoding='utf-8', newline='\n')
