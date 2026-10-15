// SPDX-License-Identifier: GPL-3.0-or-later
#include <cstdint>
#include <cstdio>
#define SYSV __attribute__((sysv_abi))
extern "C" void SYSV BcmTickHook();
extern "C" { void* BcmTickOriginal=nullptr; }
static unsigned calls;
extern "C" void BcmTick() {
    ++calls;
    asm volatile("xor %%rax,%%rax; xor %%rcx,%%rcx; xor %%rdx,%%rdx; xor %%r8,%%r8; xor %%r9,%%r9; xor %%r10,%%r10; xor %%r11,%%r11; pxor %%xmm0,%%xmm0; pxor %%xmm1,%%xmm1" ::: "rax","rcx","rdx","r8","r9","r10","r11","xmm0","xmm1","cc");
}
static uint64_t SYSV Integers(uint64_t a,uint64_t b,uint64_t c,uint64_t d,uint64_t e,uint64_t f,uint64_t g) {
    return a+3*b+5*c+7*d+11*e+13*f+17*g;
}
static double SYSV Floating(double a,double b) { return 3*a+7*b; }
int main() {
    BcmTickOriginal=reinterpret_cast<void*>(&Integers);
    using Int=uint64_t(SYSV*)(uint64_t,uint64_t,uint64_t,uint64_t,uint64_t,uint64_t,uint64_t);
    if (reinterpret_cast<Int>(&BcmTickHook)(1,2,3,4,5,6,7)!=302) return 1;
    BcmTickOriginal=reinterpret_cast<void*>(&Floating);
    using Float=double(SYSV*)(double,double);
    if (reinterpret_cast<Float>(&BcmTickHook)(1.5,2.5)!=22.0 || calls!=2) return 2;
    std::puts("PASS: SysV register/stack arguments, floating-point arguments and return values survive the Windows callback.");
}
