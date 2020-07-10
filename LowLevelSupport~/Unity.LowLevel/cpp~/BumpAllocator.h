#pragma once

#include <memory>
#include <stdlib.h>

struct BumpChunk;

struct BumpChunkHeader
{
    BumpChunk* next;
    size_t size;  // size includes the header
};

struct BumpChunk
{
    BumpChunkHeader header;
    uint8_t data[1];
};

class BumpAllocator
{
public:
    void* alloc(size_t size, size_t alignment)
    {
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
            return ret;

        newChunk(size + alignment);
        return alloc(size, alignment);
    }

    void reset()
    {
        // Instead of freeing memory, we just reset the pointer when we're done with all the memory
        mCurPtr = &mChunk->data[0];

        // Instead of a chunk reuse strategy for the previously allocated smaller chunks, we just free
        // them with the idea that eventually we have one chunk that is large enough to hold all temp allocations.
        //
        // Do this instead of realloc'ing so there is no performance hit during the first frame (or whenever) while
        // we work out the appropriate bump allocator reserved size.
        freeBlocks(mChunk->header.next);
        mChunk->header.next = nullptr;
    }

    void free()
    {
        // This is meant only for cleanup, usually at the end of the application life cycle
        freeBlocks(mChunk);
        BumpAllocator(mInitialChunkSize);
    }

private:
    void newChunk(size_t neededSpace) {
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

        BumpChunk* newChunk = (BumpChunk*)Baselib_Memory_AlignedAllocate(nextSize, sizeof(size_t));
        newChunk->header.next = mChunk;
        newChunk->header.size = nextSize;

        mChunk = newChunk;
        mChunkEnd = (uint8_t*)mChunk + nextSize;
        mCurPtr = &mChunk->data[0];
    }

    void freeBlocks(BumpChunk* startBlock)
    {
        while (startBlock) {
            BumpChunk* next = startBlock->header.next;
            Baselib_Memory_AlignedFree(startBlock);
            startBlock = next;
        }
    }

private:
    BumpChunk* mChunk{ nullptr };
    uint8_t* mChunkEnd{ nullptr };
    uint8_t* mCurPtr{ nullptr };
    constexpr static size_t kInitialChunkSize{ 1u << 14 };
};
