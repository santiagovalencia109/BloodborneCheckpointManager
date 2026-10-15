/* SPDX-License-Identifier: GPL-3.0-or-later */
#include "checkpoint_io.h"
#ifdef _WIN32
#include <windows.h>
static SRWLOCK mutex = SRWLOCK_INIT;
static CONDITION_VARIABLE changed = CONDITION_VARIABLE_INIT;
static unsigned active;
static int frozen;
static uint64_t epoch;
static __thread unsigned depth;
static volatile LONG input_blocked;
void checkpoint_input_block(int blocked) { InterlockedExchange(&input_blocked,blocked!=0); }
int checkpoint_input_blocked(void) { return InterlockedCompareExchange(&input_blocked,0,0)!=0; }
void checkpoint_io_enter(void) {
    if (depth++) return;
    AcquireSRWLockExclusive(&mutex);
    while (frozen) SleepConditionVariableSRW(&changed, &mutex, INFINITE, 0);
    ++active;
    ReleaseSRWLockExclusive(&mutex);
}
void checkpoint_io_leave(void) {
    if (!depth || --depth) return;
    AcquireSRWLockExclusive(&mutex);
    --active;
    WakeAllConditionVariable(&changed);
    ReleaseSRWLockExclusive(&mutex);
}
void checkpoint_io_note_write(void) {
    AcquireSRWLockExclusive(&mutex); ++epoch; ReleaseSRWLockExclusive(&mutex);
}
uint64_t checkpoint_io_epoch(void) {
    AcquireSRWLockExclusive(&mutex); uint64_t result=epoch; ReleaseSRWLockExclusive(&mutex); return result;
}
int checkpoint_io_try_freeze(int restoring) {
    AcquireSRWLockExclusive(&mutex);
    /* active covers commit_file AFTER do_close removed its descriptor from the table. */
    int ok = !frozen && !active && !runtime_file_save_handles(!restoring);
    if (ok) frozen=1;
    ReleaseSRWLockExclusive(&mutex);
    return ok;
}
void checkpoint_io_thaw(void) {
    AcquireSRWLockExclusive(&mutex); frozen=0; WakeAllConditionVariable(&changed); ReleaseSRWLockExclusive(&mutex);
}
int checkpoint_io_frozen(void) {
    AcquireSRWLockExclusive(&mutex); int result=frozen; ReleaseSRWLockExclusive(&mutex); return result;
}
#else
/* This addon only advertises capabilities on Windows. */
void checkpoint_io_enter(void) {}
void checkpoint_input_block(int blocked) { (void)blocked; }
int checkpoint_input_blocked(void) { return 0; }
void checkpoint_io_leave(void) {}
void checkpoint_io_note_write(void) {}
uint64_t checkpoint_io_epoch(void) { return 0; }
int checkpoint_io_try_freeze(int restoring) { (void)restoring; return 0; }
void checkpoint_io_thaw(void) {}
int checkpoint_io_frozen(void) { return 0; }
#endif
