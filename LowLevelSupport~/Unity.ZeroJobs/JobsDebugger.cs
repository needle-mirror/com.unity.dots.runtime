#if ENABLE_UNITY_COLLECTIONS_CHECKS

using System.Runtime.InteropServices;
using System;
using System.Runtime.CompilerServices;
using Unity.Jobs;
using UnityEngine.Assertions;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using System.Diagnostics;

namespace Unity.Development.JobsDebugger
{
    internal static class StaticSafetyIdHashTable
    {
        [DllImport("lib_unity_zerojobs")]
        internal static extern void AtomicSafety_LockSafetyHashTables();

        [DllImport("lib_unity_zerojobs")]
        internal static extern void AtomicSafety_UnlockSafetyHashTables();

        private const int k_HashChunkSize = 65536;
        private const int k_MaxInfoLength = 112;
        private static int nextId = 0;

        // 1 atomic static info = 124/128 bytes (for 32/64 bit next pointers)
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct StaticInfoNode
        {
            internal int id;
            internal int nameBytes;
            internal fixed byte nameUtf8[k_MaxInfoLength];
            internal StaticInfoNode* next;  // offset 120
        }

        // Allocating chunks of 64k for Markers
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct FastHashTableBufferNode
        {
            internal fixed byte buffer[k_HashChunkSize - 128];
            internal int capacity;
            internal int size;
            internal FastHashTableBufferNode* next;

            internal static unsafe FastHashTableBufferNode* Allocate(int typeSize)
            {
                var node = (FastHashTableBufferNode*)UnsafeUtility.Malloc(k_HashChunkSize, 16, Allocator.Persistent);
                UnsafeUtility.MemClear(node, k_HashChunkSize);
                node->capacity = (k_HashChunkSize - 128) / typeSize;
                node->size = 256;  // number of buckets
                return node;
            }

            // Returns the new tail (old one if pool not extended)
            internal unsafe FastHashTableBufferNode* ExtendFullPool(int typeSize)
            {
                if (size == capacity)
                {
                    FastHashTableBufferNode* newPool = Allocate(typeSize);
                    newPool->size = 0;
                    next = newPool;
                    return newPool;
                }

                fixed (FastHashTableBufferNode* self = &this)
                    return self;
            }

            internal StaticInfoNode* StaticInfoBuffer
            {
                get
                {
                    fixed (byte* b = buffer)
                        return (StaticInfoNode*)b;
                }
            }
        }

        internal unsafe static FastHashTableBufferNode* StaticHeadBufferNode => staticHashTableHead;

        private unsafe static FastHashTableBufferNode* staticHashTableHead = null;
        private unsafe static FastHashTableBufferNode* staticHashTableTail = null;

        internal unsafe static int CreateOrGetSafetyId(byte* ownerTypeNameBytes, int byteCount)
        {
            if (byteCount <= 0)
                return -1;

            if (StaticHeadBufferNode == null)
            {
                staticHashTableHead = FastHashTableBufferNode.Allocate(sizeof(StaticInfoNode));
                staticHashTableTail = staticHashTableHead;
            }

            int bucket = (((byteCount << 5) + (byteCount >> 2)) ^ ownerTypeNameBytes[0]) & 255;
            StaticInfoNode* next = &staticHashTableHead->StaticInfoBuffer[bucket];
            StaticInfoNode* staticInfo = null;

            // No need for locking yet - read operations on hash table are thread safe as long as we are careful about
            // modification during write and only allow one thread to write at a time.
            while (next != null)
            {
                staticInfo = next;
                next = staticInfo->next;

                if (staticInfo->nameBytes == byteCount)
                {
                    if (UnsafeUtility.MemCmp(ownerTypeNameBytes, staticInfo->nameUtf8, byteCount) == 0)
                        return staticInfo->id;
                }
            }

            // The static safety id didn't exist in hash table. Need to lock so only one thread can modify at a time.
            // This path will usually only be taken during startup - after which static safety ids should be found in the
            // above loop instead of needing to be created.
            AtomicSafety_LockSafetyHashTables();

            StaticInfoNode* oldInfo = null;
            if (staticInfo->nameBytes > 0)
            {
                // There is already a valid marker here at the end of the linked list - add a new one
                staticHashTableTail = staticHashTableTail->ExtendFullPool(sizeof(StaticInfoNode));

                StaticInfoNode* newInfo = &staticHashTableTail->StaticInfoBuffer[staticHashTableTail->size];
                staticHashTableTail->size++;
                oldInfo = staticInfo;
                staticInfo = newInfo;
            }

            staticInfo->id = nextId++;
            staticInfo->nameBytes = byteCount;
            Assert.IsTrue(byteCount <= k_MaxInfoLength);
            UnsafeUtility.MemCpy(staticInfo->nameUtf8, ownerTypeNameBytes, byteCount);

            // Do this last so if we find the node before locking, we don't have access to it unless it is otherwise fully assigned
            if (oldInfo != null)
                oldInfo->next = staticInfo;

            AtomicSafety_UnlockSafetyHashTables();

            return staticInfo->id;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 32, Pack = 4, CharSet = CharSet.Ansi)]
    public struct JobNameStorage
    {
        [FieldOffset(0)]
        public char zero;
    }

    public class JobNames
    {
        private static readonly JobNameStorage m_NameStorage;

        public unsafe static char* NameBlobPtr { get; private set; }

        public static unsafe void Initialize()
        {
            fixed (JobNameStorage* p = &m_NameStorage)
                NameBlobPtr = (char*)p;
        }
    }

    public class ErrorReporter
    {
        public const string k_ErrorReadAgainstScheduledWrite = "The previously scheduled job {1} writes to {2} through {3}. You are trying to schedule a new job {0} which reads from the same resource. To guarantee safety, you must include that job as a dependency of the newly scheduled job.";
        public const string k_ErrorWriteAgainstScheduledWrite = "The previously scheduled job {1} writes to {2} through {3}. You are trying to schedule a new job {0} which writes to the same resource. To guarantee safety, you must include that job as a dependency of the newly scheduled job.";
        public const string k_ErrorMarkReadOnly = "{2} must be marked [ReadOnly] in the job {0}, because the container itself is marked read only.";

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public unsafe static void ReportError(string message, int jobNameOffset, int otherJobNameOffset, int fieldNameOffset, int otherFieldNameOffset)
        {
            string jobName = new string(JobNames.NameBlobPtr + jobNameOffset);
            string otherJobName = new string(JobNames.NameBlobPtr + otherJobNameOffset);
            string fieldName = new string(JobNames.NameBlobPtr + fieldNameOffset);
            string otherFieldName = new string(JobNames.NameBlobPtr + otherFieldNameOffset);
            string output = string.Format(message, jobName, otherJobName, fieldName, otherFieldName);
            throw new InvalidOperationException(output);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DependencyValidator
    {
        // The reason AtomicSafetyNode isn't enough for dependency checking is:
        // a) For writable resources, we need the AtomicSafetyHandle representing a reference to the resource
        //    so that we can check if it's using the secondary version while updating dependency information.
        // b) Regardless, for all resources, we need a safety id representing the reflected name for understandable
        //    and useful error messages
        private AtomicSafetyHandle** ReadOnlyHandlePtr;
        private int ReadOnlyHandleCapacity;
        private int ReadOnlyHandleKnown;
        private int ReadOnlyHandleCount;
        private AtomicSafetyHandle** WritableHandlePtr;
        private int WritableHandleCapacity;
        private int WritableHandleKnown;
        private int WritableHandleCount;
        private AtomicSafetyNode** DeallocateNodePtr;
        private int DeallocateNodeCapacity;
        private int DeallocateNodeKnown;
        private int DeallocateNodeCount;

        private static void* EnsureSpace(void* currMem, ref int currCapacity, ref int currKnown, int addKnown)
        {
            const int kCapacityDeltaMinus1 = 15;

            void* ret = currMem;
            if (currKnown + addKnown > currCapacity)
            {
                int increase = (currKnown + addKnown - currCapacity + kCapacityDeltaMinus1) & ~kCapacityDeltaMinus1;
                currCapacity += increase;
                ret = UnsafeUtility.Malloc(currCapacity * sizeof(void*), 0, Allocator.Temp);
                if (currMem != null)
                {
                    UnsafeUtility.MemCpy(ret, currMem, currKnown * sizeof(void*));
                    UnsafeUtility.Free(currMem, Allocator.Temp);
                }
            }

            currKnown += addKnown;

            return ret;
        }

        // Allocate memory for resources with known usage semantics.
        // Excludes dynamic safety handles which are unknown and determined at runtime.
        public static void AllocateKnown(ref DependencyValidator data, int numReadOnly, int numWritable, int numDeallocate)
        {
            data.ReadOnlyHandlePtr = (AtomicSafetyHandle**)EnsureSpace((void*)data.ReadOnlyHandlePtr, ref data.ReadOnlyHandleCapacity, ref data.ReadOnlyHandleKnown, numReadOnly);
            data.WritableHandlePtr = (AtomicSafetyHandle**)EnsureSpace((void*)data.WritableHandlePtr, ref data.WritableHandleCapacity, ref data.WritableHandleKnown, numWritable);
            data.DeallocateNodePtr = (AtomicSafetyNode**)EnsureSpace((void*)data.DeallocateNodePtr, ref data.DeallocateNodeCapacity, ref data.DeallocateNodeKnown, numDeallocate);
        }

        public static void Cleanup(ref DependencyValidator data)
        {
            UnsafeUtility.Free((void*)data.ReadOnlyHandlePtr, Allocator.Temp);
            UnsafeUtility.Free((void*)data.WritableHandlePtr, Allocator.Temp);
            UnsafeUtility.Free((void*)data.DeallocateNodePtr, Allocator.Temp);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RecordAndSanityCheckReadOnly(ref DependencyValidator data, ref AtomicSafetyHandle handle, int fieldNameBlobOffset, int jobNameOffset)
        {
            handle.staticSafetyId = fieldNameBlobOffset;
            fixed (AtomicSafetyHandle* handlePtr = &handle)
                data.ReadOnlyHandlePtr[data.ReadOnlyHandleCount++] = handlePtr;
            AtomicSafetyHandle.SanityCheckForJob(ref handle, false, jobNameOffset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RecordAndSanityCheckWritable(ref DependencyValidator data, ref AtomicSafetyHandle handle, int fieldNameBlobOffset, int jobNameOffset)
        {
            handle.staticSafetyId = fieldNameBlobOffset;
            fixed (AtomicSafetyHandle* handlePtr = &handle)
                data.WritableHandlePtr[data.WritableHandleCount++] = handlePtr;
            AtomicSafetyHandle.SanityCheckForJob(ref handle, true, jobNameOffset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RecordDeallocate(ref DependencyValidator data, ref AtomicSafetyHandle handle)
        {
            data.DeallocateNodePtr[data.DeallocateNodeCount++] = handle.nodePtr;
        }

        public static void RecordAndSanityCheckDynamic(ref DependencyValidator data, ref AtomicSafetyHandle firstHandle, int fieldNameBlobOffset, int jobNameOffset, int handleCountReadOnly, int handleCountWritable, int handleCountForceReadOnly)
        {
            // If a container or resource is [ReadOnly] then the read/write safety handle count will be forced read only
            int countRead = handleCountReadOnly + handleCountForceReadOnly;
            int countWritable = handleCountWritable;

            fixed (AtomicSafetyHandle* firstHandlePtr = &firstHandle)
            {
                if (countRead > 0)
                {
                    data.ReadOnlyHandlePtr = (AtomicSafetyHandle**)EnsureSpace((void*)data.ReadOnlyHandlePtr, ref data.ReadOnlyHandleCapacity, ref data.ReadOnlyHandleKnown, countRead);
                    for (int i = 0; i < countRead; i++)
                        RecordAndSanityCheckReadOnly(ref data, ref firstHandlePtr[i], fieldNameBlobOffset, jobNameOffset);
                }
                if (countWritable > 0)
                {
                    data.WritableHandlePtr = (AtomicSafetyHandle**)EnsureSpace((void*)data.WritableHandlePtr, ref data.WritableHandleCapacity, ref data.WritableHandleKnown, countWritable);
                    int countTotal = countRead + countWritable;
                    for (int i = countRead; i < countTotal; i++)
                        RecordAndSanityCheckWritable(ref data, ref firstHandlePtr[i], fieldNameBlobOffset, jobNameOffset);
                }
            }
        }

        // Checks dependencies and aliasing
        public static void ValidateScheduleSafety(ref DependencyValidator data, ref JobHandle dependsOn, int jobNameOffset)
        {
            AtomicSafetyHandle** writableHandles = data.WritableHandlePtr;
            AtomicSafetyHandle** readOnlyHandles = data.ReadOnlyHandlePtr;
            AtomicSafetyNode** deallocateNodes = data.DeallocateNodePtr;

            // Check writing
            // * read write buffer must have dependencies expressed against jobs that are reading from the buffer
            // * only one job can write to a buffer at a time, must be dependency.
            // * aliasing - read write buffer may not be present multiple times
            // * aliasing - read write buffer may not also be a read only buffer
            for (int iw = 0; iw != data.WritableHandleCount; iw++)
            {
                AtomicSafetyNode* writableNode = writableHandles[iw]->nodePtr;

                for (int i = 0; i < writableNode->readerCount; i++)
                {
                    if (!JobsUtility.CheckFenceIsDependencyOrDidSyncFence(ref writableNode->readers[i].fence, ref dependsOn))
                        throw new InvalidOperationException("The previously scheduled job reads from the resource. You are trying to schedule a new job, which writes to the same resource (via another job). To guarantee safety, you must include that job as a dependency of the newly scheduled job.");
                }

                if (!JobsUtility.CheckFenceIsDependencyOrDidSyncFence(ref writableNode->writer.fence, ref dependsOn))
                    ErrorReporter.ReportError(ErrorReporter.k_ErrorWriteAgainstScheduledWrite, jobNameOffset, writableNode->writer.jobNameOffset, writableHandles[iw]->staticSafetyId, writableNode->writer.fieldNameOffset);

                for (int ir = 0; ir < data.ReadOnlyHandleCount; ir++)
                {
                    if (writableNode == readOnlyHandles[ir])
                        throw new InvalidOperationException("A read-only and write container alias in this job");
                }

                for (int iw2 = iw + 1; iw2 < data.WritableHandleCount; iw2++)
                {
                    if (writableNode == writableHandles[iw2]->nodePtr)
                        throw new InvalidOperationException("Two write containers alias in this job");
                }
            }

            // Check reading
            // * read buffer must have dependencies against any jobs writing to the buffer
            for (int ir = 0; ir != data.ReadOnlyHandleCount; ir++)
            {
                AtomicSafetyNode* readOnlyNode = readOnlyHandles[ir]->nodePtr;

                if (!JobsUtility.CheckFenceIsDependencyOrDidSyncFence(ref readOnlyNode->writer.fence, ref dependsOn))
                    ErrorReporter.ReportError(ErrorReporter.k_ErrorReadAgainstScheduledWrite, jobNameOffset, readOnlyNode->writer.jobNameOffset, readOnlyHandles[ir]->staticSafetyId, readOnlyNode->writer.fieldNameOffset);
            }
             
            // Check destroying
            // * destroy buffer may not be present multiple times
            // * destroy buffer must have input dependencies on jobs reading from the buffer
            // * destroy buffer must have input dependencies on jobs writing to the buffer
            for (int id = 0; id < data.DeallocateNodeCount; id++)
            {
                AtomicSafetyNode* deallocateNode = deallocateNodes[id];

                // There can be default initialized safety handles (with no safety node) if the container is default initialized
                // in a job with complex logic which accounts for an empty container
                if (deallocateNode == null)
                    continue;

                for (int id2 = id + 1; id2 < data.DeallocateNodeCount; id2++)
                {
                    if (deallocateNode == deallocateNodes[id2])
                        throw new InvalidOperationException("Two deallocating containers alias in this job");
                }

                for (int i = 0; i < deallocateNode->readerCount; i++)
                {
                    if (!JobsUtility.CheckFenceIsDependencyOrDidSyncFence(ref deallocateNode->readers[i].fence, ref dependsOn))
                        throw new InvalidOperationException("A deallocation job was scheduled against a read job");
                }

                if (!JobsUtility.CheckFenceIsDependencyOrDidSyncFence(ref deallocateNode->writer.fence, ref dependsOn))
                    throw new InvalidOperationException("A deallocation job was scheduled against a write job");
            }
        }

        // Checks deferred array ownership
        public static void ValidateDeferred(ref DependencyValidator data, void *deferredHandle)
        {
            if (deferredHandle == null)
                return;

            AtomicSafetyHandle** writableHandles = data.WritableHandlePtr;
            AtomicSafetyHandle** readOnlyHandles = data.ReadOnlyHandlePtr;
            AtomicSafetyNode** deallocateNodes = data.DeallocateNodePtr;
            AtomicSafetyNode* deferredNode = ((AtomicSafetyHandle*)deferredHandle)->nodePtr;

            for (int iw = 0; iw != data.WritableHandleCount; iw++)
            {
                if (writableHandles[iw]->nodePtr == deferredNode)
                    return;
            }

            for (int ir = 0; ir != data.ReadOnlyHandleCount; ir++)
            {
                if (readOnlyHandles[ir]->nodePtr == deferredNode)
                    return;
            }

            for (int id = 0; id != data.DeallocateNodeCount; id++)
            {
                if (deallocateNodes[id] == deferredNode)
                    return;
            }

            throw new InvalidOperationException("The deferred list is used to set the iteration count deferred when the job executes, but it is required that it is included in the job struct");
        }

        // Checks deallocation constraints
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ValidateDeallocateOnJobCompletion(Allocator allocType)
        {
            if (allocType != Allocator.Persistent && allocType != Allocator.TempJob)
                throw new InvalidOperationException("Only Allocator.Persistent and Allocator.TempJob can be deallocated from a job");
        }

        public static void UpdateDependencies(ref DependencyValidator data, ref JobHandle handle, int jobNameOffset)
        {
            // If the job has been run immediately rather than scheduled, don't update safety nodes because they have
            // likely already been released
            if (handle.JobGroup == IntPtr.Zero)
                return;

            AtomicSafetyHandle** writableHandles = data.WritableHandlePtr;
            AtomicSafetyHandle** readOnlyHandles = data.ReadOnlyHandlePtr;

            for (int i = 0; i < data.WritableHandleCount; i++)
            {
                // In case the node was released in another job that already executed, we need to check if it's still valid
                AtomicSafetyNode* writableNode = writableHandles[i]->GetInternalNode();

                // There can be default initialized safety handles (with no safety node) if the container is default initialized
                // in a job with complex logic which accounts for an empty container
                if (writableNode == null)
                    continue;

                bool isSecondaryVersion = writableHandles[i]->UncheckedIsSecondaryVersion();

                writableNode->writer.fence = handle;
                writableNode->writer.wasScheduledWithSecondaryBuffer = isSecondaryVersion ? 1 : 0;
                writableNode->writer.jobNameOffset = jobNameOffset;
                writableNode->writer.fieldNameOffset = writableHandles[i]->staticSafetyId;

                if (!isSecondaryVersion && (writableNode->flags & AtomicSafetyNodeFlags.BumpSecondaryVersionOnScheduleWrite) != 0)
                    writableNode->version1 += AtomicSafetyNodeVersionMask.VersionInc;

                writableNode->version0 |= AtomicSafetyNodeVersionMask.ReadWriteDisposeProtect;
                writableNode->version1 |= AtomicSafetyNodeVersionMask.ReadWriteDisposeProtect;
            }

            for (int i = 0; i < data.ReadOnlyHandleCount; i++)
            {
                AtomicSafetyNode* readOnlyNode = readOnlyHandles[i]->GetInternalNode();

                // There can be default initialized safety handles (with no safety node) if the container is default initialized
                // in a job with complex logic which accounts for an empty container
                if (readOnlyNode == null)
                    continue;

                // Clean up any synced readers
                //
                // Some safety handles protect resources which have long lifetimes, such as entities' component types.
                // (In that example, safety handles aren't released until a synchronization point such as archetype changes)
                //
                // New jobs reading these handles will continue adding "readers" and performance and memory usage will degrade
                // very quickly as the accumulation of readers continues to be included in dependency validation checks long
                // after the associated jobs have sync'd.
                //
                // Note: We can make this code faster with some sort of grow-able circular buffer if performance issues appear here.
                // For now, the simple approach is ideal, esp. because in most cases we have either sync'd none or all of the
                // readers so we avoid the memcpy anyway.
                int syncedReaders = 0;
                for (syncedReaders = 0; syncedReaders < readOnlyNode->readerCount; syncedReaders++)
                {
                    var oldReader = &readOnlyNode->readers[syncedReaders];
                    if (!JobsUtility.CheckDidSyncFence(ref oldReader->fence))
                        break;
                }
                if (syncedReaders > 0)
                {
                    if (syncedReaders < readOnlyNode->readerCount)
                        UnsafeUtility.MemCpy(readOnlyNode->readers, readOnlyNode->readers + syncedReaders, (readOnlyNode->readerCount - syncedReaders) * sizeof(BufferDebugData));
                    readOnlyNode->readerCount -= syncedReaders;
                }

                // Update readers after cleanup
                var reader = readOnlyNode->AddReader();
                reader->fence = handle;
                reader->wasScheduledWithSecondaryBuffer = 0;
                reader->jobNameOffset = jobNameOffset;
                reader->fieldNameOffset = readOnlyHandles[i]->staticSafetyId;

                readOnlyNode->version0 |= AtomicSafetyNodeVersionMask.ReadWriteDisposeProtect;
                readOnlyNode->version1 |= AtomicSafetyNodeVersionMask.ReadWriteDisposeProtect;
            }
        }
    }
}

#endif // ENABLE_UNITY_COLLECTIONS_CHECKS
