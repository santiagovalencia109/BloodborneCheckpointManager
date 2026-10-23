// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once
#include <windows.h>
#include <cstring>
#include <cstdint>

// Awaken patches the guest frame function with `jmp [rip+0]; dq stub; int3 x3` and its stub is:
//   push rdi,rsi,rdx,rcx,r8,r9 ... movabs rax,<callback in the engine>; call rax ...   (header)
//   <the 17 displaced guest prologue bytes>                                          (exact)
//   jmp [rip+0]; dq target+17                                                        (exact)
// 0.9.33 (EmitPrologueStub, 194-byte header) and later builds (WriteStub, 189-byte header) differ
// only in the header. The chain is accepted by that structure, not by an exact header, so a new
// Awaken build does not need a new bridge as long as it keeps this shape.
// Never follow an arbitrary jump: the stub must be private read/execute memory, the callback must be
// code inside the hash-verified engine image, and the stub must resume the exact original prologue.
namespace awaken_frame {
inline constexpr unsigned char kPrologue[17]={0x55,0x48,0x89,0xe5,0x41,0x57,0x41,0x56,0x41,0x55,0x41,0x54,0x53,0x48,0x83,0xec,0x38};
inline constexpr unsigned char kSaves[8]={0x57,0x56,0x52,0x51,0x41,0x50,0x41,0x51};
inline constexpr size_t kMinHeader=64, kMaxHeader=384, kWindow=kMaxHeader+sizeof(kPrologue)+14;

inline bool ReadSelf(const void* at,void* out,size_t size) {
    SIZE_T count=0;
    return ReadProcessMemory(GetCurrentProcess(),at,out,size,&count) && count==size;
}
inline bool HostCode(uintptr_t address,uintptr_t host) {
    MEMORY_BASIC_INFORMATION region{};
    if(!address || !VirtualQuery(reinterpret_cast<void*>(address),&region,sizeof(region))) return false;
    const DWORD execute=PAGE_EXECUTE|PAGE_EXECUTE_READ|PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY;
    return region.State==MEM_COMMIT && region.Type==MEM_IMAGE && (region.Protect&execute) &&
        reinterpret_cast<uintptr_t>(region.AllocationBase)==host;
}
}

inline void* VerifiedAwakenFrame(void* target,uintptr_t host) {
    using namespace awaken_frame;
    if(!host) return nullptr;
    unsigned char entry[17];
    if(!ReadSelf(target,entry,sizeof(entry))) return nullptr;
    const unsigned char jump[]={0xff,0x25,0,0,0,0};
    if(std::memcmp(entry,jump,6) || entry[14]!=0xcc || entry[15]!=0xcc || entry[16]!=0xcc) return nullptr;
    uintptr_t destination=0; std::memcpy(&destination,entry+6,8);

    MEMORY_BASIC_INFORMATION region{};
    if(!VirtualQuery(reinterpret_cast<void*>(destination),&region,sizeof(region)) || region.State!=MEM_COMMIT ||
        (region.Protect&0xff)!=PAGE_EXECUTE_READ || region.Type!=MEM_PRIVATE) return nullptr;
    const auto regionEnd=reinterpret_cast<uintptr_t>(region.BaseAddress)+region.RegionSize;
    if(destination>=regionEnd) return nullptr;
    const size_t available=static_cast<size_t>(regionEnd-destination);
    const size_t size=available<kWindow ? available : kWindow;
    unsigned char stub[kWindow];
    if(size<kMinHeader+sizeof(kPrologue)+14 || !ReadSelf(reinterpret_cast<void*>(destination),stub,size)) return nullptr;

    // Exactly one tail: displaced prologue followed by an absolute jump back to target+17.
    const auto resume=reinterpret_cast<uintptr_t>(target)+sizeof(kPrologue);
    size_t tail=0; unsigned tails=0;
    for(size_t at=kMinHeader;at<=kMaxHeader && at+sizeof(kPrologue)+14<=size;++at) {
        if(std::memcmp(stub+at,kPrologue,sizeof(kPrologue))) continue;
        const auto back=stub+at+sizeof(kPrologue); uintptr_t to=0; std::memcpy(&to,back+6,8);
        if(!std::memcmp(back,jump,6) && to==resume) { tail=at; ++tails; }
    }
    if(tails!=1) return nullptr;

    // Header: saves the argument registers first and calls exactly one engine callback through RAX.
    if(std::memcmp(stub,kSaves,sizeof(kSaves))) return nullptr;
    unsigned calls=0; uintptr_t callback=0;
    for(size_t at=0;at+12<=tail;++at) {
        if(stub[at]!=0x48 || stub[at+1]!=0xb8 || stub[at+10]!=0xff || stub[at+11]!=0xd0) continue;
        std::memcpy(&callback,stub+at+2,8); ++calls;
    }
    if(calls!=1 || !HostCode(callback,host)) return nullptr;
    return reinterpret_cast<void*>(destination);
}
