"""Read-only ELF inspection of SaveRequest and candidate title-return instructions."""
import struct, sys
from pathlib import Path
data = Path(sys.argv[1]).read_bytes()
if data[:5] != b'\x7fELF\x02': raise SystemExit('Expected ELF64')
phoff = struct.unpack_from('<Q', data, 32)[0]
phsize, phcount = struct.unpack_from('<HH', data, 54)
segments = []
for i in range(phcount):
    typ, flags, off, va, _, size, _, _ = struct.unpack_from('<IIQQQQQQ', data, phoff + i * phsize)
    if typ == 1: segments.append((va, off, size, flags))
base = min(s[0] for s in segments)
print('ELF base', hex(base))
def read(va, n):
    for addr, off, size, _ in segments:
        if addr <= va and va + n <= addr + size:
            return data[off + va - addr:off + va - addr + n]
    raise ValueError('Unmapped virtual address')
print('SaveRequest:', read(base + 0x1336da0, 120).hex(' '))
target = base + 0x553b108
for va, off, size, flags in segments:
    if not flags & 1: continue
    code = data[off:off+size]
    for opcode in (b'\x48\x8b', b'\x48\x8d', b'\x4c\x8b', b'\x4c\x8d'):
        i = -1
        while True:
            i = code.find(opcode, i+1)
            if i < 0 or i + 64 >= len(code): break
            if code[i+2] & 0xc7 != 5: continue
            dest = va + i + 7 + struct.unpack_from('<i', code, i+3)[0]
            if dest == target and b'\x02\x00\x00\x00' in code[i+7:i+64]:
                print('Title candidate:', hex(va+i-base), code[i:i+64].hex(' '))
