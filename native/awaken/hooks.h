// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once
#include <cstdint>
#include <string>
namespace external {
struct State { bool world, loading, title; int echoes, level, vials, bullets, handles; uint64_t epoch, saveWrites; };
bool Install(uintptr_t host, std::string& error);
bool Ready();
bool Starting();
std::string StartupStatus();
State Snapshot();
bool Acquire(bool restoring);
bool Frozen();
void Release();
void BlockInput(bool blocked);
bool InputBlocked();
}
