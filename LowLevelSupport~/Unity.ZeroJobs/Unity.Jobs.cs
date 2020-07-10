using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
using Unity.Development.JobsDebugger;
#endif
using UnityEngine.Assertions;

// This attribute must be outside of a namespace so its full name is "NativePInvokeCallbackAttribute"
/// <summary>
/// You likely don't want this attribute. This is an IL2CPP attribute to signify a pinvoke method is purely unmanaged 
/// and can thus be invoked directly from native code. As a result Marshal.GetFunctionPointerForDelegate(decoratedMethod) will
/// not generate a reverse callback wrapper to attach to the managed VM. Do not use this attribute if your
/// method requires managed data/types, doing so will result in undefined behaviour (almost certainly a crash at _some point_).
/// Most users likely want to use [MonoPInvokeCallback] instead as that will handle all necessary managed
/// type management when pinvoked.
/// </summary>
/// <remarks>
/// This attribute is useful for passing delegates of Bursted functions from managed code into native code where
/// they will be invoked. Without this attribute, IL2CPP will generate a reverse callback wrapper for invoking
/// the managed delegate from native code and will attach to the VM, which is unsupported in DOTS Runtime from native threads.
/// </remarks>
public class NativePInvokeCallbackAttribute : Attribute
{

}

namespace Unity.Jobs
{
    // Used by code gen. Do not remove.
    public interface IJobBase
    {
        // Generated functions from code gen.
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        // Called immediately preceeding Schedule to validate safety handles.
        unsafe int PrepareJobAtPreScheduleTimeFn_Gen(ref DependencyValidator data, ref JobHandle dependsOn, void* deferredSafety);
        // Called immediately proceeding Schedule to update safety nodes.
        void PrepareJobAtPostScheduleTimeFn_Gen(ref DependencyValidator data, ref JobHandle scheduledJob);
#endif
        // A wrapper around the user's Execute() method. This is called per-worker.
        void PrepareJobAtExecuteTimeFn_Gen(int jobIndex);
        // Free memory, performs any cleanup. Happens once after all workers have completed, on the same worker thread
        // as the last one to complete using that worker thread's copy of the job data.
        void CleanupJobFn_Gen();
        // Patches the min/max range of a parallel job so that multiple threads
        // aren't writing to the same indices.
        void PatchMinMax_Gen(JobsUtility.MinMax param);

        // Retrieves the ExecuteMethod.
        JobsUtility.ManagedJobForEachDelegate GetExecuteMethod_Gen();
        // Retrieves the UnmanagedJobSize
        int GetUnmanagedJobSize_Gen();
        // Retrieves the job's Marshal methods.
        JobsUtility.ManagedJobMarshalDelegate GetMarshalToBurstMethod_Gen();
        JobsUtility.ManagedJobMarshalDelegate GetMarshalFromBurstMethod_Gen();
        // If burst successfully compiled this job, this will return 1
        int IsBursted_Gen();
    }

    internal class MonoPInvokeCallbackAttribute : Attribute
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JobHandle
    {
        internal IntPtr JobGroup;
        internal uint Version;

        public bool Equals(JobHandle other)
        {
            return JobGroup == other.JobGroup && Version == other.Version;
        }

        public static void ScheduleBatchedJobs()
        {
#if !UNITY_SINGLETHREADED_JOBS
            JobsUtility.ScheduleBatchedJobs(JobsUtility.BatchScheduler);
#endif
        }

        public static void CompleteAll(NativeArray<JobHandle> jobs)
        {
            var combinedJobsHandle = CombineDependencies(jobs);
            combinedJobsHandle.Complete();
        }

        public void Complete()
        {
            JobsUtility.ScheduleBatchedJobsAndComplete(ref this);
        }

        public bool IsCompleted => JobsUtility.IsCompleted(ref this);

        public static bool CheckFenceIsDependencyOrDidSyncFence(JobHandle dependency, JobHandle dependsOn)
        {
            return JobsUtility.CheckFenceIsDependencyOrDidSyncFence(ref dependency, ref dependsOn);
        }

        public static unsafe JobHandle CombineDependencies(NativeArray<JobHandle> jobHandles)
        {
            var fence = new JobHandle();
#if UNITY_SINGLETHREADED_JOBS
            fence.JobGroup = JobsUtility.GetFakeJobGroupId();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            JobsUtility.DebugDidScheduleJob(ref fence, (JobHandle*)jobHandles.GetUnsafeReadOnlyPtr(), jobHandles.Length);
#endif
#else
            JobsUtility.ScheduleMultiDependencyJob(ref fence, JobsUtility.BatchScheduler, (JobHandle*)jobHandles.GetUnsafeReadOnlyPtr(), jobHandles.Length);
#endif
            return fence;
        }

        public static unsafe JobHandle CombineDependencies(JobHandle one, JobHandle two)
        {
            var fence = new JobHandle();
            var dependencies = stackalloc JobHandle[] { one, two };
#if UNITY_SINGLETHREADED_JOBS
            fence.JobGroup = JobsUtility.GetFakeJobGroupId();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            JobsUtility.DebugDidScheduleJob(ref fence, (JobHandle*)UnsafeUtility.AddressOf(ref dependencies[0]), 2);
#endif
#else
            JobsUtility.ScheduleMultiDependencyJob(ref fence, JobsUtility.BatchScheduler, (JobHandle*)UnsafeUtility.AddressOf(ref dependencies[0]), 2);
#endif
            return fence;
        }

        public static unsafe JobHandle CombineDependencies(JobHandle one, JobHandle two, JobHandle three)
        {
            var fence = new JobHandle();
            var dependencies = stackalloc JobHandle[] { one, two, three };
#if UNITY_SINGLETHREADED_JOBS
            fence.JobGroup = JobsUtility.GetFakeJobGroupId();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            JobsUtility.DebugDidScheduleJob(ref fence, (JobHandle*)UnsafeUtility.AddressOf(ref dependencies[0]), 3);
#endif
#else
            JobsUtility.ScheduleMultiDependencyJob(ref fence, JobsUtility.BatchScheduler, (JobHandle*)UnsafeUtility.AddressOf(ref dependencies[0]), 3);
#endif
            return fence;
        }
    }

    [JobProducerType(typeof(IJobExtensions.JobProducer<>))]
    public interface IJob
    {
        void Execute();
    }

    public static class IJobExtensions
    {
        internal struct JobProducer<T> where T : struct, IJob
        {
            static unsafe JobsUtility.ReflectionDataProxy* s_JobReflectionData;
            internal T JobData;

            public static unsafe JobsUtility.ReflectionDataProxy* Initialize()
            {
                if (s_JobReflectionData == null)
                {
                    s_JobReflectionData = (JobsUtility.ReflectionDataProxy*)JobsUtility.CreateJobReflectionData(typeof(JobProducer<T>), typeof(T),
                        JobType.Single,
                        (ExecuteJobFunction)Execute);
                }
                return s_JobReflectionData;
            }

            public delegate void ExecuteJobFunction(ref JobProducer<T> jobProducer, IntPtr additionalData,
                IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex);

            public static void Execute(ref JobProducer<T> jobProducer, IntPtr additionalData,
                IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex)
            {
                jobProducer.JobData.Execute();
            }
        }

        public static unsafe JobHandle Schedule<T>(this T jobData, JobHandle dependsOn = default(JobHandle))
            where T : struct, IJob
        {
            var jobProducer = new JobProducer<T>()
            {
                JobData = jobData
            };

            var scheduleParams = new JobsUtility.JobScheduleParameters(
                UnsafeUtility.AddressOf(ref jobProducer),
                JobProducer<T>.Initialize(),
                dependsOn,
                ScheduleMode.Batched);
            return JobsUtility.Schedule(ref scheduleParams);
        }

        public static void Run<T>(this T jobData) where T : struct, IJob
        {
            // can't just call: 'jobData.Execute();
            // because we need the setup/teardown.
            jobData.Schedule().Complete();
        }
    }

    [JobProducerType(typeof(IJobParallelForExtensions.JobParallelForProducer<>))]
    public interface IJobParallelFor
    {
        void Execute(int index);
    }

    public static class IJobParallelForExtensions
    {
        internal struct JobParallelForProducer<T> where T : struct, IJobParallelFor
        {
            static unsafe JobsUtility.ReflectionDataProxy* s_JobReflectionData;
            public T JobData;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            public int Sentinel;
#endif

            public static unsafe JobsUtility.ReflectionDataProxy* Initialize()
            {
                if (s_JobReflectionData == null)
                {
                    s_JobReflectionData = (JobsUtility.ReflectionDataProxy*)JobsUtility.CreateJobReflectionData(typeof(void), typeof(void),
                        JobType.ParallelFor,
                        (ExecuteJobFunction) Execute);
                }
                return s_JobReflectionData;
            }

            public delegate void ExecuteJobFunction(ref JobParallelForProducer<T> jobParallelForProducer, IntPtr additionalData,
                IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex);

            public static unsafe void Execute(ref JobParallelForProducer<T> jobParallelForProducer, IntPtr additionalData,
                IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Assert.AreEqual(jobParallelForProducer.Sentinel - ranges.ArrayLength, 37);
#endif
                // TODO Tiny doesn't currently support work stealing. https://unity3d.atlassian.net/browse/DOTSR-286

                while (true)
                {
                    if (!JobsUtility.GetWorkStealingRange(ref ranges, jobIndex, out int begin, out int end))
                        break;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    JobsUtility.PatchBufferMinMaxRanges(IntPtr.Zero, UnsafeUtility.AddressOf(ref jobParallelForProducer), begin, end - begin);
#endif
                    for (var i = begin; i < end; ++i)
                    {
                        jobParallelForProducer.JobData.Execute(i);
                    }
                }
            }
        }


        public static unsafe JobHandle Schedule<T>(this T jobData, int arrayLength, int innerloopBatchCount, JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobParallelFor
        {
            var parallelForJobProducer = new JobParallelForProducer<T>()
            {
                JobData = jobData,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Sentinel = 37 + arrayLength    // check that code is patched as expected
#endif
            };

            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref parallelForJobProducer),
                JobParallelForProducer<T>.Initialize(),
                dependsOn,
                ScheduleMode.Batched);
            return JobsUtility.ScheduleParallelFor(ref scheduleParams, arrayLength, innerloopBatchCount);
        }

        public static unsafe void Run<T>(this T jobData, int arrayLength) where T : struct, IJobParallelFor
        {
            var parallelForJobProducer = new JobParallelForProducer<T>()
            {
                JobData = jobData,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Sentinel = 37 + arrayLength    // check that code is patched as expected
#endif
            };

            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref parallelForJobProducer),
                JobParallelForProducer<T>.Initialize(),
                default,
                ScheduleMode.Run);
            JobsUtility.ScheduleParallelFor(ref scheduleParams, arrayLength, arrayLength);
        }
    }
}
