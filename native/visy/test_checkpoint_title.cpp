#include "checkpoint_title.h"
#include <cassert>
#include <cstdio>
int main() {
    CheckpointTitleEvidence e;
    assert(!e.Observe(false,false,0)); // no title evidence at startup
    assert(e.Observe(false,false,100));
    assert(!e.Observe(true,false,100)); // Continue invalidates old menu
    assert(!e.Observe(false,true,100)); // unloading: old evidence rejected
    assert(!e.Observe(false,true,200)); // title built while loading
    assert(e.Observe(false,false,200)); // same lookup survives slow unload
    assert(e.Observe(false,false,200)); // no repeated lookup required
    assert(!e.Observe(false,true,200)); // next load invalidates it
    assert(!e.Observe(false,false,200)); // a world-free transition is not title
    assert(!e.Observe(true,false,200));
    assert(!e.Observe(true,false,300)); // title built before player removed
    assert(e.Observe(false,false,300));
    puts("PASS title transitions: delayed unload, one-time lookup, stale menu, loading invalidation");
}
