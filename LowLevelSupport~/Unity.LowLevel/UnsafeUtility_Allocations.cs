using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Unity.Collections.LowLevel.Unsafe
{
    public partial class UnsafeUtility
    {

#if UNITY_DOTSRUNTIME_TRACEMALLOCS
        private static Dictionary<IntPtr, string> s_Allocs = new Dictionary<IntPtr, string>();

        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_malloc")]
        private static extern unsafe void* MallocInternal(long totalSize, int alignOf, Allocator allocator);
        public static unsafe void* Malloc(long totalSize, int alignOf, Allocator allocator)
        {
            void* ptr = MallocInternal(totalSize, alignOf, allocator);
            if (ptr != null && allocator != Allocator.Temp)
                s_Allocs[(IntPtr)ptr] = Environment.StackTrace;
            return ptr;
        }

        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_realloc")]
        private static extern unsafe void* ReallocInternal(void* oldMem, long newSize, int alignOf, Allocator allocator);
        public static unsafe void* Realloc(void* oldMem, long newSize, int alignOf, Allocator allocator)
        {
            if (oldMem != null && allocator != Allocator.Temp)
                s_Allocs.Remove((IntPtr)oldMem);
            void* ptr = ReallocInternal(oldMem, newSize, alignOf, allocator);
            if (ptr != null && allocator != Allocator.Temp)
                s_Allocs[(IntPtr)ptr] = Environment.StackTrace;
            return ptr;
        }

        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_free")]
        private static extern unsafe void FreeInternal(void* mBuffer, Allocator allocator);
        public static unsafe void Free(void* mBuffer, Allocator allocator)
        {
            if (mBuffer != null && allocator != Allocator.Temp)
                s_Allocs.Remove((IntPtr)mBuffer);
            FreeInternal(mBuffer, allocator);
        }

        public static unsafe Dictionary<string, int> DebugGetAllocationsByCount()
        {
            Dictionary<string, int> ret = new Dictionary<string, int>();
            foreach (var pair in s_Allocs)
            {
                ret.TryGetValue(pair.Value, out int curr);
                ret[pair.Value] = curr + 1;
            }

            return ret;
        }

        public static unsafe void DebugReuseAllocation(void* mem)
        {
            if (mem != null)
            {
                s_Allocs[(IntPtr)mem] = Environment.StackTrace;
            }
        }
#else
        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_malloc")]
        public static extern unsafe void* Malloc(long totalSize, int alignOf, Allocator allocator);

        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_realloc")]
        public static extern unsafe void* Realloc(void* oldMem, long newSize, int alignOf, Allocator allocator);

        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_free")]
        public static extern unsafe void Free(void* mBuffer, Allocator allocator);
#endif


        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_memcpy")]
        public static extern unsafe void MemCpy(void* dst, void* src, long n);

        // Debugging. Checks the heap guards on the requested memory.
        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_assertheap")]
        public static extern unsafe void AssertHeap(void* dst);

        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_memset")]
        public static extern unsafe void MemSet(void* destination, byte value, long size);

        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_memclear")]
        public static extern unsafe void MemClear(void* mBuffer, long size);

        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_memcpystride")]
        public static extern unsafe void MemCpyStride(void* destination, int destinationStride, void* source, int sourceStride, int elementSize, long count);

        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_memcmp")]
        public static extern unsafe int MemCmp(void* ptr1, void* ptr2, long size);

        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_memcpyreplicate")]
        public static extern unsafe void MemCpyReplicate(void* destination, void* source, int size, int count);

        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_memmove")]
        public static extern unsafe void MemMove(void* destination, void* source, long size);

        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_freetemp")]
        public static extern unsafe void FreeTempMemory();

        // The CallFunctionPtr_abc methods are used to call static code-gen methods.
        // If we remove the minimal path for ST, these could be removed.
        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_call_p")]
        public static extern unsafe void CallFunctionPtr_p(void* fnc, void* data);

        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_call_pp")]
        public static extern unsafe void CallFunctionPtr_pp(void* fnc, void* data1, void* data2);

        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_call_pi")]
        public static extern unsafe void CallFunctionPtr_pi(void* fnc, void* data, int param0);

        // Temp never shrinks. TempJob and Persistent return the current heap size.
        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_get_heap_size")]
        internal static extern long GetHeapSize(Allocator allocator);

        // We need a shared pointer with Burst; however, lowlevel doesn't have access to Burst to
        // use the typical machinery. Until (if?) that is solved, we need a workaround to pass
        // a static flag to bursted code.
        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_get_in_job")]
        internal static extern int GetInJob();

        [DllImport("lib_unity_lowlevel", EntryPoint = "unsafeutility_set_in_job")]
        internal static extern void SetInJob(int inJob);

        public static bool IsValidAllocator(Allocator allocator) { return allocator > Allocator.None; }
    }
}
