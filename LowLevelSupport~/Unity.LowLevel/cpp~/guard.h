#pragma once
#include <stdint.h>
#include <stdlib.h>

#ifdef ENABLE_UNITY_COLLECTIONS_CHECKS

void memfail();
#define MEM_FAIL()    { memfail(); }
#define MEM_ASSERT(x) { if (!(x)) { memfail(); }}

#define GUARD_PAD 32

struct GuardHeaderData {
    int64_t size;
    int32_t offset;
    int32_t powerof2padding;
};

static_assert(GUARD_PAD > sizeof(GuardHeaderData), "GUARD_PAD needs to be bigger.");
static_assert((GUARD_PAD & (GUARD_PAD - 1)) == 0, "GUARD_PAD needs to be a power of 2.");
static_assert((sizeof(GuardHeaderData) & (sizeof(GuardHeaderData) - 1)) == 0, "sizeof(GuardHeaderData) needs to be a power of 2.");

struct GuardHeader {
    // Where the constant for the pad can be any 2^n greater than sizeof(GuardHeaderData)
    static const int PAD = GUARD_PAD - sizeof(GuardHeaderData);
    static const int HEAD_SENTINEL = 0xa1;
    static const int TAIL_SENTINEL = 0xb1;

    GuardHeaderData data;
    uint8_t pad[PAD];
};

// Pointer to the memory that will be returned; setting up the padding
// is done before this call.
void setupGuardedMemory(void* mem, int32_t headerSize, int64_t size, bool initBuffer);
void checkGuardedMemory(void* mem, bool poison);

#else

#define MEM_FAIL() {}
#define MEM_ASSERT(x) {}

#endif

