// SPDX-License-Identifier: GPL-3.0-or-later
// Preserve the guest SysV register arguments, flags and red zone before calling a Windows ABI callback.
.text
.globl BcmTickHook
BcmTickHook:
    leaq -128(%rsp), %rsp
    pushfq
    pushq %rax
    pushq %rcx
    pushq %rdx
    pushq %rsi
    pushq %rdi
    pushq %r8
    pushq %r9
    pushq %r10
    pushq %r11
    subq $296, %rsp
    .irp n,0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15
    movdqu %xmm\n, (32+16*\n)(%rsp)
    .endr
    call BcmTick
    .irp n,0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15
    movdqu (32+16*\n)(%rsp), %xmm\n
    .endr
    addq $296, %rsp
    popq %r11
    popq %r10
    popq %r9
    popq %r8
    popq %rdi
    popq %rsi
    popq %rdx
    popq %rcx
    popq %rax
    popfq
    leaq 128(%rsp), %rsp
    jmp *BcmTickOriginal(%rip)
