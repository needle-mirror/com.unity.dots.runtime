using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Assertions;
using Unity.Burst;
using Unity.Core;

namespace Unity.Jobs.LowLevel.Unsafe
{
    // Code gen depends on these constants.
    public enum JobType
    {
        Single,
        ParallelFor
    }

    public enum WorkStealingState : int
    {
        NotInitialized,
        Initialized,
        Done
    }

    // NOTE: This doesn't match (big) Unity's JobRanges because JobsUtility.GetWorkStealingRange isn't fully implemented
    public struct JobRanges
    {
        public int ArrayLength;
        public int IndicesPerPhase;
        public WorkStealingState State;
        public int runOnMainThread;
    }

    // The internally used header for JobData
    // The code-gen relies on explicit layout.
    // Trivial with one entry, but declared here as a reminder.
    // Also, to preserve alignment, code-gen is using a size=16
    // for this structure.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public unsafe struct JobMetaData
    {
        // Must be the zero offset (JobMetaDataPtr === JobRanges in InterfaceGen)
        [FieldOffset(0)]
        public JobRanges JobRanges;

        // Sync with InterfaceGen.cs!
        public const int kJobMetaDataIsParallelOffset = 16;
        [FieldOffset(kJobMetaDataIsParallelOffset)]
        public int isParallelFor;

        // Sync with InterfaceGen.cs!
        const int kJobMetaDataJobSize = 20;
        [FieldOffset(kJobMetaDataJobSize)]
        public int jobDataSize;

        // Sync with InterfaceGen.cs!
        const int kJobMetaDataDeferredDataPtr = 24;
        [FieldOffset(kJobMetaDataDeferredDataPtr)]
        public void* deferredDataPtr;

        // Sync with InterfaceGen.cs!
        const int kJobMetaDataManagedPtr = 32;
        [FieldOffset(kJobMetaDataManagedPtr)]
        public void* managedPtr;

        // Sync with InterfaceGen.cs!
        const int kJobMetaDataUnmanagedPtr = 40;
        [FieldOffset(kJobMetaDataUnmanagedPtr)]
        public void* unmanagedPtr;
    }

    public enum ScheduleMode : int
    {
        Run = 0,
        // Deprecate when we add compelete 2020.2 Jobs API support :: ("Batched is obsolete, use Parallel or Single depending on job type. (UnityUpgradable) -> Parallel", false)]
        Batched = 1,
        Parallel = 1,
        Single = 2,                   // Unused in DOTS Runtime currently
        RunOnMainThread = 1000,       // Synced
        ScheduleOnMainThread = 2000,  // Needs synced to satisfy safety system
    }

    [AttributeUsage(AttributeTargets.Interface)]
    public sealed class JobProducerTypeAttribute : Attribute
    {
        public JobProducerTypeAttribute(Type producerType) => throw new NotImplementedException();
        public Type ProducerType => throw new NotImplementedException();
    }

    public static class JobsUtility
    {
#if UNITY_SINGLETHREADED_JOBS
        public const int JobWorkerCount = 0;
        public const int MaxJobThreadCount = 1;
#else
        struct JobWorkerCountKey { }
        static readonly SharedStatic<int> s_JobWorkerCount = SharedStatic<int>.GetOrCreate<JobWorkerCountKey>();

        public static unsafe int JobWorkerCount
        {
            get
            {
                return s_JobWorkerCount.Data;
            }
        }
        public const int MaxJobThreadCount = 128;
#endif
        public const int CacheLineSize = 64;
        private const int k_MainThreadWorkerIndex = -1;  // thread index is worker index + 1 always, and thread index 0 should denote main thread, with worker threads starting at 1

        public static bool JobCompilerEnabled => true;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public static bool JobDebuggerEnabled
        {
            get
            {
                return IsJobsDebuggerEnabled() != 0;
            }
            set
            {
                EnableJobsDebugger(value ? 1 : 0);
            }
        }
#else
        public static bool JobDebuggerEnabled => false;
#endif

        [StructLayout(LayoutKind.Sequential)]
        public struct JobScheduleParameters
        {
            public JobHandle Dependency;
            public ScheduleMode ScheduleMode;
            public unsafe ReflectionDataProxy* ReflectionData;
            public unsafe JobMetaData* JobDataPtr;

            public unsafe JobScheduleParameters(void* jobData,
                IntPtr reflectionData,  // need IntPtr to match Big Unity calls in obsolete methods in collections, for now
                JobHandle jobDependency,
                ScheduleMode scheduleMode,
                int jobDataSize = 0,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                int producerJobPreSchedule = 1,
                int userJobPreSchedule = 3,
#endif
                int isBursted = 5)
            : this(jobData,
                  (ReflectionDataProxy*)reflectionData,
                  jobDependency,
                  scheduleMode,
                  jobDataSize,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                  producerJobPreSchedule,
                  userJobPreSchedule,
#endif
                  isBursted)
            {
            }

            public unsafe JobScheduleParameters(void* jobData,
                ReflectionDataProxy* reflectionData,
                JobHandle jobDependency,
                ScheduleMode scheduleMode,
                int jobDataSize = 0,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                int producerJobPreSchedule = 1,
                int userJobPreSchedule = 3,
#endif
                int isBursted = 5)
            {
                // Synchronize with InterfaceGen.cs!
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                const int k_ProducerScheduleReturnValue = 4;
                const int k_UserScheduleReturnValue = 2;
                const int k_UserScheduleReturnValueNoParallel = -2;
#endif

                const string k_PostFix = " Seeing this error indicates a bug in the dots compiler. We'd appreciate a bug report (About->Report a Bug...).";

                // Default is 0; code-gen should set to a correct size.
                if (jobDataSize == 0)
                    throw new InvalidOperationException("JobScheduleParameters (size) should be set by code-gen." + k_PostFix);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (producerJobPreSchedule != k_ProducerScheduleReturnValue)
                    throw new InvalidOperationException(
                        "JobScheduleParameter (which is the return code of ProducerScheduleFn_Gen) should be set by code-gen." + k_PostFix);
                if (userJobPreSchedule != k_UserScheduleReturnValue && userJobPreSchedule != k_UserScheduleReturnValueNoParallel)
                    throw new InvalidOperationException(
                        "JobScheduleParameter (which is the return code of PrepareJobAtPreScheduleTimeFn_Gen) should be set by code-gen." + k_PostFix);
#endif
                if (!(isBursted == 0 || isBursted == 1))
                    throw new InvalidOperationException(
                        "JobScheduleParameter (which is the return code of RunOnMainThread_Gen) should be set by code-gen." + k_PostFix);

                int nWorkers = JobWorkerCount > 0 ? JobWorkerCount : 1;
                JobMetaData* mem = AllocateJobHeapMemory(jobDataSize, nWorkers);

                // A copy of the JobData is needed *for each worker thread* as it will
                // get mutated in unique ways (threadIndex, safety.) The jobIndex is passed
                // to the Execute method, so a thread can look up the correct jobData to use.
                // Cleanup is always called on jobIndex=0.
                for (int i = 0; i < nWorkers; i++)
                    UnsafeUtility.MemCpy(((byte*)mem + sizeof(JobMetaData) + jobDataSize * i), jobData, jobDataSize);
                UnsafeUtility.AssertHeap(mem);

                JobMetaData jobMetaData = new JobMetaData();
                jobMetaData.jobDataSize = jobDataSize;
                // Indicate parallel for is an error
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (userJobPreSchedule == k_UserScheduleReturnValueNoParallel)
                    jobMetaData.isParallelFor = -1;
#endif
                UnsafeUtility.CopyStructureToPtr(ref jobMetaData, mem);

                Dependency = jobDependency;
                JobDataPtr = mem;
                ReflectionData = reflectionData;
#if UNITY_DOTSRUNTIME_MULTITHREAD_NOBURST
                // Allow debugging multithreaded code without burst when necessary
                ScheduleMode = scheduleMode;
#else
                // Normally, only bursted methods run on worker threads
                if (isBursted != 0)
                    ScheduleMode = scheduleMode;
                else
                {
                    // Both main thread schedule modes run immediately - the difference is that scheduling returns a job handle
                    // which must still be synced in order to maintain dependency safety, whereas run returns a default job handle
                    // not needing completion.
                    ScheduleMode = scheduleMode == ScheduleMode.Run ? ScheduleMode.RunOnMainThread : ScheduleMode.ScheduleOnMainThread;
                }
#endif
            }
        }

        class ReflectionDataStore
        {
            // Dotnet throws an exception if the function pointers aren't pinned by a delegate.
            // Error checking? The pointers certainly can't change.
            // This class registers the function pointers with the GC.
            // TODO a more elegant solution, or switch to calli and avoid this.
            public ReflectionDataStore(Delegate executeDelegate, Delegate codeGenCleanupDelegate, Delegate codeGenExecuteDelegate, Delegate codeGenMarshalToBurstDelegate)
            {
                ExecuteDelegate = executeDelegate;
                ExecuteDelegateHandle = GCHandle.Alloc(ExecuteDelegate);

                if (codeGenCleanupDelegate != null)
                {
                    CodeGenCleanupDelegate = codeGenCleanupDelegate;
                    CodeGenCleanupDelegateHandle = GCHandle.Alloc(CodeGenCleanupDelegate);
                    CodeGenCleanupFunctionPtr = Marshal.GetFunctionPointerForDelegate(codeGenCleanupDelegate);
                }

                if (codeGenExecuteDelegate != null)
                {
                    CodeGenExecuteDelegate = codeGenExecuteDelegate;
                    CodeGenExecuteDelegateHandle = GCHandle.Alloc(CodeGenExecuteDelegate);
                    CodeGenExecuteFunctionPtr = Marshal.GetFunctionPointerForDelegate(codeGenExecuteDelegate);
                }

                if (codeGenMarshalToBurstDelegate != null)
                {
                    CodeGenMarshalToBurstDelegate = codeGenMarshalToBurstDelegate;
                    CodeGenMarshalToBurstDelegateHandle = GCHandle.Alloc(CodeGenMarshalToBurstDelegate);
                    CodeGenMarshalToBurstFunctionPtr = Marshal.GetFunctionPointerForDelegate(codeGenMarshalToBurstDelegate);
                }
            }

            internal ReflectionDataStore next;

            public Delegate ExecuteDelegate;
            public GCHandle ExecuteDelegateHandle;

            public Delegate CodeGenCleanupDelegate;
            public GCHandle CodeGenCleanupDelegateHandle;
            public IntPtr   CodeGenCleanupFunctionPtr;

            public Delegate CodeGenExecuteDelegate;
            public GCHandle CodeGenExecuteDelegateHandle;
            public IntPtr   CodeGenExecuteFunctionPtr;

            public Delegate CodeGenMarshalToBurstDelegate;
            public GCHandle CodeGenMarshalToBurstDelegateHandle;
            public IntPtr   CodeGenMarshalToBurstFunctionPtr;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ReflectionDataProxy
        {
            public IntPtr  ExecuteFunctionPtr;
            public IntPtr  CleanupFunctionPtr;
#if ENABLE_UNITY_COLLECTIONS_CHECKS && !UNITY_DOTSRUNTIME_IL2CPP
            public int     UnmanagedSize;
            public IntPtr  MarshalToBurstFunctionPtr;
#endif
        }

        public unsafe delegate void ManagedJobDelegate(void* ptr);
        // TODO: Should be ManagedJobExecuteDelegate, but waiting for Burst update.
        public unsafe delegate void ManagedJobForEachDelegate(void* ptr, int jobIndex);
        public unsafe delegate void ManagedJobMarshalDelegate(void* dst, void* src);

        static class Managed {
            public static ReflectionDataStore reflectionDataStoreRoot = null;
        }

#if UNITY_WINDOWS
        internal const string nativejobslib = "nativejobs";
#else
        internal const string nativejobslib = "libnativejobs";
#endif

        public unsafe static void Initialize()
        {
#if !UNITY_SINGLETHREADED_JOBS
            // We need to push the thread count before the jobs run, because we can't make a lazy
            // call to Environment.ProcessorCount from Burst.
            // about the 8 thread restriction: https://unity3d.atlassian.net/browse/DOTSR-1499
            s_JobWorkerCount.Data = Environment.ProcessorCount < 8 ? Environment.ProcessorCount : 8;
            s_JobQueue.Data = CreateJobQueue((IntPtr)UnsafeUtility.AddressOf(ref JobQueueName[0]), (IntPtr)UnsafeUtility.AddressOf(ref WorkerThreadName[0]), JobWorkerCount);
            s_BatchScheduler.Data = CreateJobBatchScheduler();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.AtomicSafetyHandle_SetBatchScheduler((void*)s_BatchScheduler.Data);
#endif
#endif
        }

        public static unsafe void Shutdown()
        {
#if !UNITY_SINGLETHREADED_JOBS
            if (s_BatchScheduler.Data != IntPtr.Zero)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.AtomicSafetyHandle_SetBatchScheduler(null);
#endif
                DestroyJobBatchScheduler(s_BatchScheduler.Data);
                s_BatchScheduler.Data = IntPtr.Zero;
            }

            if (s_JobQueue.Data != IntPtr.Zero)
            {
                DestroyJobQueue();
                s_JobQueue.Data = IntPtr.Zero;
            }
#endif
        }

#if !UNITY_SINGLETHREADED_JOBS
        // Todo: Remove this jank when nativejobs offers the ability to make a job queue without specifying a name
        static readonly byte[] JobQueueName = new byte[] { 0x6a, 0x6f, 0x62, 0x2d, 0x71, 0x75, 0x65, 0x75, 0x65, 0x00 }; // job-queue, UTF-8
        static readonly byte[] WorkerThreadName = new byte[] { 0x77, 0x6f, 0x72, 0x6b, 0x65, 0x72, 0x2d, 0x62, 0x65, 0x65, 0x00 }; // worker-bee, UTF-8
        internal static unsafe IntPtr JobQueue => s_JobQueue.Data;
        internal static IntPtr BatchScheduler => s_BatchScheduler.Data;

        struct JobQueueSharedStaticKey { }
        struct BatchScheduldeSharedStaticKey { }
        static readonly SharedStatic<IntPtr> s_JobQueue = SharedStatic<IntPtr>.GetOrCreate<JobQueueSharedStaticKey>();
        static readonly SharedStatic<IntPtr> s_BatchScheduler = SharedStatic<IntPtr>.GetOrCreate<BatchScheduldeSharedStaticKey>();

        public static unsafe JobHandle ScheduleJob(IntPtr jobFuncPtr, JobMetaData* jobDataPtr, JobHandle dependsOn)
        {
            Assert.IsTrue(JobQueue != IntPtr.Zero);

            ScheduleJobBatch(BatchScheduler, jobFuncPtr, jobDataPtr, ref dependsOn, out var jobHandle);
            return jobHandle;
        }

        public static unsafe JobHandle ScheduleJobParallelFor(IntPtr jobFuncPtr, IntPtr jobCompletionFuncPtr, JobMetaData* jobDataPtr, int arrayLength, int innerloopBatchCount, JobHandle dependsOn)
        {
            Assert.IsTrue(JobQueue != IntPtr.Zero && BatchScheduler != IntPtr.Zero);

            ScheduleJobBatchParallelFor(BatchScheduler, jobFuncPtr, jobDataPtr, arrayLength, innerloopBatchCount, jobCompletionFuncPtr, ref dependsOn, out var jobHandle);
            return jobHandle;
        }

        [DllImport(nativejobslib)]
        internal static extern unsafe IntPtr CreateJobQueue(IntPtr queueName, IntPtr workerName, int numJobWorkerThreads);

        [DllImport(nativejobslib)]
        internal static extern void DestroyJobQueue();

        [DllImport(nativejobslib)]
        internal static extern IntPtr CreateJobBatchScheduler();

        [DllImport(nativejobslib)]
        internal static extern void DestroyJobBatchScheduler(IntPtr scheduler);

        [DllImport(nativejobslib)]
        internal static extern unsafe void ScheduleJobBatch(IntPtr scheduler, IntPtr func, JobMetaData* userData, ref JobHandle dependency, out JobHandle jobHandle);

        [DllImport(nativejobslib)]
        internal static extern unsafe void ScheduleJobBatchParallelFor(IntPtr scheduler, IntPtr func, JobMetaData* userData, int arrayLength, int innerloopBatchCount, IntPtr completionFunc, ref JobHandle dependency, out JobHandle jobHandle);

        [DllImport(nativejobslib)]
        internal static extern unsafe void ScheduleMultiDependencyJob(ref JobHandle fence, IntPtr dispatch, JobHandle* dependencies, int fenceCount);

        [DllImport(nativejobslib)]
        internal static extern void ScheduleBatchedJobs(IntPtr scheduler);

        [DllImport(nativejobslib, EntryPoint = "ScheduleBatchedJobsAndComplete")]
        internal static extern void ScheduleBatchedJobsAndCompleteInternal(IntPtr scheduler, out JobHandle jobHandle);

        [DllImport(nativejobslib, EntryPoint = "IsCompleted")]
        internal static extern int IsCompletedInternal(IntPtr scheduler, ref JobHandle jobHandle);

        [DllImport(nativejobslib, EntryPoint = "IsMainThread")]
        internal static extern int IsMainThreadInternal();

        [DllImport(nativejobslib, EntryPoint = "IsExecutingJob")]
        internal static extern int IsExecutingJobInternal();

#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [DllImport(nativejobslib)]
        internal static unsafe extern void DebugDidScheduleJob(ref JobHandle jobHandle, JobHandle* dependsOn, int dependsOnCount);

        [DllImport(nativejobslib)]
        internal static extern void DebugDidSyncFence(ref JobHandle jobHandle);

        [DllImport(nativejobslib)]
        internal static extern void EnableJobsDebugger(int enable);

        [DllImport(nativejobslib)]
        internal static extern int IsJobsDebuggerEnabled();

        [DllImport(nativejobslib, EntryPoint = "CheckDidSyncFence")]
        internal static extern int CheckDidSyncFenceInternal(ref JobHandle jobHandle);
        public static bool CheckDidSyncFence(ref JobHandle jobHandle)
        {
            return CheckDidSyncFenceInternal(ref jobHandle) != 0;
        }

        [DllImport(nativejobslib, EntryPoint = "CheckFenceIsDependencyOrDidSyncFence")]
        internal static extern int CheckFenceIsDependencyOrDidSyncFenceInternal(ref JobHandle dependency, ref JobHandle dependsOn);
#endif

        public static unsafe bool CheckFenceIsDependencyOrDidSyncFence(ref JobHandle dependency, ref JobHandle dependsOn)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            return CheckFenceIsDependencyOrDidSyncFenceInternal(ref dependency, ref dependsOn) != 0;
#else
            return true;
#endif
        }

        // The following are needed regardless if we are in single or multi-threaded environment
        public static bool IsExecutingJob
        {
            get
            {
#if !UNITY_SINGLETHREADED_JOBS
                return (IsExecutingJobInternal() != 0) || (UnsafeUtility.GetInJob() > 0);
#else
                return UnsafeUtility.GetInJob() > 0;
#endif
            }
        }

        public static bool IsMainThread()
        {
#if !UNITY_SINGLETHREADED_JOBS
            return IsMainThreadInternal() != 0;
#else
            return true;
#endif
        }

        public static void ScheduleBatchedJobsAndComplete(ref JobHandle jobHandle)
        {
#if !UNITY_SINGLETHREADED_JOBS
            ScheduleBatchedJobsAndCompleteInternal(BatchScheduler, out jobHandle);
#else
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DebugDidSyncFence(ref jobHandle);
            jobHandle = default;
#endif
#endif
        }

        public static bool IsCompleted(ref JobHandle jobHandle)
        {
#if !UNITY_SINGLETHREADED_JOBS
            // Fast path
            if (JobsUtility.JobQueue == IntPtr.Zero || jobHandle.JobGroup == IntPtr.Zero)
                return true;

            return IsCompletedInternal(BatchScheduler, ref jobHandle) != 0;
#else
            return true;
#endif
        }

        public static unsafe IntPtr CreateJobReflectionData(Type type, Type _, JobType jobType,
            Delegate executeDelegate,
            Delegate cleanupDelegate = null,
            ManagedJobForEachDelegate codegenExecuteDelegate = null,
            ManagedJobDelegate codegenCleanupDelegate = null,
            int codegenUnmanagedJobSize = -1,
            ManagedJobMarshalDelegate codegenMarshalToBurstDelegate = null)
        {
            return CreateJobReflectionData(type, _, executeDelegate, cleanupDelegate, codegenExecuteDelegate, codegenCleanupDelegate, codegenUnmanagedJobSize, codegenMarshalToBurstDelegate);
        }

        public static unsafe IntPtr CreateJobReflectionData(Type type,
            Delegate executeDelegate,
            Delegate cleanupDelegate = null,
            ManagedJobForEachDelegate codegenExecuteDelegate = null,
            ManagedJobDelegate codegenCleanupDelegate = null,
            int codegenUnmanagedJobSize = -1,
            ManagedJobMarshalDelegate codegenMarshalToBurstDelegate = null)
        {
            return CreateJobReflectionData(type, type, executeDelegate, cleanupDelegate, codegenExecuteDelegate, codegenCleanupDelegate, codegenUnmanagedJobSize, codegenMarshalToBurstDelegate);
        }

        public static unsafe IntPtr CreateJobReflectionData(Type type, Type _,
            Delegate executeDelegate,
            Delegate cleanupDelegate = null,
            ManagedJobForEachDelegate codegenExecuteDelegate = null,
            ManagedJobDelegate codegenCleanupDelegate = null,
            int codegenUnmanagedJobSize = -1,
            ManagedJobMarshalDelegate codegenMarshalToBurstDelegate = null)
        {
            // Tiny doesn't use this on any codepath currently; may need future support for custom jobs.
            Assert.IsTrue(cleanupDelegate == null, "Runtime needs support for cleanup delegates in jobs.");

            Assert.IsTrue(codegenExecuteDelegate != null, "Code gen should have supplied an execute wrapper.");
#if ENABLE_UNITY_COLLECTIONS_CHECKS && !UNITY_DOTSRUNTIME_IL2CPP
            Assert.IsTrue((codegenUnmanagedJobSize != -1 && codegenMarshalToBurstDelegate != null) || (codegenUnmanagedJobSize == -1 && codegenMarshalToBurstDelegate == null), "Code gen should have supplied a marshal wrapper.");
#endif

            ReflectionDataProxy* reflectionDataPtr = (ReflectionDataProxy*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<ReflectionDataProxy>(),
                UnsafeUtility.AlignOf<ReflectionDataProxy>(), Allocator.Persistent);

            var reflectionData = new ReflectionDataProxy();

            // Protect against garbage collector relocating delegate
            ReflectionDataStore store = new ReflectionDataStore(executeDelegate, codegenCleanupDelegate, codegenExecuteDelegate, codegenMarshalToBurstDelegate);
            store.next = Managed.reflectionDataStoreRoot;
            Managed.reflectionDataStoreRoot = store;

            reflectionData.ExecuteFunctionPtr = store.CodeGenExecuteFunctionPtr;
            if (codegenCleanupDelegate != null)
                reflectionData.CleanupFunctionPtr = store.CodeGenCleanupFunctionPtr;

#if ENABLE_UNITY_COLLECTIONS_CHECKS && !UNITY_DOTSRUNTIME_IL2CPP
            reflectionData.UnmanagedSize = codegenUnmanagedJobSize;
            if(codegenUnmanagedJobSize != -1)
                reflectionData.MarshalToBurstFunctionPtr = store.CodeGenMarshalToBurstFunctionPtr;
#endif

            UnsafeUtility.CopyStructureToPtr(ref reflectionData, reflectionDataPtr);

            return (IntPtr)reflectionDataPtr;
        }

#if UNITY_SINGLETHREADED_JOBS
        public static int GetDefaultIndicesPerPhase(int arrayLength)
        {
            return Math.Max(arrayLength, 1);
        }
#else
        public static int GetDefaultIndicesPerPhase(int arrayLength)
        {
            return (JobWorkerCount > 0) ? Math.Max((arrayLength + (JobWorkerCount - 1)) / JobWorkerCount, 1) : 1;
        }
#endif

        // TODO: Currently, the actual work stealing code sits in (big) Unity's native code w/ some dependencies
        // This is implemented trying to use the same code pattern:
        //        while (true)
        //        {
        //            if (!JobsUtility.GetWorkStealingRange(ref ranges, jobIndex, out int begin, out int end))
        //                break;
        public static bool GetWorkStealingRange(ref JobRanges ranges, int jobIndex, out int begin, out int end)
        {
            if (ranges.State == WorkStealingState.Done)
            {
                begin = 0;
                end = 0;
                return false;
            }

#if !UNITY_SINGLETHREADED_JOBS
            if (ranges.runOnMainThread > 0) {
#endif
                // There's only one thread, and the IndicesPerPhase don't have much meaning.
                // Do everything in one block of work.
                begin = 0;
                end = ranges.ArrayLength;
                ranges.State = WorkStealingState.Done;
                return end > begin;
#if !UNITY_SINGLETHREADED_JOBS
            }

            // Divide the work equally.
            // TODO improve by accounting for the indices per phase.
            int nWorker = JobWorkerCount > 0 ? JobWorkerCount : 1;
            begin = jobIndex * ranges.ArrayLength / nWorker;
            end = (jobIndex + 1) * ranges.ArrayLength / nWorker;

            if (end > ranges.ArrayLength)
                end = ranges.ArrayLength;
            if (jobIndex == nWorker - 1)
                end = ranges.ArrayLength;

            ranges.State = WorkStealingState.Done;
            return end > begin;
#endif
        }

        // Used by code-gen, and nothing but code-gen should call it.
        static unsafe int CountFromDeferredData(void* deferredCountData)
        {
            // The initial count (which is what tiny only uses) is the `int` past the first `void*`.
            int count = *((int*) ((byte*) deferredCountData + sizeof(void*)));
            return count;
        }

        public static unsafe JobHandle ScheduleParallelFor(ref JobScheduleParameters parameters, int arrayLength, int innerloopBatchCount)
        {
            return ScheduleParallelForInternal(ref parameters, arrayLength, null, innerloopBatchCount);
        }

        // Code gen will find where atomicSafetyHandlePtr comes from caller-side and validate it. Internally, it's not
        // used further.
        public static unsafe JobHandle ScheduleParallelForDeferArraySize(ref JobScheduleParameters parameters,
            int innerloopBatchCount, void* getInternalListDataPtrUnchecked, void* atomicSafetyHandlePtr)
        {
            return ScheduleParallelForInternal(ref parameters, -1, getInternalListDataPtrUnchecked, innerloopBatchCount);
        }

        static unsafe void CopyMetaDataToJobData(ref JobMetaData jobMetaData, JobMetaData* managedJobDataPtr, JobMetaData* unmanagedJobData)
        {
            jobMetaData.managedPtr = managedJobDataPtr;
            jobMetaData.unmanagedPtr = unmanagedJobData;
            if (unmanagedJobData != null)
                UnsafeUtility.CopyStructureToPtr(ref jobMetaData, unmanagedJobData);
            if (managedJobDataPtr != null)
                UnsafeUtility.CopyStructureToPtr(ref jobMetaData, managedJobDataPtr);
        }

        static unsafe JobMetaData* AllocateJobHeapMemory(int jobSize, int n)
        {
            if (jobSize < 8) jobSize = 8;   // handles the odd case of empty job
            int metadataSize = UnsafeUtility.SizeOf<JobMetaData>();
            int allocSize = metadataSize + jobSize * n;
            void* mem = UnsafeUtility.Malloc(allocSize, 16, Allocator.TempJob);
            UnsafeUtility.MemClear(mem, allocSize);
            return (JobMetaData*)mem;
        }

#if UNITY_SINGLETHREADED_JOBS
        private struct FakeJobGroupIdKey { };
#if UNITY_DOTSRUNTIME64
        private readonly static SharedStatic<ulong> s_FakeJobGroupId = SharedStatic<ulong>.GetOrCreate<FakeJobGroupIdKey>();
        static internal IntPtr GetFakeJobGroupId()
        {
            s_FakeJobGroupId.Data++;
            return (IntPtr)s_FakeJobGroupId.Data;
        }
#else
        private readonly static SharedStatic<uint> s_FakeJobGroupId = SharedStatic<uint>.GetOrCreate<FakeJobGroupIdKey>();
        static internal IntPtr GetFakeJobGroupId()
        {
            s_FakeJobGroupId.Data++;
            return (IntPtr)s_FakeJobGroupId.Data;
        }
#endif
#endif

        static unsafe JobHandle ScheduleParallelForInternal(ref JobScheduleParameters parameters, int arrayLength, void* deferredDataPtr, int innerloopBatchCount)
        {
            // Ensure the user has not set the schedule mode to a currently unsupported type
            Assert.IsTrue(parameters.ScheduleMode != ScheduleMode.Single);

            // May provide an arrayLength (>=0) OR a deferredDataPtr, but both is senseless.
            Assert.IsTrue((arrayLength >= 0 && deferredDataPtr == null) || (arrayLength < 0 && deferredDataPtr != null));

            UnsafeUtility.AssertHeap(parameters.JobDataPtr);
            UnsafeUtility.AssertHeap(parameters.ReflectionData);
            ReflectionDataProxy jobReflectionData = UnsafeUtility.AsRef<ReflectionDataProxy>(parameters.ReflectionData);

            Assert.IsFalse(jobReflectionData.ExecuteFunctionPtr.ToPointer() == null);
            Assert.IsFalse(jobReflectionData.CleanupFunctionPtr.ToPointer() == null);
#if ENABLE_UNITY_COLLECTIONS_CHECKS && !UNITY_DOTSRUNTIME_IL2CPP
            Assert.IsTrue((jobReflectionData.UnmanagedSize != -1 && jobReflectionData.MarshalToBurstFunctionPtr != IntPtr.Zero)
                || (jobReflectionData.UnmanagedSize == -1 && jobReflectionData.MarshalToBurstFunctionPtr == IntPtr.Zero));
#endif
            JobMetaData* managedJobDataPtr = parameters.JobDataPtr;
            JobMetaData jobMetaData;

            UnsafeUtility.CopyPtrToStructure(parameters.JobDataPtr, out jobMetaData);
            Assert.IsTrue(jobMetaData.jobDataSize > 0); // set by JobScheduleParameters
            Assert.IsTrue(sizeof(JobRanges) <= JobMetaData.kJobMetaDataIsParallelOffset);
            jobMetaData.JobRanges.ArrayLength = (arrayLength >= 0) ? arrayLength : 0;
            jobMetaData.JobRanges.IndicesPerPhase = (arrayLength >= 0) ? GetDefaultIndicesPerPhase(arrayLength) : 1; // TODO indicesPerPhase isn't actually used, except as a flag.
            // If this is set to -1 by codegen, that indicates an error if we schedule the job as parallel for because
            // it potentially consists of write operations which are not parallel compatible
            if (jobMetaData.isParallelFor == -1)
                throw new InvalidOperationException("Parallel writing not supported in this job. Parallel scheduling invalid.");
            jobMetaData.isParallelFor = 1;
            jobMetaData.deferredDataPtr = deferredDataPtr;

            JobHandle jobHandle = default;
#if !UNITY_SINGLETHREADED_JOBS
            bool runSingleThreadSynchronous =
                parameters.ScheduleMode == ScheduleMode.RunOnMainThread ||
                parameters.ScheduleMode == ScheduleMode.ScheduleOnMainThread;
#else
            bool runSingleThreadSynchronous = true;
#endif

            jobMetaData.JobRanges.runOnMainThread = runSingleThreadSynchronous ? 1 : 0;

            if (runSingleThreadSynchronous)
            {
                bool syncNow = parameters.ScheduleMode == ScheduleMode.Run || parameters.ScheduleMode == ScheduleMode.RunOnMainThread;
#if UNITY_SINGLETHREADED_JOBS
                // Nativejobs needs further support in creating a JobHandle not linked to an actual job in order to support this correctly
                // in multithreaded builds
                if (!syncNow)
                {
                    jobHandle.JobGroup = GetFakeJobGroupId();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    DebugDidScheduleJob(ref jobHandle, (JobHandle*)UnsafeUtility.AddressOf(ref parameters.Dependency), 1);
#endif
                }
#endif

                parameters.Dependency.Complete();
                UnsafeUtility.SetInJob(1);
                try
                {
                    // We assume there are no non-blittable fields in a bursted job (i.e. DisposeSentinel) if
                    // collections checks are not enabled
#if ENABLE_UNITY_COLLECTIONS_CHECKS && !UNITY_DOTSRUNTIME_IL2CPP

                    // If the job was bursted, and the job structure contained non-blittable fields, the UnmanagedSize will
                    // be something other than -1 meaning we need to marshal the managed representation before calling the ExecuteFn
                    if (jobReflectionData.UnmanagedSize != -1)
                    {
                        JobMetaData* unmanagedJobData = AllocateJobHeapMemory(jobReflectionData.UnmanagedSize, 1);

                        void* dst = (byte*) unmanagedJobData + sizeof(JobMetaData);
                        void* src = (byte*) managedJobDataPtr + sizeof(JobMetaData);

                        // In the single threaded case, this is synchronous execution.
                        UnsafeUtility.EnterTempScope();
                        try
                        {
                            UnsafeUtility.CallFunctionPtr_pp(jobReflectionData.MarshalToBurstFunctionPtr.ToPointer(), dst, src);

                            CopyMetaDataToJobData(ref jobMetaData, managedJobDataPtr, unmanagedJobData);

                            UnsafeUtility.CallFunctionPtr_pi(jobReflectionData.ExecuteFunctionPtr.ToPointer(), unmanagedJobData, k_MainThreadWorkerIndex);
                            UnsafeUtility.CallFunctionPtr_p(jobReflectionData.CleanupFunctionPtr.ToPointer(), unmanagedJobData);
                        }
                        finally
                        {
                            UnsafeUtility.ExitTempScope();
                        }
                    }
                    else
#endif
                    {
                        CopyMetaDataToJobData(ref jobMetaData, managedJobDataPtr, null);

                        // In the single threaded case, this is synchronous execution.
                        UnsafeUtility.EnterTempScope();
                        try
                        {
                            UnsafeUtility.CallFunctionPtr_pi(jobReflectionData.ExecuteFunctionPtr.ToPointer(), managedJobDataPtr, k_MainThreadWorkerIndex);
                            UnsafeUtility.CallFunctionPtr_p(jobReflectionData.CleanupFunctionPtr.ToPointer(), managedJobDataPtr);
                        }
                        finally
                        {
                            UnsafeUtility.ExitTempScope();
                        }
                    }
                }
                finally
                {
                    UnsafeUtility.SetInJob(0);
                }

                return jobHandle;
            }
#if !UNITY_SINGLETHREADED_JOBS
#if ENABLE_UNITY_COLLECTIONS_CHECKS && !UNITY_DOTSRUNTIME_IL2CPP
            // If the job was bursted, and the job structure contained non-blittable fields, the UnmanagedSize will
            // be something other than -1 meaning we need to marshal the managed representation before calling the ExecuteFn
            if (jobReflectionData.UnmanagedSize != -1)
            {
                int nWorker = JobWorkerCount > 1 ? JobWorkerCount : 1;
                JobMetaData* unmanagedJobData = AllocateJobHeapMemory(jobReflectionData.UnmanagedSize, nWorker);

                for (int i = 0; i < nWorker; i++)
                {
                    void* dst = (byte*)unmanagedJobData + sizeof(JobMetaData) + i * jobReflectionData.UnmanagedSize;
                    void* src = (byte*)managedJobDataPtr + sizeof(JobMetaData) + i * jobMetaData.jobDataSize;
                    UnsafeUtility.CallFunctionPtr_pp(jobReflectionData.MarshalToBurstFunctionPtr.ToPointer(), dst, src);
                }

                // Need to change the jobDataSize so the job will have the correct stride when finding
                // the correct jobData for a thread.
                JobMetaData unmanagedJobMetaData = jobMetaData;
                unmanagedJobMetaData.jobDataSize = jobReflectionData.UnmanagedSize;
                CopyMetaDataToJobData(ref unmanagedJobMetaData, managedJobDataPtr, unmanagedJobData);

                jobHandle = ScheduleJobParallelFor(jobReflectionData.ExecuteFunctionPtr,
                    jobReflectionData.CleanupFunctionPtr, unmanagedJobData, arrayLength,
                    innerloopBatchCount, parameters.Dependency);
            }
            else
#endif
            {
                CopyMetaDataToJobData(ref jobMetaData, managedJobDataPtr, null);
                jobHandle = ScheduleJobParallelFor(jobReflectionData.ExecuteFunctionPtr,
                    jobReflectionData.CleanupFunctionPtr, parameters.JobDataPtr, arrayLength,
                    innerloopBatchCount, parameters.Dependency);
            }

            if (parameters.ScheduleMode == ScheduleMode.Run)
            {
                jobHandle.Complete();
            }
#endif
            return jobHandle;
        }

        public static unsafe JobHandle Schedule(ref JobScheduleParameters parameters)
        {
            // Ensure the user has not set the schedule mode to a currently unsupported type
            Assert.IsTrue(parameters.ScheduleMode != ScheduleMode.Single);

            // Heap memory must be passed to schedule, so that Cleanup can free() it.
            UnsafeUtility.AssertHeap(parameters.JobDataPtr);
            UnsafeUtility.AssertHeap(parameters.ReflectionData);
            ReflectionDataProxy jobReflectionData = UnsafeUtility.AsRef<ReflectionDataProxy>(parameters.ReflectionData);

            Assert.IsTrue(jobReflectionData.ExecuteFunctionPtr.ToPointer() != null);
            Assert.IsTrue(jobReflectionData.CleanupFunctionPtr.ToPointer() != null);

#if ENABLE_UNITY_COLLECTIONS_CHECKS && !UNITY_DOTSRUNTIME_IL2CPP
            Assert.IsTrue((jobReflectionData.UnmanagedSize != -1 && jobReflectionData.MarshalToBurstFunctionPtr != IntPtr.Zero)
                || (jobReflectionData.UnmanagedSize == -1 && jobReflectionData.MarshalToBurstFunctionPtr == IntPtr.Zero));
#endif

            JobMetaData* managedJobDataPtr = parameters.JobDataPtr;
            JobMetaData jobMetaData;

            Assert.IsTrue(sizeof(JobRanges) <= JobMetaData.kJobMetaDataIsParallelOffset);
            UnsafeUtility.CopyPtrToStructure(managedJobDataPtr, out jobMetaData);
            Assert.IsTrue(jobMetaData.jobDataSize > 0); // set by JobScheduleParameters
            jobMetaData.managedPtr = managedJobDataPtr;
            jobMetaData.isParallelFor = 0;
            UnsafeUtility.CopyStructureToPtr(ref jobMetaData, managedJobDataPtr);

            JobHandle jobHandle = default;
#if !UNITY_SINGLETHREADED_JOBS
            bool runSingleThreadSynchronous =
                parameters.ScheduleMode == ScheduleMode.RunOnMainThread ||
                parameters.ScheduleMode == ScheduleMode.Run ||
                parameters.ScheduleMode == ScheduleMode.ScheduleOnMainThread;
#else
            bool runSingleThreadSynchronous = true;
#endif

            if (runSingleThreadSynchronous)
            {
                bool syncNow = parameters.ScheduleMode == ScheduleMode.Run || parameters.ScheduleMode == ScheduleMode.RunOnMainThread;

#if UNITY_SINGLETHREADED_JOBS
                if (!syncNow)
                {
                    jobHandle.JobGroup = GetFakeJobGroupId();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    DebugDidScheduleJob(ref jobHandle, (JobHandle*)UnsafeUtility.AddressOf(ref parameters.Dependency), 1);
#endif
                }
#endif

                parameters.Dependency.Complete();
                UnsafeUtility.SetInJob(1);
                try
                {
                    // We assume there are no non-blittable fields in a bursted job (i.e. DisposeSentinel) if
                    // collections checks are not enabled
#if ENABLE_UNITY_COLLECTIONS_CHECKS && !UNITY_DOTSRUNTIME_IL2CPP

                    // If the job was bursted, and the job structure contained non-blittable fields, the UnmanagedSize will
                    // be something other than -1 meaning we need to marshal the managed representation before calling the ExecuteFn
                    if (jobReflectionData.UnmanagedSize != -1)
                    {
                        JobMetaData* unmanagedJobData = AllocateJobHeapMemory(jobReflectionData.UnmanagedSize, 1);

                        void* dst = (byte*) unmanagedJobData + sizeof(JobMetaData);
                        void* src = (byte*) managedJobDataPtr + sizeof(JobMetaData);

                        UnsafeUtility.EnterTempScope();
                        try
                        {
                            UnsafeUtility.CallFunctionPtr_pp(jobReflectionData.MarshalToBurstFunctionPtr.ToPointer(), dst, src);

                            // In the single threaded case, this is synchronous execution.
                            // The cleanup *is* bursted, so pass in the unmanangedJobDataPtr
                            CopyMetaDataToJobData(ref jobMetaData, managedJobDataPtr, unmanagedJobData);

                            UnsafeUtility.CallFunctionPtr_pi(jobReflectionData.ExecuteFunctionPtr.ToPointer(), unmanagedJobData, k_MainThreadWorkerIndex);
                        }
                        finally
                        {
                            UnsafeUtility.ExitTempScope();                              
                        }
                    }
                    else
#endif
                    {
                        CopyMetaDataToJobData(ref jobMetaData, managedJobDataPtr, null);

                        // In the single threaded case, this is synchronous execution.
                        UnsafeUtility.EnterTempScope();
                        try
                        {
                            UnsafeUtility.CallFunctionPtr_pi(jobReflectionData.ExecuteFunctionPtr.ToPointer(), managedJobDataPtr, k_MainThreadWorkerIndex);
                        }
                        finally
                        {
                            UnsafeUtility.ExitTempScope();
                        }
                    }
                }
                finally
                {
                    UnsafeUtility.SetInJob(0);
                }

                return jobHandle;
            }
#if !UNITY_SINGLETHREADED_JOBS
#if ENABLE_UNITY_COLLECTIONS_CHECKS && !UNITY_DOTSRUNTIME_IL2CPP
            // If the job was bursted, and the job structure contained non-blittable fields, the UnmanagedSize will
            // be something other than -1 meaning we need to marshal the managed representation before calling the ExecuteFn.
            // This time though, we have a whole bunch of jobs that need to be processed.
            if (jobReflectionData.UnmanagedSize != -1)
            {
                JobMetaData* unmanagedJobData = AllocateJobHeapMemory(jobReflectionData.UnmanagedSize, 1);

                void* dst = (byte*)unmanagedJobData + sizeof(JobMetaData);
                void* src = (byte*)managedJobDataPtr + sizeof(JobMetaData);
                UnsafeUtility.CallFunctionPtr_pp(jobReflectionData.MarshalToBurstFunctionPtr.ToPointer(), dst, src);

                CopyMetaDataToJobData(ref jobMetaData, managedJobDataPtr, unmanagedJobData);
                jobHandle = ScheduleJob(jobReflectionData.ExecuteFunctionPtr, unmanagedJobData, parameters.Dependency);
            }
            else
#endif
            {
                CopyMetaDataToJobData(ref jobMetaData, managedJobDataPtr, null);
                jobHandle = ScheduleJob(jobReflectionData.ExecuteFunctionPtr, parameters.JobDataPtr, parameters.Dependency);
            }
#endif
            return jobHandle;
        }

        public struct MinMax
        {
            public int Min;
            public int Max;
        }

        public static unsafe MinMax PatchBufferMinMaxRanges(IntPtr bufferRangePatchData, void* jobdata, int startIndex, int rangeSize)
        {
            return new MinMax
            {
                Min = startIndex,
                Max = startIndex + rangeSize - 1
            };
        }
    }

    public static class JobHandleUnsafeUtility
    {
        public static unsafe JobHandle CombineDependencies(JobHandle* jobs, int count)
        {
            var fence = new JobHandle();
#if UNITY_SINGLETHREADED_JOBS
            fence.JobGroup = JobsUtility.GetFakeJobGroupId();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            JobsUtility.DebugDidScheduleJob(ref fence, jobs, count);
#endif
#else
            JobsUtility.ScheduleMultiDependencyJob(ref fence, JobsUtility.BatchScheduler, jobs, count);
#endif
            return fence;
        }
    }
}
