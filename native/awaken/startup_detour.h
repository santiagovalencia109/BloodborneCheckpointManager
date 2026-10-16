// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstring>
#include <cstdint>

// Only at the reviewed pre-guest startup boundary. These exact 17 bytes contain
// complete instructions with no relative operands. No near-address allocation is needed.
inline bool InstallStartupTick(void* target,void* replacement,void** original) {
    const unsigned char prefix[]={0x55,0x48,0x89,0xe5,0x41,0x57,0x41,0x56,0x41,0x55,0x41,0x54,0x53,0x48,0x83,0xec,0x38};
    if(std::memcmp(target,prefix,sizeof(prefix))) return false;
    auto trampoline=static_cast<unsigned char*>(VirtualAlloc(nullptr,64,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
    if(!trampoline) return false;
    auto jump=[](unsigned char* bytes,void* destination) { const unsigned char instruction[]={0xff,0x25,0,0,0,0}; std::memcpy(bytes,instruction,6); std::memcpy(bytes+6,&destination,8); };
    std::memcpy(trampoline,prefix,sizeof(prefix)); jump(trampoline+sizeof(prefix),static_cast<unsigned char*>(target)+sizeof(prefix));
    DWORD old=0;
    if(!VirtualProtect(trampoline,64,PAGE_EXECUTE_READ,&old)) { VirtualFree(trampoline,0,MEM_RELEASE); return false; }
    FlushInstructionCache(GetCurrentProcess(),trampoline,64);
    unsigned char patch[sizeof(prefix)]; std::memset(patch,0x90,sizeof(patch)); jump(patch,replacement);
    if(!VirtualProtect(target,sizeof(prefix),PAGE_EXECUTE_READWRITE,&old)) { VirtualFree(trampoline,0,MEM_RELEASE); return false; }
    *original=trampoline; // Must be callable before publishing the entry jump.
    std::memcpy(target,patch,sizeof(patch)); FlushInstructionCache(GetCurrentProcess(),target,sizeof(patch));
    DWORD ignored=0;
    if(!VirtualProtect(target,sizeof(prefix),old,&ignored)) {
        // Retain executable trampoline: a published entry must never point at freed code.
        return false;
    }
    return true;
}
