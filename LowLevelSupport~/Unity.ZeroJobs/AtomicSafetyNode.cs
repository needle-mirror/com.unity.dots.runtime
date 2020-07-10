#if ENABLE_UNITY_COLLECTIONS_CHECKS

using System.Runtime.InteropServices;
using System;
using Unity.Jobs;
using UnityEngine.Assertions;
using System.Diagnostics;

namespace Unity.Collections.LowLevel.Unsafe
{
    internal struct AtomicSafetyNodeFlags
    {
        internal const uint AllowSecondaryWriting = 1 << 0;
        internal const uint IsInit = 1 << 1;
        internal const uint AllowDispose = 1 << 2;
        internal const uint BumpSecondaryVersionOnScheduleWrite = 1 << 3;
        internal const uint Magic = ((1u << 28) - 1) << 4;
    }

    // Permission flags are guards. If the flag is set, the node is protected from doing
    // that operation. I.e. for read only, Write+Dispose should be set.
    internal struct AtomicSafetyNodeVersionMask
    {
        internal const int ReadProtect = 1 << 0;
        internal const int WriteProtect = 1 << 1;
        internal const int DisposeProtect = 1 << 2;
        internal const int ReadWriteProtect = ReadProtect | WriteProtect;
        internal const int ReadWriteDisposeProtect = ReadProtect | WriteProtect | DisposeProtect;

        internal const int ReadUnprotect = ~ReadProtect;
        internal const int WriteUnprotect = ~WriteProtect;
        internal const int DisposeUnprotect = ~DisposeProtect;
        internal const int ReadWriteUnprotect = ~ReadWriteProtect;
        internal const int ReadWriteDisposeUnprotect = ~ReadWriteDisposeProtect;

        internal const int VersionAndReadProtect = ~(WriteProtect | DisposeProtect);
        internal const int VersionAndWriteProtect = ~(ReadProtect | DisposeProtect);
        internal const int VersionAndDisposeProtect = ~(ReadProtect | WriteProtect);

        internal const int SecondaryVersion = 1 << 3;   // Track here rather than with pointer alignment as in Big Unity

        internal const int VersionInc = 1 << 4;
    }

    [StructLayout(LayoutKind.Sequential)]
    [DebuggerDisplay(
        "Job name = {new string(Unity.Development.JobsDebugger.JobNames.NameBlobPtr + jobNameOffset)}, " +
        "Field name = {new string(Unity.Development.JobsDebugger.JobNames.NameBlobPtr + fieldNameOffset)}"
        )]
    public unsafe struct BufferDebugData
    {
        // The following are provided in post processing since we don't need multiple copies of job names (reflection replacement)
        public JobHandle fence;
        public int wasScheduledWithSecondaryBuffer;
        public int jobNameOffset;
        public int fieldNameOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct AtomicSafetyNode
    {
        private IntPtr m_BaselibNodeNext;  // This allows us to pretend this struct inherits from mpmc_node<pointer> in native code
        internal int version0;
        internal int version1;
        internal uint flags;

        internal BufferDebugData writer;
        internal BufferDebugData* readers;
        internal int readerCount;
        internal int readerCapacity;

        internal void Init()
        {
            if ((flags & AtomicSafetyNodeFlags.IsInit) == 0)
            {
                version0 = 0;
                version1 = AtomicSafetyNodeVersionMask.SecondaryVersion;
                flags = AtomicSafetyNodeFlags.Magic | AtomicSafetyNodeFlags.IsInit;
            }
            flags |= AtomicSafetyNodeFlags.AllowDispose;
            flags |= AtomicSafetyNodeFlags.AllowSecondaryWriting;
            flags &= ~AtomicSafetyNodeFlags.BumpSecondaryVersionOnScheduleWrite;
            readers = null;
            readerCount = 0;
            readerCapacity = 0;
            writer = new BufferDebugData();

            // If this fails, we probably released an AtomicSafetyNodePatched
            Assert.IsTrue((flags & AtomicSafetyNodeFlags.Magic) == AtomicSafetyNodeFlags.Magic);

            // If these fail then this is an AtomicSafetyNode which was previously used and released by AtomicSafetyHandle.Release
            // but a dangling pointer to it still existed and had protections modified
            Assert.IsTrue((version0 & AtomicSafetyNodeVersionMask.ReadWriteDisposeProtect) == 0);
            Assert.IsTrue((version1 & AtomicSafetyNodeVersionMask.ReadWriteDisposeProtect) == 0);
        }

        internal unsafe BufferDebugData* AddReader()
        {
            readerCount++;
            if (readerCount > readerCapacity)
            {
                readerCapacity++;
                readers = (BufferDebugData*)UnsafeUtility.Realloc((void*)readers, readerCapacity * sizeof(BufferDebugData), 0, Allocator.Persistent);
            }
            return &readers[readerCount - 1];
        }

        internal void FreeDebugInfo()
        {
            writer = new BufferDebugData();
            UnsafeUtility.Free((void*)readers, Allocator.Persistent);
            readers = null;
            readerCount = 0;
            readerCapacity = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AtomicSafetyNodePatched
    {
        private IntPtr m_BaselibNodeNext;  // Unused but needed to match layout of AtomicSafetyNode
        internal int version0;
        internal int version1;
        internal uint flags;
        internal unsafe AtomicSafetyNode* originalNode;
    }
}

#endif // ENABLE_UNITY_COLLECTIONS_CHECKS
