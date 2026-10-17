"""Maintainer tool for the reviewed Awaken 0.9.33 LTO/SRW ABI. Does not execute the game."""
import sys,struct,json,hashlib
from pathlib import Path
data=Path(sys.argv[1]).read_bytes()
u16=lambda p:struct.unpack_from('<H',data,p)[0]
u32=lambda p:struct.unpack_from('<I',data,p)[0]
u64=lambda p:struct.unpack_from('<Q',data,p)[0]
pe=u32(60); opt=pe+24; base=u64(opt+24); sh=opt+u16(pe+20)
assert u16(pe+4)==0x8664
sections=[struct.unpack_from('<IIII',data,sh+n*40+8) for n in range(u16(pe+6))]
def offset(rva):
    for vs,va,size,raw in sections:
        if va<=rva<va+size:return raw+rva-va
    raise ValueError(hex(rva))
def string(at):return data[at:data.index(0,at)].decode('ascii')
start,count=u32(pe+12),u32(pe+16); strings=start+18*count; symbols=[]; i=0
while i<count:
    at=start+i*18; sec=struct.unpack_from('<h',data,at+12)[0]
    if sec>0 and data[at+16] in (2,3):
        name=string(strings+u32(at+4)) if u32(at)==0 else data[at:at+8].split(b'\0')[0].decode('ascii')
        if name and not name.startswith('.'):
            symbols.append((name,sections[sec-1][1]+u32(at+8),u16(at+14)==32))
    i+=1+data[at+17]
def required(name):
    matches=[s for s in symbols if s[0]==name]
    assert len(matches)==1,(name,len(matches))
    return matches[0][1]
files=[s for s in symbols if s[0].startswith('files.llvm.')]; assert len(files)==1
suffix=files[0][0][5:]; roots={required(n) for n in ['runtime_file_open','runtime_file_close','runtime_file_read','runtime_thread_attach_main','runtime_file_mount','runtime_file_configure','_ZN8BbEngine12_GLOBAL__N_19FrameHookEyPKy']}
tables={}
for name,rva,fn in symbols:
    if not name.startswith('exports.llvm.'):continue
    names=[]; targets=[]
    for n in range(128):
        try:
            at=offset(rva+n*16); text=string(offset(u64(at)-base)); target=u64(at+8)-base
            if target not in [s[1] for s in symbols if s[2]]:break
            names.append(text); targets.append(target)
        except (ValueError,UnicodeError,struct.error):break
    kind=next((k for k,first in [('file','sceKernelOpen'),('save','sceSaveDataInitialize'),('pad','scePadInit')] if names and names[0]==first),None)
    if kind:
        assert kind not in tables
        tables[kind]={'rva':rva,'count':len(names),'names':names,'targets':targets}; roots.update(targets)
assert set(tables)=={'file','save','pad'}
assert 'scePadRead' in tables['pad']['names'] and 'scePadReadState' in tables['pad']['names']
# Missing host helpers were removed by LTO, not substituted with SysV guest functions.
layout=[required('image'),required('gpu_enabled'),required('entered_game'),required('user_root'+suffix),required('lock'+suffix),files[0][1],required('AcquireSRWLockExclusive'),required('ReleaseSRWLockExclusive'),required('runtime_file_open'),required('runtime_file_close'),required('runtime_file_read'),0,0,0,0,0,0,required('runtime_thread_attach_main')]
for kind in ['file','save','pad']:layout.extend([tables[kind]['rva'],tables[kind]['count']])
layout.extend([536,24]); assert len(layout)==26
functions=sorted({s[1] for s in symbols if s[2]}); checks=[]
fixups=[]; reloc_rva=u32(opt+112+5*8); reloc_size=u32(opt+116+5*8)
if reloc_rva:
    pos=offset(reloc_rva); end=pos+reloc_size
    while pos<end:
        page=u32(pos); size=u32(pos+4); assert size>=8 and pos+size<=end
        for at in range(pos+8,pos+size,2):
            entry=u16(at); kind=entry>>12
            if kind==10:fixups.append(page+(entry&4095))
            else:assert kind==0,'Unexpected PE relocation'
        pos+=size
for rva in sorted(roots):
    end=next(r for r in functions if r>rva); length=end-rva
    assert 0<length<131072
    body=bytearray(data[offset(rva):offset(rva)+length]); masks=[]
    for fixup in fixups:
        if rva<=fixup<rva+length:
            assert fixup+8<=rva+length
            masks.extend(range(fixup-rva,fixup-rva+8)); body[fixup-rva:fixup-rva+8]=bytes(8)
    checks.append({'rva':rva,'length':length,'masks':masks,'sha256':hashlib.sha256(body).hexdigest().upper()})
out={'schema':1,'family':'awaken-0.9.33-lto-srw-v1','sha256':hashlib.sha256(data).hexdigest().upper(),'length':len(data),'layout':layout,'code':checks,'tables':tables}
Path(sys.argv[2]).write_text(json.dumps(out,separators=(',',':')),encoding='utf-8')
print('Awaken runtime:',out['sha256'],len(checks),'loaded-code checks; tables',[(k,v['count']) for k,v in tables.items()])
