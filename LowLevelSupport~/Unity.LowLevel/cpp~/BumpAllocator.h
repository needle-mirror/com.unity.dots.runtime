#pragma once

#include <memory>
#include <stdlib.h>
#include "guard.h"

struct BumpChunk;

struct BumpChunkHeader
{
    BumpChunk* next;
    BumpChunk* prev;
    size_t size;  // size includes the header
};

struct BumpChunk
{
    BumpChunkHeader header;
    uint8_t data[1];
};

struct RewindInfo
{
    BumpChunk* prevChunk;
    RewindInfo* prevRewind;
    uint8_t* prevPtr;
    void* user;
    size_t prevTotalUsed;
};

class BumpAllocator
{
public:
    void* alloc(size_t size, size_t alignment)
    {
#ifdef ENABLE_UNITY_COLLECTIONS_CHECKS
        // We should be in a scope at this point
        if (!mRewind)
            return nullptr;
#endif

        // This gets us a new temporary 'allocated' memory address in the fewest possible
        // instructions, branches, and mispredictions.

        // Get next aligned memory address
        // We currently always request alignment, so there is no reason to conditionally branch
        size_t alignMask = alignment - 1;
        uint8_t* ret = (uint8_t*)((size_t)(mCurPtr + alignMask) & ~alignMask);

        // Increase position in the reserved chunk, and return requested memory if there was enough space
        // The condition evaluates to true in the majority of allocations and so branch prediction is happy
        mCurPtr = ret + size;
        if (mCurPtr <= mChunkEnd)
        {
            mTotalUsed += size;
            return ret;
        }

        newChunk(size + alignment);
        return alloc(size, alignment);
    }

    void reset()
    {
        // We should not be in any scopes at this point
        MEM_ASSERT(mRewind == nullptr);
        MEM_ASSERT(mCurPtr == &mChunk->data[0]);
        MEM_ASSERT(mTotalUsed == 0);

        // Instead of a chunk reuse strategy for the previously allocated smaller chunks, we just free
        // them with the idea that eventually we have one chunk that is large enough to hold all temp allocations.
        //
        // Do this instead of realloc'ing so there is no performance hit during the first frame (or whenever) while
        // we work out the appropriate bump allocator reserved size.
        freeExtraChunks();
        mChunk->header.prev = nullptr;
    }

    void free()
    {
        // This is meant only for cleanup, usually at the end of the application life cycle
        freeExtraChunks();
        BumpAllocator(mInitialChunkSize);
    }

    void pushRewindPoint()
    {
        uint8_t* prevPtr = mCurPtr;
        size_t prevTotalUsed = mTotalUsed;
        BumpChunk* prevChunk = mChunk;

        // Tell the allocation function assert that it's okay to allocate this time (for a user, it wouldn't be)
        mRewind++;
        RewindInfo* newRewind = (RewindInfo*)alloc(sizeof(RewindInfo), sizeof(void*));
        mRewind--;

        // When we enter the top-level scope, creating the default chunk for the first time, ensure the
        // first rewind goes back to it since it will be preserved after this.
        if (!prevChunk)
        {
            prevChunk = mChunk;
            prevPtr = &mChunk->data[0];
        }

        newRewind->user = nullptr;
        newRewind->prevChunk = prevChunk;
        newRewind->prevRewind = mRewind;
        newRewind->prevPtr = prevPtr;
        newRewind->prevTotalUsed = prevTotalUsed;
        mRewind = newRewind;
    }

    void rewind()
    {
        MEM_ASSERT(mRewind != nullptr);

        mChunk = mRewind->prevChunk;
        mChunkEnd = mChunk ? (uint8_t*)mChunk + mChunk->header.size : nullptr;
        mCurPtr = mRewind->prevPtr;
        mTotalUsed = mRewind->prevTotalUsed;

        mRewind = mRewind->prevRewind;
    }

    void* getRewindUser() const
    {
#ifdef ENABLE_UNITY_COLLECTIONS_CHECKS
        if (!mRewind)
            return nullptr;
#endif
        return mRewind->user;
    }

    void setRewindUser(void* user)
    {
        MEM_ASSERT(mRewind != nullptr);
        mRewind->user = user;
    }

    uint32_t getTotalUsed() const
    {
        return (uint32_t)mTotalUsed;
    }

    size_t getCapacity() const
    {
        // This is really only used in tests, so it is okay to calculate when needed and not cache it
        BumpChunk* pChunk = mChunk;
        size_t total = 0;
        while (pChunk)
        {
            total += pChunk->header.size;
            pChunk = pChunk->header.prev;
        }
        return (uint32_t)total;
    }

    bool hasRewind() const
    {
        return mRewind != nullptr;
    }

private:
    void newChunk(size_t neededSpace)
    {
        size_t estimatedNeededSize = sizeof(BumpChunkHeader) + neededSpace;
        size_t nextSize = kInitialChunkSize;
        if (mChunk)
        {
            estimatedNeededSize += mChunk->header.size;
            nextSize = mChunk->header.size * 2;
        }

        // Next size will always be a power of 2 and attempt to fit the currently discoverable memory requirements
        while (nextSize < estimatedNeededSize)
            nextSize *= 2;

        BumpChunk* newChunk = mChunk;
        while (newChunk)
        {
            mChunk = newChunk;
            if (newChunk->header.size >= nextSize)
                break;
            newChunk = newChunk->header.next;
        }

        if (!newChunk)
        {
            newChunk = (BumpChunk*)Baselib_Memory_AlignedAllocate(nextSize, sizeof(size_t));
            newChunk->header.next = nullptr;
            newChunk->header.prev = mChunk;
            newChunk->header.size = nextSize;
            if (mChunk)
                mChunk->header.next = newChunk;
        }

        mChunk = newChunk;
        mChunkEnd = (uint8_t*)mChunk + mChunk->header.size;
        mCurPtr = &mChunk->data[0];
    }

    void freeExtraChunks()
    {
        BumpChunk* currChunk = mChunk;
        while (currChunk->header.prev)
            currChunk = currChunk->header.prev;

        // Reset chunk to the default/base chunk
        mChunk = currChunk;
        mChunkEnd = (uint8_t*)mChunk + mChunk->header.size;
        mCurPtr = &mChunk->data[0];

        // Free all next chunks
        currChunk = currChunk->header.next;
        while (currChunk) {
            BumpChunk* next = currChunk->header.next;
            Baselib_Memory_AlignedFree(currChunk);
            currChunk = next;
        }

        mChunk->header.next = nullptr;
    }

private:
    BumpChunk* mChunk{ nullptr };
    uint8_t* mChunkEnd{ nullptr };
    uint8_t* mCurPtr{ nullptr };
    size_t mTotalUsed{ 0 };
    RewindInfo* mRewind{ nullptr };
    constexpr static size_t kInitialChunkSize{ 1u << 14 };
};
