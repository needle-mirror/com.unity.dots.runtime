using System;
using System.Threading;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Tiny.IO;
using UnityEngine.Assertions;

namespace Unity.IO.LowLevel.Unsafe
{
    internal enum ReadStatus
    {
        /// <summary>
        ///   <para>All the ReadCommand operations completed successfully.</para>
        /// </summary>
        Complete,
        /// <summary>
        ///   <para>The read operation is in progress.</para>
        /// </summary>
        InProgress,
        /// <summary>
        ///   <para>One or more of the ReadCommand operations failed.</para>
        /// </summary>
        Failed,
    }

    internal struct ReadCommand
    {
        /// <summary>
        ///   <para>The buffer that receives the read data.</para>
        /// </summary>
        public unsafe void* Buffer;
        /// <summary>
        ///   <para>The offset where the read begins, within the file.</para>
        /// </summary>
        public long Offset;
        /// <summary>
        ///   <para>The size of the read in bytes.</para>
        /// </summary>
        public long Size;
    }

    internal struct ReadHandle : IDisposable
    {
        internal AsyncOp mAsyncOp;
        internal JobHandle mJobHandle;

        /// <summary>
        ///   <para>Check if the ReadHandle is valid.</para>
        /// </summary>
        /// <returns>
        ///   <para>True if the ReadHandle is valid.</para>
        /// </returns>
        public bool IsValid()
        {
            return mAsyncOp.IsCreated;
        }

        /// <summary>
        ///   <para>Disposes the ReadHandle. Use this to free up internal resources for reuse.</para>
        /// </summary>
        public void Dispose()
        {
            mAsyncOp.Dispose();
        }

        /// <summary>
        ///   <para>JobHandle that completes when the read operation completes.</para>
        /// </summary>
        public JobHandle JobHandle
        {
            get
            {
                return mJobHandle;
            }
        }

        /// <summary>
        ///   <para>Current state of the read operation.</para>
        /// </summary>
        public ReadStatus Status
        {
            get
            {
                switch (mAsyncOp.GetStatus())
                {
                    case AsyncOp.Status.Success:
                        return ReadStatus.Complete;
                    case AsyncOp.Status.NotStarted:
                    case AsyncOp.Status.InProgress:
                        return ReadStatus.InProgress;
                    case AsyncOp.Status.Failure:
                    default:
                        return ReadStatus.Failed;
                }
            }
        }
    }

#if !UNITY_SINGLETHREADED_JOBS
    struct ReadJob : IJob
    {
        public AsyncOp m_Op;

        public void Execute()
        {
            AsyncOp.Status status = AsyncOp.Status.NotStarted;
            while((status = m_Op.GetStatus()) <= AsyncOp.Status.InProgress)
                Baselib.LowLevel.Binding.Baselib_Thread_YieldExecution();

            if (status == AsyncOp.Status.Failure)
            {
                switch (m_Op.GetErrorStatus())
                {
                    case AsyncOp.ErrorStatus.FileNotFound:
                        throw new ArgumentException("Could not find specified file when reading scene in ReadJob");
                    case AsyncOp.ErrorStatus.Unknown:
                    default:
                        throw new ArgumentException("Unknown error trying to read scene during ReadJob");
                }
            }
        }
    }
#endif

    internal static class AsyncReadManager
    {
        /// <summary>
        ///   <para>Issues an asynchronous file read operation. Returns a ReadHandle.</para>
        /// </summary>
        /// <param name="filename">The filename to read from.</param>
        /// <param name="readCmds">A pointer to an array of ReadCommand structs that specify offset, size, and destination buffer.</param>
        /// <param name="readCmdCount">The number of read commands pointed to by readCmds.</param>
        /// <returns>
        ///   <para>Used to monitor the progress and status of the read command.</para>
        /// </returns>
        public static unsafe ReadHandle Read(
          string filename,
          ReadCommand* readCmds,
          uint readCmdCount)
        {
            // For now just assume one read operation and assume it's the whole file
            // since we only support the AsyncReadManager in the Unity.Scenes which has no other usecase
            Assert.IsTrue(readCmdCount == 1);
            Assert.IsTrue(readCmds[0].Offset == 0);

            ReadHandle handle = default;
            AsyncOp asyncOp;

            // Always provide a new buffer for reads as the data will be compressed and needs to be decoded into the
            // passed in buffer here.
            asyncOp = IOService.RequestAsyncRead(filename);

            handle.mAsyncOp = asyncOp;

#if !UNITY_SINGLETHREADED_JOBS
            handle.mJobHandle = new ReadJob()
            {
                m_Op = handle.mAsyncOp
            }.Schedule();
#endif

            return handle;
        }
    }
}
