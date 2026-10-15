#include "checkpoint_io.h"
#include "checkpoint_input.h"
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
static volatile LONG handles;
static HANDLE entered, completed;
int runtime_file_save_handles(int writers_only) { (void)writers_only; return (int)InterlockedCompareExchange(&handles,0,0); }
static DWORD WINAPI reader(void *arg) {
    (void)arg; SetEvent(entered); checkpoint_io_enter(); SetEvent(completed); checkpoint_io_leave(); return 0;
}
static void check(int ok,const char *why) { if (!ok) { fprintf(stderr,"FAIL: %s\n",why); exit(1); } }
int main(void) {
    int held=0;
    check(!checkpoint_input_blocked(),"input starts unblocked");
    checkpoint_input_block(1);
    check(checkpoint_input_blocked(),"restore blocks input");
    check(checkpoint_input_neutral(1,1,&held),"held Continue neutral during restore");
    checkpoint_input_block(0);
    check(checkpoint_input_neutral(0,1,&held),"held Continue still neutral after release");
    check(!checkpoint_input_neutral(0,0,&held),"physical release rearms input");
    check(!checkpoint_input_neutral(0,1,&held),"new Continue accepted after release");
    checkpoint_io_enter(); checkpoint_io_enter();
    check(!checkpoint_io_try_freeze(0),"cannot freeze active/nested operation");
    checkpoint_io_leave(); check(!checkpoint_io_try_freeze(0),"outer operation still active"); checkpoint_io_leave();
    InterlockedExchange(&handles,1); check(!checkpoint_io_try_freeze(1),"open descriptors block restore"); InterlockedExchange(&handles,0);
    check(checkpoint_io_try_freeze(1),"idle freeze succeeds");
    entered=CreateEvent(NULL,TRUE,FALSE,NULL); completed=CreateEvent(NULL,TRUE,FALSE,NULL);
    HANDLE thread=CreateThread(NULL,0,reader,NULL,0,NULL); check(thread!=NULL,"reader created");
    check(WaitForSingleObject(entered,1000)==WAIT_OBJECT_0,"reader started");
    check(WaitForSingleObject(completed,100)==WAIT_TIMEOUT,"frozen gate blocks reader");
    checkpoint_io_thaw(); check(WaitForSingleObject(completed,1000)==WAIT_OBJECT_0,"release unblocks reader");
    WaitForSingleObject(thread,1000); CloseHandle(thread); CloseHandle(entered); CloseHandle(completed);
    uint64_t before=checkpoint_io_epoch(); checkpoint_io_note_write(); check(checkpoint_io_epoch()==before+1,"metadata generation accounted");
    puts("PASS native gate: nesting, active commit coverage, handles, blocked readers, thaw and metadata epoch"); return 0;
}
