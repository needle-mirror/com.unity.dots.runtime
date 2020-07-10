using System;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine.Assertions;

namespace Unity.Jobs
{
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
