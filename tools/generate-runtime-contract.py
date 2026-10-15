"""Build-time only: freeze critical ABI code shapes, allowing relocation, not semantic changes.
Requires llvm-objdump. Does not inject into or execute the inspected program.
"""
import argparse, hashlib, json, re, struct, subprocess
from pathlib import Path

arg=argparse.ArgumentParser()
arg.add_argument('executable'); arg.add_argument('objdump'); arg.add_argument('output')
arg.add_argument('--file-stride',required=True,type=lambda v:int(v,0),choices=[0x218,0x230])
arg.add_argument('--file-path-offset',required=True,type=int,choices=[24,32])
a=arg.parse_args()
if (a.file_stride,a.file_path_offset) not in [(0x218,24),(0x230,32)]: arg.error('File layout must be reviewed as a matching stride/path pair.')
data=Path(a.executable).read_bytes()
pe=struct.unpack_from('<I',data,60)[0]; nsec=struct.unpack_from('<H',data,pe+6)[0]
ptr,count=struct.unpack_from('<II',data,pe+12); opt=pe+24
base=struct.unpack_from('<Q',data,opt+24)[0]; sh=opt+struct.unpack_from('<H',data,pe+20)[0]
sections=[struct.unpack_from('<IIII',data,sh+n*40+8) for n in range(nsec)]
def offset(rva):
    for vs,va,size,raw in sections:
        if va<=rva<va+size: return raw+rva-va
    raise ValueError(f'Unbacked RVA {rva:x}')
def string(at):
    end=data.find(b'\0',at,min(len(data),at+65536))
    if end<0: raise ValueError('Unterminated name')
    return data[at:end].decode('ascii')
symbols=[]; i=0; strings=ptr+count*18
while i<count:
    at=ptr+i*18; name=data[at:at+8]; value,sec,typ,cls,aux=struct.unpack_from('<IhHBB',data,at+8)
    if sec>0 and cls in (2,3):
        name=string(strings+struct.unpack_from('<I',name,4)[0]) if name[:4]==bytes(4) else name.split(b'\0')[0].decode('ascii')
        if name and not name.startswith('.'):
            symbols.append(dict(name=name,rva=sections[sec-1][1]+value,function=typ==32))
    i+=1+aux
groups={}
for s in sorted(symbols,key=lambda s:s['rva']):
    group=groups.setdefault(s['name'],[]); s['key']=s['name']+'#'+str(len(group)); group.append(s)
byaddr={}
for s in symbols: byaddr.setdefault(s['rva'],[]).append(s)
functions=sorted({s['rva'] for s in symbols if s['function']})
tables={}; roots=set()
for s in groups['exports']:
    names=[]; targets=[]
    for n in range(256):
        try:
            name,fn=struct.unpack_from('<QQ',data,offset(s['rva']+n*16)); names.append(string(offset(name-base))); targets.append(fn-base)
        except (ValueError,UnicodeError,struct.error): break
    kind=next((k for k,v in [('file','sceKernelOpen'),('save','sceSaveDataInitialize'),('pad','scePadInit')] if names and names[0]==v),None)
    if kind:
        tables[kind]={'symbol':s['key'],'names':names,'functions':[byaddr[r][0]['key'] for r in targets]}
        roots.update(targets)
required=['runtime_file_mount','runtime_file_configure','runtime_file_user_dir','runtime_thread_attach_main','pthread_mutex_lock','pthread_mutex_unlock']
helpers=['runtime_file_'+n for n in ['open','close','read','pread','write','lseek','stat','fstat','getdents']]
scope=sorted(s['name'] for s in symbols if s['function'] and s['name'].startswith(('runtime_file_','runtime_savedata_','runtime_pad_')))
roots.update(groups[n][0]['rva'] for n in required+helpers+scope)
patterns=[]; visited=set()
while roots:
    start=roots.pop()
    if start in visited: continue
    visited.add(start); sym=next(s for s in byaddr[start] if s['function'])
    end=next(r for r in functions if r>start)
    if end-start>65536: raise ValueError('Excessive function')
    raw=bytearray(data[offset(start):offset(start)+end-start]); masks=[]; references=[]
    dis=subprocess.check_output([a.objdump,'-d',f'--start-address={base+start}',f'--stop-address={base+end}',a.executable],text=True,encoding='utf-8')
    for line in dis.splitlines():
        m=re.match(r'\s*([0-9a-f]+):\s*((?:[0-9a-f]{2}\s+)+)\s*(.*)',line)
        if not m: continue
        addr=int(m[1],16)-base; instr=bytes.fromhex(m[2]); text=m[3]; at=addr-start
        target=None; pos=None
        if '%rip' in text and re.search(r'#\s*0x([0-9a-f]+)',text):
            target=int(re.search(r'#\s*0x([0-9a-f]+)',text)[1],16)-base
            # Find the disp32 by its actual RIP-relative target, also handles trailing immediates.
            found=[j for j in range(len(instr)-3) if addr+len(instr)+struct.unpack_from('<i',instr,j)[0]==target]
            if len(found)!=1: raise ValueError('Ambiguous RIP operand: '+line)
            pos=found[0]
        elif len(instr)==5 and instr[0] in (0xe8,0xe9):
            target=addr+5+struct.unpack_from('<i',instr,1)[0]
            if not start<=target<end: pos=1
        if pos is not None:
            masks.extend(range(at+pos,at+pos+4)); raw[at+pos:at+pos+4]=bytes(4)
            matches=byaddr.get(target,[])
            if matches:
                chosen=next((s for s in matches if s['function']),matches[0])
                references.append({'offset':at+pos,'next':at+len(instr),'symbol':chosen['key']})
                # Include the runtime helper graph. OS/library implementations stay outside this range.
                if chosen['function'] and target<0x24000 and not chosen['name'].startswith('pthread_'): roots.add(target)
    patterns.append({'symbol':sym['key'],'length':len(raw),'sha256':hashlib.sha256(raw).hexdigest().upper(),'masks':masks,'references':references})
layout={n:groups[n][0]['key'] for n in ['image','gpu_enabled','entered_game','user_root','files','pthread_mutex_lock','pthread_mutex_unlock','runtime_thread_attach_main']+helpers}
# The file mutex must be the named global referenced by runtime_file_mount, not a guessed address.
mount=next(p for p in patterns if p['symbol']==groups['runtime_file_mount'][0]['key'])
locks={r['symbol'] for r in mount['references'] if r['symbol'].startswith('lock#')}
if len(locks)!=1: raise ValueError('File lock is ambiguous')
layout['file_lock']=locks.pop()
out={'schema':1,'family':('supermedo-runtime-c-v1' if a.file_stride==0x218 else 'supermedo-runtime-c-atomic-v2'),'baselineSha256':hashlib.sha256(data).hexdigest().upper(),'fileRecord':{'stride':a.file_stride,'pathOffset':a.file_path_offset},'scope':scope,'layout':layout,'tables':tables,'patterns':sorted(patterns,key=lambda p:p['symbol'])}
Path(a.output).parent.mkdir(parents=True,exist_ok=True); Path(a.output).write_text(json.dumps(out,separators=(',',':')),encoding='utf-8')
print(f'Contract: {len(patterns)} critical function shapes; {len(tables)} export tables; no whole-executable identity requirement.')
