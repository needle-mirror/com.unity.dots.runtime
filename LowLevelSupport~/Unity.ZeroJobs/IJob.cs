using System;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Jobs
{
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
}
