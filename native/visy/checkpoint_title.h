// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once
#include <cstdint>

// Menu lookup is an entry event, not a heartbeat. Observe it across the
// unloading interval, while rejecting evidence left over from a prior title.
struct CheckpointTitleEvidence {
    std::int64_t floor = 0;
    bool was_world = false, was_loading = false;
    bool Observe(bool world, bool loading, std::int64_t lookup) {
        if ((world && !was_world) || (loading && !was_loading)) floor = lookup;
        was_world = world;
        was_loading = loading;
        return !world && !loading && lookup > floor;
    }
};
