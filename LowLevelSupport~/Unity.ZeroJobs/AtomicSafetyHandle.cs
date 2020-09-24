#if ENABLE_UNITY_COLLECTIONS_CHECKS

using System.Runtime.InteropServices;
using System;
using System.Runtime.CompilerServices;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Development.JobsDebugger;
using Unity.Core;
using UnityEngine.Assertions;
using Unity.Burst;

namespace Unity.Collections.LowLevel.Unsafe
{
    public enum EnforceJobResult
    {
        AllJobsAlreadySynced = 0,
        DidSyncRunningJobs = 1,
        HandleWasAlreadyDeallocated = 2,
    }

    public enum AtomicSafetyErrorType
    {
        /// <summary>
        ///   <para>Corresponds to an error triggered when the object protected by this AtomicSafetyHandle is accessed on the main thread after it is deallocated.</para>
        /// </summary>
        Deallocated,
        /// <summary>
        ///   <para>Corresponds to an error triggered when the object protected by this AtomicSafetyHandle is accessed by a worker thread after it is deallocated.</para>
        /// </summary>
        DeallocatedFromJob,
        /// <summary>
        ///   <para>Corresponds to an error triggered when the object protected by this AtomicSafetyHandle is accessed by a worker thread before it is allocated.</para>
        /// </summary>
        NotAllocatedFromJob,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AtomicSafetyHandle
    {
#if UNITY_WINDOWS
        public const string nativejobslib = "nativejobs";
#else
        public const string nativejobslib = "libnativejobs";
#endif

        internal unsafe AtomicSafetyNode* nodePtr;
        internal int version;
        internal int staticSafetyId;
        internal unsafe void* nodeLocalPtr;

        //---------------------------------------------------------------------------------------------------
        // Basic lifetime management
        //---------------------------------------------------------------------------------------------------

        [DllImport(nativejobslib)]
        private static extern unsafe void AtomicSafetyHandle_Initialize(void* nameBlobPtr);
        [DllImport(nativejobslib)]
        private static extern unsafe void AtomicSafetyHandle_Shutdown();
        [DllImport(nativejobslib)]
        private static extern unsafe void AtomicSafetyHandle_Create(void* ash);
        [DllImport(nativejobslib)]
        private static extern unsafe void AtomicSafetyHandle_Release(void* ash);

        [DllImport(nativejobslib)]
        private static extern unsafe void AtomicSafetyHandle_Unpatch(void* ash);
        [DllImport(nativejobslib)]
        private static extern unsafe void AtomicSafetyHandle_CheckWriteAndThrow(void* ash);
        [DllImport(nativejobslib)]
        private static extern unsafe void AtomicSafetyHandle_CheckReadAndThrow(void* ash);
        [DllImport(nativejobslib)]
        private static extern unsafe void AtomicSafetyHandle_CheckDisposeAndThrow(void* ash);
        [DllImport(nativejobslib)]
        private static extern unsafe void AtomicSafetyHandle_CheckGetSecondaryDataPointerAndThrow(void* ash);
        [DllImport(nativejobslib)]
        private static extern unsafe void AtomicSafetyHandle_SetAllowSecondaryVersionWriting(void* ash, int allowWriting);
        [DllImport(nativejobslib)]
        private static extern unsafe int AtomicSafetyHandle_EnforceAllBufferJobsHaveCompletedAndRelease(void* ash);
        [DllImport(nativejobslib)]
        private static extern unsafe int AtomicSafetyHandle_EnforceAllBufferJobsHaveCompleted(void* ash);
        [DllImport(nativejobslib)]
        private static extern unsafe int AtomicSafetyHandle_GetReaderArray(void* ash, int maxCount, void* handles);
        [DllImport(nativejobslib)]
        private static extern unsafe void AtomicSafetyHandle_GetWriter(void* ash, void* jobHandle);
        [DllImport(nativejobslib)]
        private static extern unsafe char* AtomicSafetyHandle_GetReaderName(void* ash, int readerIndex);
        [DllImport(nativejobslib)]
        private static extern unsafe char* AtomicSafetyHandle_GetWriterName(void* ash);
        [DllImport(nativejobslib)]
        private static extern unsafe void AtomicSafetyHandle_PatchLocal(void* ash);
        [DllImport(nativejobslib)]
        private static extern unsafe void AtomicSafetyHandle_GetTempUnsafePtrSliceHandle(void* handle);
        [DllImport(nativejobslib)]
        internal static extern unsafe void AtomicSafetyHandle_SetBatchScheduler(void* scheduler);

        public static void Initialize()
        {
            unsafe
            {
                AtomicSafetyHandle_Initialize(JobNames.NameBlobPtr);
            }
        }

        public unsafe static void Shutdown()
        {
            AtomicSafetyHandle_Shutdown();
        }
        public unsafe static AtomicSafetyHandle GetTempUnsafePtrSliceHandle()
        {
            AtomicSafetyHandle handle = default;
            AtomicSafetyHandle_GetTempUnsafePtrSliceHandle(&handle);
            return handle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsTempUnsafePtrSliceHandle(AtomicSafetyHandle handle)
        {
            unsafe
            {
                return handle.nodePtr == GetTempUnsafePtrSliceHandle().nodePtr;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AtomicSafetyHandle GetTempMemoryHandle()
        {
            return TempMemoryScope.GetSafetyHandle();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsTempMemoryHandle(AtomicSafetyHandle handle)
        {
            unsafe
            {
                return handle.nodePtr == TempMemoryScope.GetSafetyHandle().nodePtr;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe AtomicSafetyHandle Create()
        {
            AtomicSafetyHandle handle = default;
            AtomicSafetyHandle_Create(&handle);
            return handle;

// TODO: fix in future
#if UNITY_DOTSRUNTIME_TRACEMALLOCS
            // This is likely a very different callstack than when we first Malloc'd the AtomicSafetyNode.
            // To help track leaks, update it.
            UnsafeUtility.DebugReuseAllocation(nodePtr);
#endif
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static void Release(AtomicSafetyHandle handle)
        {
            AtomicSafetyHandle_Release(&handle);
            ExceptionReporter.Check();
        }
        //---------------------------------------------------------------------------------------------------
        // Quick tests (often used to avoid executing much slower test code)
        //---------------------------------------------------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal unsafe bool IsValid() => (nodePtr != null) &&
            (version == (UncheckedGetNodeVersion() & AtomicSafetyNodeVersionMask.ReadWriteDisposeUnprotect));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool IsAllowedToWrite() => (nodePtr != null) &&
            (version == (UncheckedGetNodeVersion() & AtomicSafetyNodeVersionMask.VersionAndWriteProtect));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool IsAllowedToRead() => (nodePtr != null) &&
            (version == (UncheckedGetNodeVersion() & AtomicSafetyNodeVersionMask.VersionAndReadProtect));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool IsAllowedToDispose() => (nodePtr != null) &&
            (version == (UncheckedGetNodeVersion() & AtomicSafetyNodeVersionMask.VersionAndDisposeProtect));

        //---------------------------------------------------------------------------------------------------
        // Externally used by owners of safety handles to setup safety handles
        //---------------------------------------------------------------------------------------------------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe int UncheckedGetNodeVersion() =>
            (version & AtomicSafetyNodeVersionMask.SecondaryVersion) == AtomicSafetyNodeVersionMask.SecondaryVersion ?
            nodePtr->version1 : nodePtr->version0;

        // Switches the AtomicSafetyHandle to the secondary version number
        // Also clears protections
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void UncheckedUseSecondaryVersion()
        {
            if (UncheckedIsSecondaryVersion())
                throw new InvalidOperationException("Already using secondary version");
            version = nodePtr->version1 & AtomicSafetyNodeVersionMask.ReadWriteDisposeUnprotect;
        }
        public static unsafe void UseSecondaryVersion(ref AtomicSafetyHandle handle)
        {
            handle.UncheckedUseSecondaryVersion();
        }

        // Sets whether the secondary version is readonly (allowWriting = false) or readwrite (allowWriting= true)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void SetAllowSecondaryVersionWriting(bool allowWriting)
        {
            unsafe
            {
                var node = GetInternalNode();
                if (node == null)
                    throw new InvalidOperationException("Node is not valid in SetAllowSecondaryVersionWriting");

                // This logic is not obvious. For explanation, see comments at top of file.
                node->version1 |= AtomicSafetyNodeVersionMask.WriteProtect;
                if (allowWriting)
                    node->flags |= AtomicSafetyNodeFlags.AllowSecondaryWriting;
                else
                    node->flags &= ~AtomicSafetyNodeFlags.AllowSecondaryWriting;
            }
        }

        public static void SetAllowSecondaryVersionWriting(AtomicSafetyHandle handle, bool allowWriting)
        {
            handle.SetAllowSecondaryVersionWriting(allowWriting);
        }

        // Sets whether the secondary version is readonly (allowWriting = false) or readwrite (allowWriting= true)
        // "bump" means increment.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetBumpSecondaryVersionOnScheduleWrite(bool bump)
        {
            unsafe
            {
                var node = GetInternalNode();
                if (node == null)
                    throw new InvalidOperationException("Node is not valid in SetBumpSecondaryVersionOnScheduleWrite");
                if (bump)
                    node->flags |= AtomicSafetyNodeFlags.BumpSecondaryVersionOnScheduleWrite;
                else
                    node->flags &= ~AtomicSafetyNodeFlags.BumpSecondaryVersionOnScheduleWrite;
            }
        }

        public static void SetBumpSecondaryVersionOnScheduleWrite(AtomicSafetyHandle handle, bool bump)
        {
            handle.SetBumpSecondaryVersionOnScheduleWrite(bump);
        }

        //---------------------------------------------------------------------------------------------------
        // Called either directly or indirectly by CodeGen only
        //---------------------------------------------------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void PatchLocal(ref AtomicSafetyHandle handle)
        {
            fixed (AtomicSafetyHandle* ptr = &handle)
            {
                AtomicSafetyHandle_PatchLocal(ptr);
                ExceptionReporter.Check();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void PatchLocalReadOnly(ref AtomicSafetyHandle handle)
        {
            PatchLocal(ref handle);
            unsafe
            {
                // There can be default initialized safety handles (with no safety node) if the container is default initialized
                // in a job with complex logic which accounts for an empty container
                if (handle.nodePtr == null)
                    return;
                handle.nodePtr->version0 = (handle.nodePtr->version0 & AtomicSafetyNodeVersionMask.ReadUnprotect) | AtomicSafetyNodeVersionMask.WriteProtect | AtomicSafetyNodeVersionMask.DisposeProtect;
                handle.nodePtr->version1 = (handle.nodePtr->version1 & AtomicSafetyNodeVersionMask.ReadUnprotect) | AtomicSafetyNodeVersionMask.WriteProtect | AtomicSafetyNodeVersionMask.DisposeProtect;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void PatchLocalWriteOnly(ref AtomicSafetyHandle handle)
        {
            PatchLocal(ref handle);
            unsafe
            {
                // There can be default initialized safety handles (with no safety node) if the container is default initialized
                // in a job with complex logic which accounts for an empty container
                if (handle.nodePtr == null)
                    return;
                handle.nodePtr->version0 = (handle.nodePtr->version0 & AtomicSafetyNodeVersionMask.WriteUnprotect) | AtomicSafetyNodeVersionMask.ReadProtect | AtomicSafetyNodeVersionMask.DisposeProtect;
                handle.nodePtr->version1 = (handle.nodePtr->version1 & AtomicSafetyNodeVersionMask.WriteUnprotect) | AtomicSafetyNodeVersionMask.ReadProtect | AtomicSafetyNodeVersionMask.DisposeProtect;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void PatchLocalReadWrite(ref AtomicSafetyHandle handle)
        {
            PatchLocal(ref handle);
            unsafe
            {
                // There can be default initialized safety handles (with no safety node) if the container is default initialized
                // in a job with complex logic which accounts for an empty container
                if (handle.nodePtr == null)
                    return;
                handle.nodePtr->version0 = (handle.nodePtr->version0 & AtomicSafetyNodeVersionMask.ReadWriteUnprotect) | AtomicSafetyNodeVersionMask.DisposeProtect;
                handle.nodePtr->version1 = (handle.nodePtr->version1 & AtomicSafetyNodeVersionMask.ReadWriteUnprotect) | AtomicSafetyNodeVersionMask.DisposeProtect;
            }
        }

        public static unsafe void PatchLocalDynamic(ref AtomicSafetyHandle firstHandle, int handleCountReadOnly, int handleCountWritable, int handleCountForceReadOnly, int handleCountForceWritable)
        {
            // If a container or resource is [ReadOnly] then the read/write safety handle count will be forced read only
            int countRead = handleCountReadOnly + handleCountForceReadOnly;

            // If a container or resource is has safety disabled, then the read/write safety handle count will be forced writable
            int countWritable = handleCountWritable + handleCountForceWritable;

            fixed (AtomicSafetyHandle* firstHandlePtr = &firstHandle)
            {
                for (int i = 0; i < countRead; i++)
                    PatchLocalReadOnly(ref firstHandlePtr[i]);
                int countTotal = countRead + countWritable;
                for (int i = countRead; i < countTotal; i++)
                    PatchLocalReadWrite(ref firstHandlePtr[i]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // FIXME check interface gen
        // FIXME This should be Release() (native version no parameter)
        public static unsafe void Unpatch(ref AtomicSafetyHandle handle)
        {
            // Handle wasn't created and therefore never patched
            fixed (AtomicSafetyHandle* ash = &handle)
            {
                AtomicSafetyHandle_Unpatch(ash);
            }
        }

        //---------------------------------------------------------------------------------------------------
        // JobsDebugger safety checks usage (may be used internally as well)
        //---------------------------------------------------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe AtomicSafetyNode* GetInternalNode()
        {
            if (!IsValid())
                return null;
            if ((nodePtr->flags & AtomicSafetyNodeFlags.Magic) == AtomicSafetyNodeFlags.Magic)
                return nodePtr;
            throw new InvalidOperationException("AtomicSafetyNode has either been corrupted or is being accessed on a job which is not allowed");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool IsDefaultValue() => version == 0 && nodePtr == null;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool UncheckedIsSecondaryVersion() =>
            (version & AtomicSafetyNodeVersionMask.SecondaryVersion) == AtomicSafetyNodeVersionMask.SecondaryVersion;

        public unsafe int GetReaderArray(int maxCount, JobHandle* handles)
        {
            int result = 0;
            fixed (AtomicSafetyHandle* ash = &this)
            {
                result = AtomicSafetyHandle_GetReaderArray(ash, maxCount, handles);
            }
            ExceptionReporter.Check();
            return result;
        }
        public unsafe static int GetReaderArray(AtomicSafetyHandle handle, int maxCount, IntPtr handles)
        {
            return handle.GetReaderArray(maxCount, (JobHandle*)handles);
        }

        public unsafe JobHandle GetWriter()
        {
            JobHandle handle = default;
            fixed (AtomicSafetyHandle* ash = &this)
            {
                AtomicSafetyHandle_GetWriter(ash, &handle);
            }
            ExceptionReporter.Check();
            return handle;
        }

        public static JobHandle GetWriter(AtomicSafetyHandle handle)
        {
            return handle.GetWriter();
        }

        public static unsafe string GetReaderName(AtomicSafetyHandle handle, int readerIndex)
        {
            char* c = AtomicSafetyHandle_GetReaderName(&handle, readerIndex);
            ExceptionReporter.Check();
            return new string(c);
        }

        public static unsafe string GetWriterName(AtomicSafetyHandle handle)
        {
            char* c = AtomicSafetyHandle_GetWriterName(&handle);
            ExceptionReporter.Check();
            return new string(c);
        }

        public static unsafe int NewStaticSafetyId(byte* ownerTypeNameBytes, int byteCount)
        {
            return 0;// StaticSafetyIdHashTable.CreateOrGetSafetyId(ownerTypeNameBytes, byteCount);
        }

        public static unsafe int NewStaticSafetyId<T>()
        {
            return 0;  // ID for unknown object type
        }

        public static void SetStaticSafetyId(ref AtomicSafetyHandle handle, int staticSafetyId)
        {
            //handle.staticSafetyId = staticSafetyId;
        }

        public static unsafe void SetCustomErrorMessage(int staticSafetyId, AtomicSafetyErrorType errorType, byte* messageBytes, int byteCount)
        {
            // temporary stub to support additions in UnityEngine
        }

        public static unsafe byte* GetOwnerTypeName(AtomicSafetyHandle handle, byte* defaultName)
        {
            // temporary stub to support additions in UnityEngine
            return null;
        }

        public static unsafe byte* GetCustomErrorMessage(AtomicSafetyHandle handle, AtomicSafetyErrorType errorType, byte* defaultMsg)
        {
            // temporary stub to support additions in UnityEngine
            return null;
        }

        //---------------------------------------------------------------------------------------------------
        // Should be in JobsDebugger namespace or something because they know both control jobs and safety handles
        //---------------------------------------------------------------------------------------------------

        public static unsafe EnforceJobResult EnforceAllBufferJobsHaveCompleted(AtomicSafetyHandle handle)
        {
            var result = (EnforceJobResult) AtomicSafetyHandle_EnforceAllBufferJobsHaveCompleted(&handle);
            ExceptionReporter.Check();
            return result;
        }
        public unsafe static EnforceJobResult EnforceAllBufferJobsHaveCompletedAndRelease(AtomicSafetyHandle handle)
        {
            var result = (EnforceJobResult) AtomicSafetyHandle_EnforceAllBufferJobsHaveCompletedAndRelease(&handle);
            ExceptionReporter.Check();
            return result;
        }



        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static void CheckWriteAndThrow(AtomicSafetyHandle handle)
        {
            AtomicSafetyHandle_CheckWriteAndThrow(&handle);
            ExceptionReporter.Check();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static void CheckReadAndThrow(AtomicSafetyHandle handle)
        {
            AtomicSafetyHandle_CheckReadAndThrow(&handle);
            ExceptionReporter.Check();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static void CheckDisposeAndThrow(AtomicSafetyHandle handle)
        {
            AtomicSafetyHandle_CheckDisposeAndThrow(&handle);
            ExceptionReporter.Check();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckDeallocateAndThrow(AtomicSafetyHandle handle)
        {
            CheckDisposeAndThrow(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static void CheckGetSecondaryDataPointerAndThrow(AtomicSafetyHandle handle)
        {
            AtomicSafetyHandle_CheckGetSecondaryDataPointerAndThrow(&handle);
            ExceptionReporter.Check();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckWriteAndBumpSecondaryVersion(AtomicSafetyHandle handle)
        {
            Assert.IsFalse(handle.UncheckedIsSecondaryVersion());

            if (!handle.IsAllowedToWrite())
                CheckWriteAndThrow(handle);
            unsafe
            {
                handle.nodePtr->version1 += AtomicSafetyNodeVersionMask.VersionInc;
                Assert.IsTrue((handle.nodePtr->version0 & AtomicSafetyNodeVersionMask.ReadWriteProtect) == (handle.nodePtr->version1 & AtomicSafetyNodeVersionMask.ReadWriteProtect));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckExistsAndThrow(AtomicSafetyHandle handle)
        {
            if (!handle.IsValid())
                throw new InvalidOperationException("The safety handle is no longer valid -- a native container or other protected resource has been deallocated");
        }
    }
}

#endif // ENABLE_UNITY_COLLECTIONS_CHECKS
