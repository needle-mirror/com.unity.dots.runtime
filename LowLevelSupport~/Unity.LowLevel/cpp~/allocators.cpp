#include <allocators.h>
#include <baselibext.h>
#include <C/Baselib_Memory.h>
#include <C/Baselib_Atomic.h>

#include "guard.h"
#include "BumpAllocator.h"

#include <stdlib.h>
#include "string.h"

using namespace Unity::LowLevel;

#ifdef ENABLE_UNITY_COLLECTIONS_CHECKS
#include <vector>
#endif

#ifdef TRACY_ENABLE
#include "Tracy.hpp"
#endif

#ifdef _WIN32
#define DOEXPORT __declspec(dllexport)
#define CALLEXPORT __stdcall
#else
#define DOEXPORT __attribute__ ((visibility ("default")))
#define CALLEXPORT
#endif

// SSE requires 16 bytes alignment
// AVX 256 is 32 bytes
// AVX 512 is 64 bytes
// Arm tends to be 4 bytes
// Arm NEON can be up to 16 bytes (sometimes 2, 4, or 8 depending on instruction)
#if INTPTR_MAX == INT64_MAX
static const int ARCH_ALIGNMENT = 16;
#elif INTPTR_MAX == INT32_MAX
static const int ARCH_ALIGNMENT = 8;
#else
#error Unknown pointer size or missing size macros!
#endif

static int64_t heapUsage[(int)Allocator::NumAllocators] = { 0 };   // Debugging, multithread safe

static BumpAllocator sBumpAlloc;
static baselib::Lock sBumpAllocMutex;

static inline int checkAlignment(int alignOf)
{
    // Alignment is a power of 2
    MEM_ASSERT(alignOf == 0 || ((alignOf - 1) & alignOf) == 0);

    // Alignment is not greater than 65536
    MEM_ASSERT(alignOf < 65536);

    if (alignOf < ARCH_ALIGNMENT)
        alignOf = ARCH_ALIGNMENT;

    return alignOf;
}

static inline int64_t calcPaddedSize(int64_t userSize, int headerSize)
{
#ifdef ENABLE_UNITY_COLLECTIONS_CHECKS
    return
        headerSize +                            // Size for the header, or alignOf, whichever is greater.
        userSize +                              // Size of user request
        GUARD_PAD;                              // Size of tail buffer (in bytes - no need to align)
#else
    (void)headerSize;
    return userSize;
#endif
}

static void checkAllocation(void* userPtr, int64_t userSize, int headerSize, bool initBuffer_0xbc, Allocator allocatorType)
{
    // Track memory size and pointer
#ifdef TRACY_ENABLE
    TracyAlloc(userPtr, userSize);
#endif

#ifdef ENABLE_UNITY_COLLECTIONS_CHECKS
    int64_t paddedSize = calcPaddedSize(userSize, headerSize);
    int64_t old;
    Baselib_atomic_fetch_add_64_relaxed_v(&heapUsage[(int)allocatorType], &paddedSize, &old);

    setupGuardedMemory(userPtr, headerSize, userSize, initBuffer_0xbc);

#else
#ifndef TRACY_ENABLE
    (void)userPtr;
    (void)userSize;
#endif
    (void)headerSize;
    (void)allocatorType;
#endif
}

static void* checkDeallocation(void* userPtr, bool willFreeMem, Allocator allocatorType)
{
#ifdef TRACY_ENABLE
    TracyFree(userPtr);
#endif

#ifdef ENABLE_UNITY_COLLECTIONS_CHECKS
    if (!userPtr)
        return nullptr;

    checkGuardedMemory(userPtr, willFreeMem);

    GuardHeader* head = (GuardHeader*)userPtr - 1;

    int64_t old;
    int64_t paddedSizeNeg = -calcPaddedSize(head->data.size, head->data.offset);
    Baselib_atomic_fetch_add_64_relaxed_v(&heapUsage[(int)allocatorType], &paddedSizeNeg, &old);

    return (void*)((uint8_t*)userPtr - head->data.offset);
#else
    (void)allocatorType;
    return userPtr;
#endif
}

extern "C" 
{

DOEXPORT
void* CALLEXPORT unsafeutility_malloc(int64_t size, int alignOf, Allocator allocatorType)
{
    alignOf = checkAlignment(alignOf);

#ifdef ENABLE_UNITY_COLLECTIONS_CHECKS
    int headerSize = alignOf < sizeof(GuardHeader) ? sizeof(GuardHeader) : alignOf;
#else
    int headerSize = 0;
#endif
    int64_t paddedSize = calcPaddedSize(size, headerSize);

    void* memBase;
    void* memUser;
    if (allocatorType == Allocator::Temp)
    {
        BaselibLock lock(sBumpAllocMutex);
        memBase = sBumpAlloc.alloc((int)paddedSize, alignOf);
        memUser = (void*)((uint8_t*)memBase + headerSize);
    }
    else
    {
        memBase = Baselib_Memory_AlignedAllocate(paddedSize, alignOf);
        memUser = (void*)((uint8_t*)memBase + headerSize);
    }

    checkAllocation(memUser, size, headerSize, true, allocatorType);

    return memUser;
}

// To support the same debug/performance tools in some native 3rd party libraries as we do everywhere else
// which allocates native memory on the heap
DOEXPORT
void* CALLEXPORT unsafeutility_realloc(void* ptr, int64_t newSize, int alignOf, Allocator allocatorType)
{
    // For now, only support reallocation of persistent memory
    MEM_ASSERT(allocatorType == Allocator::Persistent);

    // This isn't per spec for standard c realloc calls, but some 3rd party libraries rely on this
    // behaviour so we'll implement it to ensure no hidden issues.
    if (newSize == 0)
    {
        unsafeutility_free(ptr, allocatorType);
        return nullptr;
    }

    alignOf = checkAlignment(alignOf);

#ifdef ENABLE_UNITY_COLLECTIONS_CHECKS
    int headerSize = alignOf < sizeof(GuardHeader) ? sizeof(GuardHeader) : alignOf;
#else
    int headerSize = 0;
#endif
    int64_t paddedSize = calcPaddedSize(newSize, headerSize);

    void* realPtr = checkDeallocation(ptr, false, allocatorType);

    void* memBase = Baselib_Memory_AlignedReallocate(realPtr, paddedSize, alignOf);
    void* memUser = (void*)((uint8_t*)memBase + headerSize);

    checkAllocation(memUser, newSize, headerSize, ptr == nullptr, allocatorType);

    return memUser;
}

DOEXPORT
void CALLEXPORT unsafeutility_assertheap(void* ptr)
{
    MEM_ASSERT(ptr);
#ifdef ENABLE_UNITY_COLLECTIONS_CHECKS
    checkGuardedMemory(ptr, false);
#endif
}

DOEXPORT
void CALLEXPORT unsafeutility_free(void* ptr, Allocator allocatorType)
{
    if (ptr == nullptr)
        return;

    void* realPtr = checkDeallocation(ptr, true, allocatorType);

    if (allocatorType == Allocator::Temp)
        return;

    Baselib_Memory_AlignedFree(realPtr);
}

DOEXPORT
int64_t CALLEXPORT unsafeutility_get_heap_size(Allocator allocatorType)
{
    return heapUsage[(int)allocatorType];
}

DOEXPORT
void CALLEXPORT unsafeutility_memset(void* destination, char value, int64_t size)
{
    memset(destination, value, static_cast<size_t>(size));
}

DOEXPORT
void CALLEXPORT unsafeutility_memclear(void* destination, int64_t size)
{
    memset(destination, 0, static_cast<size_t>(size));
}

DOEXPORT
void CALLEXPORT unsafeutility_freetemp()
{
    BaselibLock lock(sBumpAllocMutex);
    sBumpAlloc.reset();
    heapUsage[(int)Allocator::Temp] = 0;
}

#define UNITY_MEMCPY memcpy
typedef uint8_t UInt8;

DOEXPORT
void CALLEXPORT unsafeutility_memcpy(void* destination, void* source, int64_t count)
{
    UNITY_MEMCPY(destination, source, (size_t)count);
}

DOEXPORT
void CALLEXPORT unsafeutility_memcpystride(void* destination_, int destinationStride, void* source_, int sourceStride, int elementSize, int64_t count)
{   
    UInt8* destination = (UInt8*)destination_;
    UInt8* source = (UInt8*)source_;
    if (elementSize == destinationStride && elementSize == sourceStride)
    {
        UNITY_MEMCPY(destination, source, static_cast<size_t>(count) * static_cast<size_t>(elementSize));
    }
    else
    {
        for (int i = 0; i != count; i++)
        {
            UNITY_MEMCPY(destination, source, elementSize);
            destination += destinationStride;
            source += sourceStride;
        }
    }
}

DOEXPORT
int32_t CALLEXPORT unsafeutility_memcmp(void* ptr1, void* ptr2, uint64_t size)
{
    return memcmp(ptr1, ptr2, (size_t)size);
}

DOEXPORT
void CALLEXPORT unsafeutility_memcpyreplicate(void* dst, void* src, int size, int count)
{
    uint8_t* dstbytes = (uint8_t*)dst;
    // TODO something smarter	
    for (int i = 0; i < count; ++i)
    {
        memcpy(dstbytes, src, size);
        dstbytes += size;
    }
}

DOEXPORT
void CALLEXPORT unsafeutility_memmove(void* dst, void* src, uint64_t size)
{
    memmove(dst, src, (size_t)size);
}


typedef void (*Call_p)(void*);
typedef void (*Call_pp)(void*, void*);
typedef void (*Call_pi)(void*, int);

DOEXPORT
void CALLEXPORT unsafeutility_call_p(void* f, void* data)
{
    MEM_ASSERT(f);
    Call_p func = (Call_p) f;
    func(data);
}

DOEXPORT
void CALLEXPORT unsafeutility_call_pp(void* f, void* data1, void* data2)
{
    MEM_ASSERT(f);
    Call_pp func = (Call_pp)f;
    func(data1, data2);
}

DOEXPORT
void CALLEXPORT unsafeutility_call_pi(void* f, void* data, int i)
{
    MEM_ASSERT(f);
    Call_pi func = (Call_pi)f;
    func(data, i);
}

int inJob = 0;

DOEXPORT
int CALLEXPORT unsafeutility_get_in_job()
{
    return inJob;
}

DOEXPORT
void CALLEXPORT unsafeutility_set_in_job(int v)
{
    inJob = v;
}

} // extern "C"
