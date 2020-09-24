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

    public unsafe struct PadBufferDebugData
    {
        public JobHandle pad0;
        public int pad1;
        public int pad2;
        public int pad3;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct AtomicSafetyNode
    {
        private void* vPtr;
        internal int version0;
        internal int version1;
        internal uint flags;

        internal PadBufferDebugData pad0;
        internal void* pad1;
        internal int pad2;
        internal int pad3;
    }

}

#endif // ENABLE_UNITY_COLLECTIONS_CHECKS
