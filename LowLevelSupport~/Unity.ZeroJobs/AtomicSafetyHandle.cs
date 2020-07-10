#if ENABLE_UNITY_COLLECTIONS_CHECKS

using System.Runtime.InteropServices;
using System;
using System.Runtime.CompilerServices;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Core;
using UnityEngine.Assertions;
using Unity.Burst;

namespace Unity.Collections.LowLevel.Unsafe
{
    // AtomicSafetyHandle is used by the C# job system to provide validation and full safety
    // for read / write permissions to access the buffers represented by each handle.
    // Each AtomicSafetyHandle represents a single container struct (or resource).
    // Since all Native containers are written using structs,
    // it also provides checks against destroying a container
    // and accessing from another struct pointing to the same buffer.
    //
    // AtomicSafetyNodes represent the actual state of a valid AtomicSafetyHandle.
    // They are associated with a container's allocated memory buffer. Because Native
    // Containers are copyable structs, there can be multiple copies which point to 
    // the same underlying memory.
    // If they become out-of-sync (tracked by version increments), the AtomicSafetyHandle
    // is invalid. Checking for this is safe and will never be a memory access error because 
    // once allocated, AtomicSafetyNodes live to the end of the application's life. Released
    // AtomicSafetyNodes are recycled via free-list.
    //
    // The key to setting permissions in the AtomicSafetyHandles lies in attributes
    // set for the Native Containers in C#. AtomicSafetyNodes permissions were patched at runtime
    // using reflection in Big Unity, but with DOTS Runtime, they will have to be patched
    // at compile time using IL code generation. Containers also may manually set read/write only.
    //
    // IMPL NOTE 1: One tricky behavior to note is that when we call Check***AndThrow, if the handle doesn't
    // provide access, yet that REASON to throw can't be reasoned about, the handle will unprotect
    // that relevant access and continue execution.
    //
    // AtomicSafetyNodes actually track two version numbers. It allows NativeList cast to NativeArray, so the
    // NativeList can continue to be resized dynamically (which invalidates the version in the NativeArray
    // using the secondary version in the node).
    //
    // IMPL NOTE 2: Another tricky behavior is the presense of AllowSecondaryWriting as well as WriteProtect
    // in the node's flag and secondary version, respectively. The idea is that WriteProtect enforces protection,
    // but AllowSecondaryWriting will keep the CheckWriteAndThrow function from auto-enabling write, as
    // described in IMPL NOTE 1 above. These Check***AndThrow functions are responsible for most of the
    // transitions between setting and unsetting protection to the SafetyHandles and underlying nodes.
    //
    // Differences from CPP impl. in Big Unity
    // - PrepareUndisposable is not implemented. Not actually used in Big Unity or DOTS.
    // - AllowReadOrWrite flag removed. Not used in Big Unity or DOTS.

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
        // Helper class to allow burstable static slice handle
        private static class AtomicSliceHandle
        {
            public struct SliceHandleKey { };
            public static readonly SharedStatic<AtomicSafetyHandle> s_SliceHandle = SharedStatic<AtomicSafetyHandle>.GetOrCreate<SliceHandleKey>();
        }


        [DllImport("lib_unity_zerojobs")]
        private static extern unsafe void AtomicSafety_PushNode(void* node);

        [DllImport("lib_unity_zerojobs")]
        private static extern unsafe void* AtomicSafety_PopNode();

        internal unsafe AtomicSafetyNode* nodePtr;
        internal int version;
        internal int staticSafetyId;

        // This is used in a job instead of the shared node, since different jobs may enforce
        // different access to memory/object protected by the safety handle, and once we have
        // verified the job can safely access it without race conditions etc., it should maintain
        // it's own copy of required permissions in that moment for checking with actual code
        // which accesses that memory/object.
        internal unsafe AtomicSafetyNodePatched* nodeLocalPtr;


        //---------------------------------------------------------------------------------------------------
        // Basic lifetime management
        //---------------------------------------------------------------------------------------------------

        public static void Initialize()
        {
            unsafe
            {
                // Keep from initializing twice
                if (AtomicSliceHandle.s_SliceHandle.Data.nodePtr != null)
                    return;

                AtomicSliceHandle.s_SliceHandle.Data = Create();
                AtomicSliceHandle.s_SliceHandle.Data.SetAllowSecondaryVersionWriting(false);
            }
        }

        public unsafe static void Shutdown()
        {
            // Protect from multiple shutdown
            if (AtomicSliceHandle.s_SliceHandle.Data.nodePtr == null)
                return;

            Release(AtomicSliceHandle.s_SliceHandle.Data);
            AtomicSliceHandle.s_SliceHandle.Data.nodePtr = null;

            var nodePtr = (AtomicSafetyNode*)AtomicSafety_PopNode();
            while (nodePtr != null)
            {
                UnsafeUtility.Free(nodePtr, Allocator.Persistent);
                nodePtr = (AtomicSafetyNode*)AtomicSafety_PopNode();
            }
        }

        public static AtomicSafetyHandle GetTempUnsafePtrSliceHandle()
        {
            return AtomicSliceHandle.s_SliceHandle.Data;
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
            AtomicSafetyHandle handle = new AtomicSafetyHandle();

            var nodePtr = (AtomicSafetyNode*)AtomicSafety_PopNode();
            if (nodePtr == null)
            {
                nodePtr = (AtomicSafetyNode*)UnsafeUtility.Malloc(sizeof(AtomicSafetyNode), 0, Allocator.Persistent);
                *nodePtr = new AtomicSafetyNode();
            }
#if UNITY_DOTSRUNTIME_TRACEMALLOCS
            // This is likely a very different callstack than when we first Malloc'd the AtomicSafetyNode.
            // To help track leaks, update it.
            UnsafeUtility.DebugReuseAllocation(nodePtr);
#endif
            nodePtr->Init();

            handle.nodePtr = nodePtr;
            handle.version = nodePtr->version0;
            handle.staticSafetyId = 0;

            return handle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Release(AtomicSafetyHandle handle)
        {
            unsafe
            {
                // Can throw if corrupted or unallowed job
                // Otherwise return null if released already (based on version mismatch), where we will throw
                AtomicSafetyNode* node = handle.GetInternalNode();
                if (node == null)
                    throw new InvalidOperationException("The Handle has already been released");

                // Clear all protections and increment version to protect from any other remaining AtomicSafetyHandles
                node->version0 = (node->version0 & AtomicSafetyNodeVersionMask.ReadWriteDisposeUnprotect) + AtomicSafetyNodeVersionMask.VersionInc;
                node->version1 = (node->version1 & AtomicSafetyNodeVersionMask.ReadWriteDisposeUnprotect) + AtomicSafetyNodeVersionMask.VersionInc;
                node->FreeDebugInfo();
                AtomicSafety_PushNode(handle.nodePtr);
            }
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
        public void SetAllowSecondaryVersionWriting(bool allowWriting)
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
            if (handle.nodePtr == null)
                return;

            if (handle.nodeLocalPtr != null)
                throw new Exception("Code-gen created a duplicate PatchLocal. This is bug.");

            handle.nodeLocalPtr = (AtomicSafetyNodePatched*)UnsafeUtility.Malloc(sizeof(AtomicSafetyNodePatched), 16, Allocator.Temp);
            *handle.nodeLocalPtr = *(AtomicSafetyNodePatched*)handle.nodePtr;

            // Clear bits marking this as a real AtomicSafetyNode
            handle.nodeLocalPtr->flags ^= AtomicSafetyNodeFlags.Magic;
            handle.nodeLocalPtr->originalNode = handle.nodePtr;

            handle.nodePtr = (AtomicSafetyNode*)handle.nodeLocalPtr;

            handle.version = handle.UncheckedGetNodeVersion() & AtomicSafetyNodeVersionMask.ReadWriteDisposeUnprotect;
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
        public static unsafe void ReleasePatched(ref AtomicSafetyHandle handle)
        {
            // JOB STRUCT
            // In single threaded build target jobs:
            //   We release the safety handle during post schedule with unpatched safety handles in the job struct.
            //   This is due to execute happening during schedule and still wanting dependency validation to function
            //   identically between MT and ST builds
            // In multi threaded build target jobs:
            //   Bursted parallel:
            //     We release the safety handle after execute with unpatched safety handles in the job struct because
            //     they were patched after marshalling
            //   Not bursted or non-parallel:
            //     We release the safety handle after execute with patched safety handles in the job struct.
            //
            // JOB PRODUCER
            // In both:
            //   We release the safety handle after execute with patched safety handles in the job producer.

            if (handle.nodeLocalPtr != null)
                handle.nodePtr = handle.nodeLocalPtr->originalNode;
            Release(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void SanityCheckForJob(ref AtomicSafetyHandle handle, bool isWrite, int jobNameOffset)
        {
            if (IsTempMemoryHandle(handle))
                throw new InvalidOperationException("Native container or resource allocated with temp memory. Temp memory containers cannot be used when scheduling a job, use TempJob instead.");

            if (handle.nodePtr == null)
                throw new InvalidOperationException("The native container or resource has not been assigned or constructed. All containers must be valid when scheduling a job.");
            else if (handle.version != (handle.UncheckedGetNodeVersion() & AtomicSafetyNodeVersionMask.ReadWriteDisposeUnprotect))
                throw new InvalidOperationException("The native container or resource has been deallocated. All containers must be valid when scheduling a job.");

            if (IsTempUnsafePtrSliceHandle(handle))
                throw new InvalidOperationException("The native container or resource can not be used in a job, because it was constructed from an Unsafe Pointer.");
            if (isWrite && handle.UncheckedIsSecondaryVersion() && (handle.nodePtr->flags & AtomicSafetyNodeFlags.AllowSecondaryWriting) == 0)
                throw new InvalidOperationException("The native container or resource must be marked [ReadOnly] in the job, because the container itself is marked read only.");
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
            AtomicSafetyNode* node = GetInternalNode();
            if (node == null)
                return 0;

            int count = node->readerCount < maxCount ? node->readerCount : maxCount;
            for (int i = 0; i < count; i++)
                handles[i] = node->readers[i].fence;

            return node->readerCount;
        }
        public unsafe static int GetReaderArray(AtomicSafetyHandle handle, int maxCount, IntPtr handles)
        {
            return handle.GetReaderArray(maxCount, (JobHandle*)handles);
        }

        public JobHandle GetWriter()
        {
            unsafe
            {
                AtomicSafetyNode* node = GetInternalNode();
                if (node != null)
                    return node->writer.fence;
            }
            return new JobHandle();
        }
        public static JobHandle GetWriter(AtomicSafetyHandle handle)
        {
            return handle.GetWriter();
        }

        public static string GetReaderName(AtomicSafetyHandle handle, int readerIndex) => "(GetReaderName not implemented yet)";

        public static string GetWriterName(AtomicSafetyHandle handle) => "(GetWriterName not implemented yet)";

        public static unsafe int NewStaticSafetyId(byte* ownerTypeNameBytes, int byteCount)
        {
            return 0;
        }

        public static unsafe int NewStaticSafetyId<T>()
        {
            return 0;
        }

        public static void SetStaticSafetyId(ref AtomicSafetyHandle handle, int staticSafetyId)
        {
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
            AtomicSafetyNode* node = handle.GetInternalNode();
            if (node == null)
                return EnforceJobResult.HandleWasAlreadyDeallocated;

            EnforceJobResult res = EnforceJobResult.AllJobsAlreadySynced;
            if (!JobsUtility.CheckDidSyncFence(ref node->writer.fence))
            {
                res = EnforceJobResult.DidSyncRunningJobs;
                JobsUtility.ScheduleBatchedJobsAndComplete(ref node->writer.fence);
            }

            for (int i = 0; i != node->readerCount; i++)
            {
                // Only allowed to access data if someone called sync fence on the job or on a job that depends on it.
                if (!JobsUtility.CheckDidSyncFence(ref node->readers[i].fence))
                {
                    res = EnforceJobResult.DidSyncRunningJobs;
                    JobsUtility.ScheduleBatchedJobsAndComplete(ref node->readers[i].fence);
                }
            }

            return res;
        }

        public static EnforceJobResult EnforceAllBufferJobsHaveCompletedAndRelease(AtomicSafetyHandle handle)
        {
            EnforceJobResult res = EnforceJobResult.AllJobsAlreadySynced;
            if (!handle.IsAllowedToDispose())
                res = EnforceAllBufferJobsHaveCompleted(handle);

            if (res != EnforceJobResult.HandleWasAlreadyDeallocated)
                AtomicSafetyHandle.Release(handle);

            return res;            
        }

        public static unsafe EnforceJobResult EnforceAllBufferJobsHaveCompletedAndDisableReadWrite(AtomicSafetyHandle handle)
        {
            EnforceJobResult res = EnforceJobResult.AllJobsAlreadySynced;
            if (!handle.IsAllowedToDispose())
                res = EnforceAllBufferJobsHaveCompleted(handle);

            if (res != EnforceJobResult.HandleWasAlreadyDeallocated)
            {
                handle.nodePtr->version0 |= AtomicSafetyNodeVersionMask.ReadWriteProtect;
                handle.nodePtr->version1 |= AtomicSafetyNodeVersionMask.ReadWriteProtect;
            }

            return res;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckMainThread()
        {
            // Currently if we're not executing a job, we have to be calling this from the main thread in DOTS Runtime.
            // This is a problem with NUnit tests however, because the "main thread" in NUnit tests is actually a runner thread
            // which is not the true main thread. So, it is safe to disable for now, but if we support any other means of creating threads
            // in DOTS Runtime in the future, this should be addressed.

            //if (!JobsUtility.IsMainThread())
            //    throw new InvalidOperationException("Native container or resource being used from thread which is not main or belonging to job");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckWriteAndThrow(AtomicSafetyHandle handle)
        {
            if (handle.IsAllowedToWrite())
                return;

            if (JobsUtility.IsExecutingJob)
                throw new InvalidOperationException("You are not allowed to write this native container or resource");

            unsafe
            {
                AtomicSafetyNode* node = handle.GetInternalNode();
                if (node == null)
                    throw new InvalidOperationException("The safety handle is no longer valid -- a native container or other protected resource has been deallocated");

                CheckMainThread();

                if (!JobsUtility.CheckDidSyncFence(ref node->writer.fence))
                    throw new InvalidOperationException("The previously scheduled job writes to the container or resource. You must call JobHandle.Complete() on the job, before you can write to the container or resource safely.");

                for (int i = 0; i != node->readerCount; i++)
                {
                    // Only allowed to access data if someone called sync fence on the job or on a job that depends on it.
                    if (!JobsUtility.CheckDidSyncFence(ref node->readers[i].fence))
                        throw new InvalidOperationException("The previously scheduled job reads from the container or resource. You must call JobHandle.Complete() on the job, before you can write to the container or resource safely.");
                }

                if ((node->flags & AtomicSafetyNodeFlags.AllowSecondaryWriting) == 0 && handle.UncheckedIsSecondaryVersion())
                throw new InvalidOperationException("Native container has been declared [ReadOnly] but you are attemping to write to it");

                // If we are write protected, but are no longer in a job and no other safety checks failed, we can remove write protection
                node->version0 &= AtomicSafetyNodeVersionMask.WriteUnprotect;
                if ((node->flags & AtomicSafetyNodeFlags.AllowSecondaryWriting) == AtomicSafetyNodeFlags.AllowSecondaryWriting)
                    node->version1 &= AtomicSafetyNodeVersionMask.WriteUnprotect;
            }

            Assert.IsTrue(handle.IsAllowedToWrite());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckReadAndThrow(AtomicSafetyHandle handle)
        {
            if (handle.IsAllowedToRead())
                return;

            if (JobsUtility.IsExecutingJob)
                throw new InvalidOperationException("You are not allowed to read this native container or resource");

            unsafe
            {
                AtomicSafetyNode* node = handle.GetInternalNode();
                if (node == null)
                    throw new InvalidOperationException("The safety handle is no longer valid -- a native container or other protected resource has been deallocated");

                CheckMainThread();

                if (!JobsUtility.CheckDidSyncFence(ref node->writer.fence))
                    throw new InvalidOperationException("The previously scheduled job writes to the container or resource. You must call JobHandle.Complete() on the job, before you can read from the container or resource safely.");

                // If we are read protected, but are no longer in a job and no other safety checks failed, we can remove read protection
                node->version0 &= AtomicSafetyNodeVersionMask.ReadUnprotect;
                node->version1 &= AtomicSafetyNodeVersionMask.ReadUnprotect;
            }

            Assert.IsTrue(handle.IsAllowedToRead());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckDisposeAndThrow(AtomicSafetyHandle handle)
        {
            if (handle.IsAllowedToDispose())
                return;

            if (JobsUtility.IsExecutingJob)
                throw new InvalidOperationException("You are not allowed to Dispose this native container or resource");

            unsafe
            {
                AtomicSafetyNode* node = handle.GetInternalNode();
                if (node == null)
                    throw new InvalidOperationException("The safety handle is no longer valid -- a native container or other protected resource has been deallocated");

                CheckMainThread();

                if (!JobsUtility.CheckDidSyncFence(ref node->writer.fence))
                    throw new InvalidOperationException("The previously scheduled job writes to the container or resource. You must call JobHandle.Complete() on the job, before you can deallocate the container or resource safely.");

                if ((node->flags & AtomicSafetyNodeFlags.AllowDispose) == 0)
                    throw new InvalidOperationException("You are not allowed to Dispose this native container or resource");

                for (int i = 0; i != node->readerCount; i++)
                {
                    // Only allowed to access data if someone called sync fence on the job or on a job that depends on it.
                    if (!JobsUtility.CheckDidSyncFence(ref node->readers[i].fence))
                        throw new InvalidOperationException("The previously scheduled job reads from the container or resource. You must call JobHandle.Complete() on the job, before you can deallocate the container or resource safely.");
                }

                // If we are dispose protected, but are no longer in a job and no other safety checks failed, we can remove dispose protection
                node->version0 &= AtomicSafetyNodeVersionMask.DisposeUnprotect;
                node->version1 &= AtomicSafetyNodeVersionMask.DisposeUnprotect;
            }

            Assert.IsTrue(handle.IsAllowedToDispose());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckDeallocateAndThrow(AtomicSafetyHandle handle)
        {
            CheckDisposeAndThrow(handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckGetSecondaryDataPointerAndThrow(AtomicSafetyHandle handle)
        {
            if (handle.IsAllowedToRead())
                return;

            if (JobsUtility.IsExecutingJob)
                throw new InvalidOperationException("You are not allowed to read this native container or resource");

            unsafe
            {
                AtomicSafetyNode* node = handle.GetInternalNode();
                if (node == null)
                    throw new InvalidOperationException("The safety handle is no longer valid -- a native container or other protected resource has been deallocated");

                Assert.IsFalse(handle.UncheckedIsSecondaryVersion());

                CheckMainThread();

                // The primary buffer might resize (List)
                // The secondary buffer does not resize (Array)
                // Thus if it was scheduled as a secondary buffer, we can safely access it
                if (node->writer.wasScheduledWithSecondaryBuffer == 1)
                    return;

                if (JobsUtility.CheckDidSyncFence(ref node->writer.fence))
                    return;
            }

            throw new InvalidOperationException("The previously scheduled job writes to the NativeList. You must call JobHandle.Complete() on the job before you can cast the NativeList to a NativeArray safely or use NativeList.AsDeferredJobArray() to cast the array when the job executes.");
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
