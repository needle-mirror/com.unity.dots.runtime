using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Tiny.Tests.Common;
using Unity.Tiny.IO;
using System.Threading;
using System.Diagnostics;
using static Unity.Tiny.IO.AsyncOp;

namespace Unity.Tiny.IO.Tests
{
    public class AsyncOpTests : TinyTestFixture
    {
        readonly string kTestDataPath = "testdata.dat";
        const int kTestDataSize = 10;

        [Test]
        public unsafe void AsyncOpSuccess()
        {
            var op = new AsyncOp();
            Assert.AreEqual(op.GetStatus(), Status.NotStarted);
            Assert.AreEqual(op.GetErrorStatus(), ErrorStatus.None);

            op = IOService.RequestAsyncRead(kTestDataPath);

            Assert.IsTrue(WaitForOpCompletion(op));
            Assert.AreEqual(op.GetStatus(), Status.Success);
            Assert.AreEqual(op.GetErrorStatus(), ErrorStatus.None);

            op.GetData(out var data, out var sizeInBytes);
            for (int i = 0; i < sizeInBytes; ++i)
            {
                Assert.AreEqual((int)data[i], i);
            }

            op.Dispose();
        }

        [Test]
        public unsafe void ManyAsyncOps()
        {
            const int kMaxIterations = 512;
            NativeHashMap<int, AsyncOp> opMap = new NativeHashMap<int, AsyncOp>(kMaxIterations, Allocator.Temp);

            for (int iter = 0; iter < kMaxIterations; ++iter)
            {
                var op = IOService.RequestAsyncRead(kTestDataPath);
                Assert.IsTrue(opMap.TryAdd(iter, op));
            }

            const int kUnilateralTimeoutMS = 10_000;
            bool bTimedOut = false;
            Stopwatch unilateralStopwatch = new Stopwatch();
            unilateralStopwatch.Start();

            NativeArray<int> opsToComplete = opMap.GetKeyArray(Allocator.Temp);
            while (opsToComplete.Length > 0 && !bTimedOut)
            {
                for (int i = 0; i < opsToComplete.Length && !bTimedOut; ++i)
                {
                    var iter = opsToComplete[i];
                    Assert.IsTrue(opMap.TryGetValue(iter, out var op));

                    var opFinished = WaitForOpCompletion(op, 1);
                    bTimedOut = unilateralStopwatch.ElapsedMilliseconds >= kUnilateralTimeoutMS;
                    if (!opFinished)
                        continue;

                    opMap.Remove(iter);
                    Assert.AreEqual(op.GetStatus(), Status.Success);
                    Assert.AreEqual(op.GetErrorStatus(), ErrorStatus.None);

                    op.GetData(out var data, out var sizeInBytes);
                    for (int j = 0; j < sizeInBytes; ++j)
                    {
                        Assert.AreEqual((int)data[j], j);
                    }

                    op.Dispose();
                }

                opsToComplete.Dispose();
                opsToComplete = opMap.GetKeyArray(Allocator.Temp);
            }

            Assert.IsFalse(bTimedOut, "Test has timedout");
            opsToComplete.Dispose();
            opMap.Dispose();
        }

        bool WaitForOpCompletion(AsyncOp op, int timeoutMS = 5000)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            while (op.GetStatus() == Status.InProgress && stopwatch.ElapsedMilliseconds < timeoutMS)
                Baselib.LowLevel.Binding.Baselib_Thread_YieldExecution();

            return op.GetStatus() != Status.InProgress;
        }

        [Test]
        public unsafe void AsyncOpFailure()
        {
            var op = new AsyncOp();
            Assert.AreEqual(op.GetStatus(), Status.NotStarted);
            Assert.AreEqual(op.GetErrorStatus(), ErrorStatus.None);

            op = IOService.RequestAsyncRead("IDoNotExist");
            Assert.IsTrue(WaitForOpCompletion(op));
            Assert.AreEqual(op.GetStatus(), Status.Failure);
            Assert.AreEqual(op.GetErrorStatus(), ErrorStatus.FileNotFound);

            op.Dispose();
        }

        [Test]
        public unsafe void WriteToExternalBuffer()
        {
            const int kBufferSize = 128;
            var buffer = (byte*) Memory.Unmanaged.Allocate(kBufferSize, 16, Allocator.Temp);
            var op = IOService.RequestAsyncRead(kTestDataPath, buffer, kBufferSize);

            Assert.IsTrue(WaitForOpCompletion(op));
            Assert.AreEqual(op.GetStatus(), Status.Success);
            Assert.AreEqual(op.GetErrorStatus(), ErrorStatus.None);

            op.GetData(out var data, out var sizeInBytes);
            Assert.IsTrue(data == buffer);
            Assert.IsTrue(sizeInBytes <= kBufferSize);

            for (int i = 0; i < sizeInBytes; ++i)
            {
                Assert.AreEqual((int)data[i], i);
            }

            op.Dispose(); // Frees the asyncop data (if owned)

            for (int i = 0; i < sizeInBytes; ++i)
            {
                Assert.AreEqual((int)buffer[i], i);
            }

            Memory.Unmanaged.Free(buffer, Allocator.Temp);
        }

        [Test]
        public unsafe void WriteToSmallExternalBuffer()
        {
            const int kBufferSize = 3;
            var buffer = (byte*)Memory.Unmanaged.Allocate(kBufferSize, 16, Allocator.Temp);
            buffer[0] = buffer[1] = buffer[2] = 0xCD;
            var op = IOService.RequestAsyncRead(kTestDataPath, buffer, kBufferSize-1);

            Assert.IsTrue(WaitForOpCompletion(op));
            Assert.AreEqual(op.GetStatus(), Status.Success);
            Assert.AreEqual(0, (int)buffer[0]);
            Assert.AreEqual(1, (int)buffer[1]);
            Assert.AreEqual(0xCD, (int)buffer[2]);

            Memory.Unmanaged.Free(buffer, Allocator.Temp);
        }
    }
}
