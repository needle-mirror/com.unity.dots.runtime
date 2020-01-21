#include "guard.h"

#include <stdlib.h>
#include <string.h>
#include <stdint.h>

#ifdef DEBUG
void memfail() {
#ifdef _WIN32
    __debugbreak();
#else
    abort();
#endif
}
#endif

#ifdef GUARD_HEAP

#define GUARD_HEAP_POISON 0xec  // the underlying allocators should poison on free; but be very sure.

static void guardcheck(uint8_t* p, uint8_t x, size_t s) {
    for ( size_t i=0; i<s; i++ ) {
        if (p[i]!=x) {
            memfail();
            return;
        }
    }
}

void setupGuardedMemory(void* mem, int headerSize, int64_t size)
{
    uint8_t* user = (uint8_t*)mem;

    // Setup head
    GuardHeader* head = (GuardHeader*)(user - sizeof(GuardHeader));
    head->size = size;
    head->offset = headerSize;

    // Setup buffer
    memset(mem, 0xbc, size);

    // Setup tail
    GuardFooter *tail = (GuardFooter*)(user + size);
    memset(tail->front, 0xa1, sizeof(tail->front));
    memset(tail->back, 0xa2, sizeof(tail->back));
}

void checkGuardedMemory(void* mem, bool poison)
{
    uint8_t* user = (uint8_t*)mem;
    GuardHeader* head = (GuardHeader*)(user - sizeof(GuardHeader));

    GuardFooter* tail = (GuardFooter*)(user + head->size);

    guardcheck(tail->front, 0xa1, sizeof(tail->front));
    guardcheck(tail->back, 0xa2, sizeof(tail->back));

    if (poison)
        memset(mem, GUARD_HEAP_POISON, (size_t)(head->size));
}

#endif
