#if UNITY_DOTSRUNTIME    // Unity.Runtime has its own copy of this; in the future would like to commonize.

using System;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Jobs
{
    [JobProducerType(typeof(IJobForExtensions.JobProducer<>))]
    public interface IJobFor
    {
        void Execute(int index);
    }

    public static class IJobForExtensions
    {
        internal struct JobProducer<T> where T : struct, IJobFor
        {
            static IntPtr s_jobReflectionData;

            public static IntPtr Initialize()
            {
                if (s_jobReflectionData == IntPtr.Zero)
                    s_jobReflectionData = JobsUtility.CreateJobReflectionData(typeof(void), typeof(void), (ExecuteJobFunction)Execute);
                return s_jobReflectionData;
            }

            public delegate void ExecuteJobFunction(ref T jobData, IntPtr additionalPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex);

            public static unsafe void Execute(ref T jobData, IntPtr additionalPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex)
            {
                while (true)
                {
                    int begin;
                    int end;
                    if (!JobsUtility.GetWorkStealingRange(ref ranges, jobIndex, out begin, out end))
                        break;

                    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                    JobsUtility.PatchBufferMinMaxRanges(bufferRangePatchData, UnsafeUtility.AddressOf(ref jobData), begin, end - begin);
                    #endif

                    var endThatCompilerCanSeeWillNeverChange = end;
                    for (var i = begin; i < endThatCompilerCanSeeWillNeverChange; ++i)
                        jobData.Execute(i);
                }
            }
        }

        unsafe public static JobHandle Schedule<T>(this T jobData, int arrayLength, JobHandle dependency) where T : struct, IJobFor
        {
            // https://unity3d.atlassian.net/browse/DOTSR-1888
            // var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), JobProducer<T>.Initialize(), dependency, ScheduleMode.Single);
            // IJobChunk uses both JobsUtility.ScheduleParallelFor and JobsUtility.Schedule, so that could be implemented.
            // However, it brings this class (which is rarely used) even more out of sync with the Unity.Runtime version, where
            // the better fix is to implement ScheduleMode.Single
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), JobProducer<T>.Initialize(), dependency, ScheduleMode.Parallel);
            return JobsUtility.ScheduleParallelFor(ref scheduleParams, arrayLength, arrayLength);
        }

        unsafe public static JobHandle ScheduleParallel<T>(this T jobData, int arrayLength, int innerloopBatchCount, JobHandle dependency) where T : struct, IJobFor
        {
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), JobProducer<T>.Initialize(), dependency, ScheduleMode.Parallel);
            return JobsUtility.ScheduleParallelFor(ref scheduleParams, arrayLength, innerloopBatchCount);
        }

        unsafe public static void Run<T>(this T jobData, int arrayLength) where T : struct, IJobFor
        {
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), JobProducer<T>.Initialize(), new JobHandle(), ScheduleMode.Run);
            JobsUtility.ScheduleParallelFor(ref scheduleParams, arrayLength, arrayLength);
        }
    }
}
#endif
