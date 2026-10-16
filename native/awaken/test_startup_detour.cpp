// SPDX-License-Identifier: GPL-3.0-or-later
#include "startup_detour.h"
static int Replacement() { return 77; }
int main() {
    const unsigned char code[]={0x55,0x48,0x89,0xe5,0x41,0x57,0x41,0x56,0x41,0x55,0x41,0x54,0x53,0x48,0x83,0xec,0x38,
        0x48,0x83,0xc4,0x38,0x5b,0x41,0x5c,0x41,0x5d,0x41,0x5e,0x41,0x5f,0x5d,0xb8,42,0,0,0,0xc3};
    auto target=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE)); if(!target) return 1;
    std::memcpy(target,code,sizeof(code)); DWORD old=0;
    if(!VirtualProtect(target,4096,PAGE_EXECUTE_READ,&old)) return 2;
    using Function=int(*)(); if(reinterpret_cast<Function>(target)()!=42) return 3;
    void* original=nullptr;
    if(!InstallStartupTick(target,reinterpret_cast<void*>(&Replacement),&original)) return 4;
    if(reinterpret_cast<Function>(target)()!=77 || reinterpret_cast<Function>(original)()!=42) return 5;
    void* rejected=nullptr;
    if(InstallStartupTick(target,reinterpret_cast<void*>(&Replacement),&rejected) || rejected) return 6;
    VirtualFree(target,0,MEM_RELEASE); VirtualFree(original,0,MEM_RELEASE); return 0;
}
