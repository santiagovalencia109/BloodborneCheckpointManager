#pragma once
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
void checkpoint_io_enter(void);
void checkpoint_io_leave(void);
void checkpoint_io_note_write(void);
uint64_t checkpoint_io_epoch(void);
/* Snapshot: no writers. Restore: no live save descriptors at all. Non-blocking. */
int checkpoint_io_try_freeze(int restoring);
void checkpoint_io_thaw(void);
int checkpoint_io_frozen(void);
void checkpoint_input_block(int blocked);
int checkpoint_input_blocked(void);
int runtime_file_save_handles(int writers_only);
#ifdef __cplusplus
}
#endif
