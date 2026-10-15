"""Apply the opt-in checkpoint bridge to a clean, pinned visy checkout.

Does not touch an installed game. Fails if expected source anchors do not match.
"""
from pathlib import Path
import re, shutil, sys, subprocess
root = Path(sys.argv[1]).resolve()
assets = Path(__file__).resolve().parents[1] / 'native' / 'visy'
subprocess.run([sys.executable, str(Path(__file__).with_name('patch-pad-input.py')), str(root)], check=True)
def edit(relative, transform):
    path = root / relative
    old = path.read_text(encoding='utf-8')
    new = transform(old)
    if old == new: raise RuntimeError('No change: ' + relative)
    path.write_text(new, encoding='utf-8', newline='\n')
def once(text, old, new):
    if text.count(old) != 1: raise RuntimeError('Anchor mismatch: ' + old[:80])
    return text.replace(old, new)
for name in ('checkpoint_io.h', 'runtime_checkpoint_io.c'):
    shutil.copyfile(assets / name, root / 'src' / name)
for name in ('checkpoint_io.h', 'checkpoint_title.h', 'checkpoint_bridge.h', 'checkpoint_bridge.cpp'):
    shutil.copyfile(assets / name, root / 'gpu' / 'shim' / name)
def file_patch(text):
    text=once(text, '#include "runtime.h"', '#include "runtime.h"\n#include "checkpoint_io.h"')
    text=once(text, 'return sce(do_##name args);', 'checkpoint_io_enter(); int64_t r=do_##name args; checkpoint_io_leave(); return sce(r);')
    text=once(text, 'return posix(do_##name args);', 'checkpoint_io_enter(); int64_t r=do_##name args; checkpoint_io_leave(); return posix(r);')
    text, count=re.subn(r'(int64_t runtime_file_\w+\([^\n]*?\) \{) return (do_\w+\([^;]+\)); \}', r'\1 checkpoint_io_enter(); int64_t r=\2; checkpoint_io_leave(); return r; }', text)
    if count != 9: raise RuntimeError('Unexpected host wrappers: ' + str(count))
    text,count=re.subn(r'(static ABI int64_t (?:posix_access|sce_mkdir|posix_mkdir|sce_rmdir|posix_rmdir|sce_unlink|posix_unlink)\([^\n]*?\) \{) return ([^;]+); \}', r'\1 checkpoint_io_enter(); int64_t r=\2; checkpoint_io_leave(); return r; }', text)
    if count!=7: raise RuntimeError('Unexpected path wrappers')
    text=once(text, 'GuestStat s; return (int32_t)sce(do_stat(p,&s));', 'checkpoint_io_enter(); GuestStat s; int32_t r=(int32_t)sce(do_stat(p,&s)); checkpoint_io_leave(); return r;')
    text+='\nint runtime_file_save_handles(int writers_only) {\n    int n=0; pthread_mutex_lock(&lock);\n    for (int i=3;i<MAX_FILES;++i) if (files[i].used && save_path(files[i].path) && (!writers_only || files[i].commit)) ++n;\n    pthread_mutex_unlock(&lock); return n;\n}\n'
    return text
edit('src/runtime_file.c',file_patch)
def save_patch(text):
    text=once(text, '#include "runtime.h"', '#include "runtime.h"\n#include "checkpoint_io.h"')
    wrappers=[]; names=[]
    for name,params in re.findall(r'static ABI int32_t (save_\w+|memory_setup|memory_get|memory_set)\(([^)]*)\)',text):
        args=[]
        for param in ([] if params.strip() == 'void' else params.split(',')):
            args.append(re.search(r'(\w+)\s*$',param).group(1))
        note='' if name in ('save_search','memory_get') else ' if (!bcm_result) checkpoint_io_note_write();'
        wrappers.append(f'static ABI int32_t ck_{name}({params}) {{ checkpoint_io_enter(); int32_t bcm_result={name}({",".join(args)});{note} checkpoint_io_leave(); return bcm_result; }}')
        names.append(name)
    if len(names)!=12: raise RuntimeError('Unexpected savedata API count')
    text=once(text,'static const RuntimeExport exports[]={','\n'.join(wrappers)+'\n\nstatic const RuntimeExport exports[]={')
    for name in names:
        text=re.sub(r'(?<=,)\s*'+name+r'(?=\})','ck_'+name,text)
    return text
edit('src/runtime_savedata.c',save_patch)
def gpu_patch(text):
    text='#include "checkpoint_bridge.h"\n'+text
    return once(text, '    bbnet_set_image(image, size);', '    checkpoint_bridge::Init(image, size);\n    bbnet_set_image(image, size);')
edit('gpu/shim/bbgpu.cpp',gpu_patch)
def cmake_patch(text):
    return text+'\nif(WIN32)\n  target_link_libraries(bbport PRIVATE advapi32 ole32)\n  set_source_files_properties(shim/checkpoint_bridge.cpp PROPERTIES COMPILE_OPTIONS "-mno-red-zone")\nendif()\n'
edit('gpu/CMakeLists.txt',cmake_patch)
print('Applied checkpoint addon to',root)
