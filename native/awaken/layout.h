// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once
#include <cstdint>
#include <array>
namespace runtime {
enum Field { Image,Gpu,Entered,Root,FileLock,Files,MutexLock,MutexUnlock,
 Open,Close,Read,Pread,Write,Lseek,Stat,Fstat,Getdents,MainAttach,
 FileTable,FileCount,SaveTable,SaveCount,PadTable,PadCount,FileStride,FilePathOffset,Count };
inline std::array<uint32_t,Count> layout{};
inline uint32_t At(Field field) { return layout[field]; }
}
