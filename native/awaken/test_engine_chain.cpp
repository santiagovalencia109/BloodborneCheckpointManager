// SPDX-License-Identifier: GPL-3.0-or-later
#include "startup_detour.h"
#include <cstdio>
static void* original;
static int Replacement() { return reinterpret_cast<int(*)()>(original)()+1; }
int main() {
    auto page=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));
    auto stub=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));
    auto callback=static_cast<unsigned char*>(VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));
    if(!page || !stub || !callback) return 1;
    unsigned calls=0;
    // Synthetic callback with the reviewed entry and a visible effect.
    const unsigned char begin[]={0x55,0x41,0x57,0x41,0x56,0x41,0x54,0x53,0x48,0x83,0xec,0x40,0x48,0x8d,0x6c,0x24,0x40,0x48,0xb8};
    std::memcpy(callback,begin,sizeof(begin)); auto counter=reinterpret_cast<uintptr_t>(&calls); std::memcpy(callback+sizeof(begin),&counter,8);
    const unsigned char end[]={0xff,0x00,0x48,0x83,0xc4,0x40,0x5b,0x41,0x5c,0x41,0x5e,0x41,0x5f,0x5d,0x31,0xc0,0xc3};
    std::memcpy(callback+sizeof(begin)+8,end,sizeof(end));
    const auto host=reinterpret_cast<uintptr_t>(callback)-0x119f80;
    auto bytes=AwakenFrameStub(host,reinterpret_cast<uintptr_t>(page)+17); std::memcpy(stub,bytes.data(),bytes.size());
    const unsigned char body[]={0x48,0x83,0xc4,0x38,0x5b,0x41,0x5c,0x41,0x5d,0x41,0x5e,0x41,0x5f,0x5d,0xb8,42,0,0,0,0xc3};
    std::memcpy(page+17,body,sizeof(body));
    const unsigned char jump[]={0xff,0x25,0,0,0,0}; std::memcpy(page,jump,6);
    const auto destination=reinterpret_cast<uintptr_t>(stub); std::memcpy(page+6,&destination,8); std::memset(page+14,0xcc,3);
    DWORD old=0; VirtualProtect(callback,4096,PAGE_EXECUTE_READ,&old); VirtualProtect(stub,4096,PAGE_EXECUTE_READ,&old); VirtualProtect(page,4096,PAGE_EXECUTE_READ,&old);
    if(VerifiedAwakenFrame(page,host)!=stub) return 2;
    if(reinterpret_cast<int(*)()>(page)()!=42 || calls!=1) return 3;
    // Every emitted opcode, including the callback and return destination, is checked.
    for(size_t n=0;n<bytes.size();++n) {
        VirtualProtect(stub,4096,PAGE_READWRITE,&old); stub[n]^=1; VirtualProtect(stub,4096,PAGE_EXECUTE_READ,&old);
        if(VerifiedAwakenFrame(page,host)) return 4;
        void* rejected=nullptr;
        if(InstallStartupTick(page,reinterpret_cast<void*>(&Replacement),&rejected,host) || rejected) return 5;
        VirtualProtect(stub,4096,PAGE_READWRITE,&old); stub[n]^=1; VirtualProtect(stub,4096,PAGE_EXECUTE_READ,&old);
    }
    if(!InstallStartupTick(page,reinterpret_cast<void*>(&Replacement),&original,host) || original!=stub) return 6;
    if(reinterpret_cast<int(*)()>(page)()!=43 || calls!=2) return 7;
    if(reinterpret_cast<int(*)()>(original)()!=42 || calls!=3) return 8;
    VirtualFree(page,0,MEM_RELEASE); VirtualFree(stub,0,MEM_RELEASE); VirtualFree(callback,0,MEM_RELEASE);
    std::puts("PASS: verified engine chain retains its callback and original; all 219 altered bytes rejected"); return 0;
}
