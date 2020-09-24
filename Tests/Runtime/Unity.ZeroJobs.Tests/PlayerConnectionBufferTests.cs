using System;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Tiny.Tests.Common;
using Unity.Development.PlayerConnection;
using UnityEngine;
using static Unity.Baselib.LowLevel.Binding;
using UnityEngine.Networking.PlayerConnection;

// Tests for
// - data integrity
// - re-use and minimal allocation
// - large data support

namespace Unity.ZeroJobs.Tests
{
    public class PlayerConnectionBufferTestFixture : PlayerConnectionTestFixture
    {
        internal unsafe void BufferTestWriteInternal(ref MessageStream buf, string data)
        {
            int reserve = buf.m_Reserve;
            int bytes = data.Length * 2;
            int oldHeadBytes = buf.BufferRead->Size;
            int oldTotalBytes = buf.TotalBytes;

            unsafe
            {
                fixed (MessageStream* bufPtr = &buf)
                {
                    MessageStreamBuilder messageBuilder = new MessageStreamBuilder(bufPtr);

                    fixed (char* d = data)
                    {
                        messageBuilder.WriteRaw((byte*)d, bytes);
                    }
                }

                Assert.AreEqual(oldTotalBytes + bytes, buf.TotalBytes);

                if (buf.TotalBytes > reserve)
                {
                    // Read buffer should be unchanged (including if size was 0) once it's exceeded
                    Assert.AreEqual(oldHeadBytes, buf.BufferRead->Size);

                    // Write buffer should contain a single data write in a consecutive buffer
                    Assert.AreEqual(bytes, buf.BufferWrite->Size);
                    Assert.AreEqual(data, new string((char*)buf.BufferWrite->Buffer, 0, data.Length));
                }
                else
                {
                    // Read buffer should contain accumulative data writes as long as reserve size is not exceeded
                    Assert.AreEqual(oldHeadBytes + bytes, buf.BufferRead->Size);
                    Assert.AreEqual(data, new string((char*)buf.BufferRead->Buffer, oldTotalBytes / 2, data.Length));

                    // Write buffer should match read buffer as long as we are within reserve size
                    Assert.AreEqual(oldTotalBytes + bytes, buf.BufferWrite->Size);
                    Assert.AreEqual(data, new string((char*)buf.BufferWrite->Buffer, oldTotalBytes / 2, data.Length));

                    Assert.AreEqual(oldHeadBytes, oldTotalBytes);
                }
            }
        }

        internal void BufferTestReadInternal(ref MessageStream buf, string data, int start, int end, string fullData = null)
        {
            int bytes = data.Length * 2;
            string dataCheck;

            unsafe
            {
                fixed (MessageStream* bufPtr = &buf)
                {
                    MessageStreamBuilder messageBuilder = new MessageStreamBuilder(bufPtr);

                    fixed (char* d = data)
                    {
                        messageBuilder.WriteRaw((byte*)d, bytes);
                    }
                }

                byte[] arr = buf.ToByteArray(start, end);
                fixed (byte* a = arr)
                {
                    dataCheck = new string((char*)a, 0, arr.Length / 2);
                }
            }

            Assert.AreEqual((fullData ?? data).Substring(start / 2, (end - start) / 2), dataCheck);
        }

        internal unsafe void BufferTestRecycleInternal(string[] textData, int recycleSize)
        {
            MessageStream buf = new MessageStream(16);
            MessageStream.MessageStreamBuffer* recycleUntil = buf.BufferRead;
            int recycleOffset = recycleSize;
            string totalText = "";

            int sizeBeforeWriteBuffer = 0;
            int totalSize = 0;

            foreach (string text in textData)
            {
                totalText += text;
                bool incRecycleUntil = false;
                if (text.Length * 2 + buf.BufferWrite->Size > buf.BufferWrite->Capacity)
                {
                    sizeBeforeWriteBuffer += buf.BufferWrite->Size;
                    if (sizeBeforeWriteBuffer < recycleSize)
                    {
                        incRecycleUntil = true;
                        recycleOffset -= buf.BufferWrite->Size;
                    }
                }

                BufferTestWriteInternal(ref buf, text);

                if (incRecycleUntil)
                    recycleUntil = recycleUntil->Next;

                totalSize += text.Length * 2;
            }

            int preRecycleUntilSize = recycleUntil->Size;
            int preRecycleUntilNextSize = recycleUntil->Next != null ? recycleUntil->Next->Size : -1;
            int preRecycleWriteSize = buf.BufferWrite->Size;

            buf.RecyclePartialStream(recycleUntil, recycleOffset);

            // After recycling----

            // - Find first buffer with data remaining after recycling
            MessageStream.MessageStreamBuffer* firstBuffer = buf.BufferRead;
            while (firstBuffer->Size == 0 && firstBuffer->Next != null)
                firstBuffer = firstBuffer->Next;

            // - if recycleOffset == size of the last buffer, next buffer should be "First buffer remaining" and be full
            // - otherwise, ensure size in first buffer with data is as expected
            if (preRecycleUntilSize == recycleOffset)
                Assert.AreEqual(preRecycleUntilNextSize, firstBuffer->Size);
            else
                Assert.AreEqual(preRecycleUntilSize - recycleOffset, firstBuffer->Size);
            unsafe
            {
                Assert.AreEqual(totalText.Substring(recycleSize / 2, firstBuffer->Size / 2), new string((char*)firstBuffer->Buffer, 0, firstBuffer->Size / 2));
            }

            // - Check write buffer - if different than first buffer remaining it should be same as before
            //                      - otherwise should be pre-recycle write buffer size minus recycleOffset (which should work out the same as previous check)
            if (firstBuffer->Buffer == buf.BufferWrite->Buffer)
                Assert.AreEqual(preRecycleWriteSize - recycleOffset, buf.BufferWrite->Size);
            else
                Assert.AreEqual(preRecycleWriteSize, buf.BufferWrite->Size);
            unsafe
            {
                if (firstBuffer->Buffer == buf.BufferWrite->Buffer)
                    Assert.AreEqual(totalText.Substring(recycleSize / 2, (totalSize - recycleSize) / 2), new string((char*)buf.BufferWrite->Buffer, 0, buf.BufferWrite->Size / 2));
                else
                    Assert.AreEqual(totalText.Substring(sizeBeforeWriteBuffer / 2, buf.BufferWrite->Size / 2), new string((char*)buf.BufferWrite->Buffer, 0, buf.BufferWrite->Size / 2));
            }

            // - Check read buffer - if smaller than first data size it should be 0
            //                     - otherwise if recycled more than its size it should be 0
            //                     - otherwise it should be the first buffer with data remaining
            //                     - If recycled including full offset, read should be 0 and not have a next

            if (buf.BufferRead->Capacity < textData[0].Length * 2)
                Assert.AreEqual(0, buf.BufferRead->Size);
            else if (buf.BufferRead->Size <= recycleSize)
                Assert.AreEqual(0, buf.BufferRead->Size);
            else
                Assert.AreEqual(buf.BufferRead->Buffer, firstBuffer->Buffer);

            if (preRecycleUntilSize == recycleOffset)
            {
                Assert.AreEqual(0, buf.BufferRead->Size);
                Assert.AreEqual(IntPtr.Zero, (IntPtr)buf.BufferRead->Next);
            }

            unsafe
            {
                Assert.AreEqual(totalText.Substring(recycleSize / 2, buf.BufferRead->Size / 2), new string((char*)buf.BufferRead->Buffer, 0, buf.BufferRead->Size / 2));
            }

            // - Check other details
            Assert.AreEqual(totalSize - recycleSize, buf.TotalBytes);
        }
    }


    //---------------------------------------------------------------------------------------------------
    // BUFFER WRITE TESTS
    //---------------------------------------------------------------------------------------------------
    public class BufferWriteTests : PlayerConnectionBufferTestFixture
    {
        [Test]
        public void BufferTestWriteNullThrows()
        {
            unsafe
            {
                MessageStream buf = new MessageStream(16);
                MessageStreamBuilder messageBuilder = new MessageStreamBuilder(&buf);
                Assert.Throws<System.ArgumentNullException>(() => messageBuilder.WriteRaw(null, 4));
            }
        }

        [Test]
        public void BufferTestWriteNull0()
        {
            unsafe
            {
                MessageStream buf = new MessageStream(16);
                MessageStreamBuilder messageBuilder = new MessageStreamBuilder(&buf);
                messageBuilder.WriteRaw(null, 0);
                Assert.AreEqual(0, buf.TotalBytes);
            }
        }

        [Test]
        public void BufferTestWrite0()
        {
            unsafe
            {
                MessageStream buf = new MessageStream(16);
                MessageStreamBuilder messageBuilder = new MessageStreamBuilder(&buf);
                string data = "abcd";
                fixed (char* d = data)
                {
                    messageBuilder.WriteRaw((byte*)d, 0);
                }
                Assert.AreEqual(0, buf.TotalBytes);
            }
        }

        [Test]
        public void BufferTestWriteSmall()
        {
            // Should be in head only
            MessageStream buf = new MessageStream(16);
            BufferTestWriteInternal(ref buf, "abcd");
        }

        [Test]
        public void BufferTestWriteExact()
        {
            // Should be in head only
            MessageStream buf = new MessageStream(16);
            BufferTestWriteInternal(ref buf, "abcdefgh");
        }

        [Test]
        public void BufferTestWriteBig()
        {
            // Should be in new node only, skipping head
            MessageStream buf = new MessageStream(16);
            BufferTestWriteInternal(ref buf, "abcdefgh ijk");
        }

        [Test]
        public void BufferTestWriteSmallSmall()
        {
            // Should be in head only
            MessageStream buf = new MessageStream(16);
            BufferTestWriteInternal(ref buf, "ab");
            BufferTestWriteInternal(ref buf, "cd");
        }

        [Test]
        public void BufferTestWriteSmallBig()
        {
            // Should be in 2 nodes, including head
            MessageStream buf = new MessageStream(16);
            BufferTestWriteInternal(ref buf, "ab");
            BufferTestWriteInternal(ref buf, "cdefgh ijklmnopqrs");
        }

        [Test]
        public void BufferTestWriteBigSmall()
        {
            // Should be in 2 nodes, skipping head
            MessageStream buf = new MessageStream(16);
            BufferTestWriteInternal(ref buf, "abcdefgh ijklmnopq");
            BufferTestWriteInternal(ref buf, "rs");
        }

        [Test]
        public void BufferTestWriteBigBig()
        {
            // Should be in 2 nodes, skipping head
            MessageStream buf = new MessageStream(16);
            BufferTestWriteInternal(ref buf, "abcdefghzyx ");
            BufferTestWriteInternal(ref buf, "ijklmnopqrs ");
        }

        [Test]
        public void BufferTestWriteBigSmallBig()
        {
            // Should be in 3 nodes, skipping head
            MessageStream buf = new MessageStream(16);
            BufferTestWriteInternal(ref buf, "abcdefghijk");
            BufferTestWriteInternal(ref buf, "lm");
            BufferTestWriteInternal(ref buf, "nopqrstuvwx");
        }

        [Test]
        public void BufferTestWriteSmallSmallSmallExpand()
        {
            // Should be in 2 nodes, including head
            MessageStream buf = new MessageStream(16);

            // head
            BufferTestWriteInternal(ref buf, "abc");
            // head
            BufferTestWriteInternal(ref buf, "def");
            // new node
            BufferTestWriteInternal(ref buf, "ghi");
        }
    }


    //---------------------------------------------------------------------------------------------------
    // BUFFER READ TESTS
    //---------------------------------------------------------------------------------------------------
    public class BufferReadTests : PlayerConnectionBufferTestFixture
    {
        [Test]
        public void BufferTestReadSmall()
        {
            MessageStream buf = new MessageStream(16);
            BufferTestReadInternal(ref buf, "abcd", 0, 8);
        }

        [Test]
        public void BufferTestReadExact()
        {
            MessageStream buf = new MessageStream(16);
            BufferTestReadInternal(ref buf, "abcdefgh", 0, 16);
        }

        [Test]
        public void BufferTestReadBig()
        {
            MessageStream buf = new MessageStream(16);
            BufferTestReadInternal(ref buf, "abcdefgh ijk", 0, 24);
        }

        [Test]
        public void BufferTestReadSmallOffset()
        {
            MessageStream buf = new MessageStream(16);
            BufferTestReadInternal(ref buf, "abcd", 2, 6);
        }

        [Test]
        public void BufferTestReadBigOffset()
        {
            MessageStream buf = new MessageStream(16);
            BufferTestReadInternal(ref buf, "abcdefgh ijk", 2, 22);
        }

        [Test]
        public void BufferTestReadSmallBigOffset()
        {
            // Should be in 2 nodes, including head
            MessageStream buf = new MessageStream(16);
            BufferTestWriteInternal(ref buf, "abcd");
            // Include head and tail
            BufferTestReadInternal(ref buf, "efgh ijklmnop", 2, 22, "abcdefgh ijklmnop");
            // Skip head, need tail
            BufferTestReadInternal(ref buf, "", 10, 22, "abcdefgh ijklmnop");
        }

        [Test]
        public void BufferTestReadBigBigOffset()
        {
            // Should be in 2 nodes, skipping head
            MessageStream buf = new MessageStream(16);
            BufferTestWriteInternal(ref buf, "abcdefgh ijk");
            // Skip head, need both proceeding nodes
            BufferTestReadInternal(ref buf, "lmnopqrstuvwx", 4, 34, "abcdefgh ijklmnopqrstuvwx");
            // Skip head and next node, need tail
            BufferTestReadInternal(ref buf, "", 28, 42, "abcdefgh ijklmnopqrstuvwx");
        }

        [Test]
        public void BufferTestReadTooBigThrows()
        {
            MessageStream buf = new MessageStream(16);
            Assert.Throws<ArgumentOutOfRangeException>(() => BufferTestReadInternal(ref buf, "abcd", 0, 10));
        }

        [Test]
        public void BufferTestReadTooNegativeThrows()
        {
            MessageStream buf = new MessageStream(16);
            Assert.Throws<ArgumentOutOfRangeException>(() => BufferTestReadInternal(ref buf, "abcd", 4, 2));
        }

        [Test]
        public void BufferTestRead0()
        {
            MessageStream buf = new MessageStream(16);
            BufferTestReadInternal(ref buf, "abcd", 2, 2, " ");
        }

        [Test]
        public void BufferTestReadEmpty0()
        {
            MessageStream buf = new MessageStream(16);
            BufferTestReadInternal(ref buf, "", 0, 0, "");
        }
    }

    //---------------------------------------------------------------------------------------------------
    // BUFFER RECYCLE AND FREE TESTS
    // Used when receiving data - we wait until the full data stream has been received
    // and then process immediately so we don't need to keep any of it here afterward
    //---------------------------------------------------------------------------------------------------
    public class BufferRecycleAndFreeTests : PlayerConnectionBufferTestFixture
    {
        [Test]
        public void BufferTestRecycleAllSmall()
        {
            // Should be in 2 nodes, skipping head
            unsafe
            {
                MessageStream buf = new MessageStream(16);
                BufferTestWriteInternal(ref buf, "abcdef");
                buf.RecycleStreamAndFreeExtra();
                Assert.AreEqual(0, buf.BufferRead->Size);
                Assert.AreEqual(0, buf.BufferWrite->Size);
                Assert.AreEqual((IntPtr)buf.BufferWrite, (IntPtr)buf.BufferRead);
                Assert.AreEqual(0, buf.TotalBytes);
                Assert.AreEqual(IntPtr.Zero, (IntPtr)buf.BufferRead->Next);
            }
        }

        [Test]
        public void BufferTestRecycleAllExact()
        {
            // Should be in 2 nodes, skipping head
            unsafe
            {
                MessageStream buf = new MessageStream(16);
                BufferTestWriteInternal(ref buf, "abcdefgh");
                buf.RecycleStreamAndFreeExtra();
                Assert.AreEqual(0, buf.BufferRead->Size);
                Assert.AreEqual(0, buf.BufferWrite->Size);
                Assert.AreEqual((IntPtr)buf.BufferWrite, (IntPtr)buf.BufferRead);
                Assert.AreEqual(0, buf.TotalBytes);
                Assert.AreEqual(IntPtr.Zero, (IntPtr)buf.BufferRead->Next);
            }
        }

        [Test]
        public void BufferTestRecycleAllBig()
        {
            // Should be in new node only, skipping head
            unsafe
            {
                MessageStream buf = new MessageStream(16);
                BufferTestWriteInternal(ref buf, "abcdefgh ijk");
                buf.RecycleStreamAndFreeExtra();
                Assert.AreEqual(0, buf.BufferRead->Size);
                Assert.AreEqual(0, buf.BufferWrite->Size);
                Assert.AreEqual((IntPtr)buf.BufferWrite, (IntPtr)buf.BufferRead);
                Assert.AreEqual(0, buf.TotalBytes);
                Assert.AreEqual(IntPtr.Zero, (IntPtr)buf.BufferRead->Next);
            }
        }

        [Test]
        public void BufferTestRecycleAllSmallBig()
        {
            // Should be in 2 nodes, skipping head
            unsafe
            {
                MessageStream buf = new MessageStream(16);
                BufferTestWriteInternal(ref buf, "abcdef");
                BufferTestWriteInternal(ref buf, "abcdefgh ijk");
                buf.RecycleStreamAndFreeExtra();
                Assert.AreEqual(0, buf.BufferRead->Size);
                Assert.AreEqual(0, buf.BufferWrite->Size);
                Assert.AreEqual((IntPtr)buf.BufferWrite, (IntPtr)buf.BufferRead);
                Assert.AreEqual(0, buf.TotalBytes);
                Assert.AreEqual(IntPtr.Zero, (IntPtr)buf.BufferRead->Next);
            }
        }

        [Test]
        public void BufferTestRecycleAllBigBig()
        {
            // Should be in new node only, skipping head
            unsafe
            {
                MessageStream buf = new MessageStream(16);
                BufferTestWriteInternal(ref buf, "abcdefgh ijk");
                BufferTestWriteInternal(ref buf, "lmnopqrstuvw");
                buf.RecycleStreamAndFreeExtra();
                Assert.AreEqual(0, buf.BufferRead->Size);
                Assert.AreEqual(0, buf.BufferWrite->Size);
                Assert.AreEqual((IntPtr)buf.BufferWrite, (IntPtr)buf.BufferRead);
                Assert.AreEqual(0, buf.TotalBytes);
                Assert.AreEqual(IntPtr.Zero, (IntPtr)buf.BufferRead->Next);
            }
        }

        [Test]
        public void BufferTestRecycleAllEmpty()
        {
            // Should be in new node only, skipping head
            unsafe
            {
                MessageStream buf = new MessageStream(16);
                buf.RecycleStreamAndFreeExtra();
                Assert.AreEqual(0, buf.BufferRead->Size);
                Assert.AreEqual(0, buf.BufferWrite->Size);
                Assert.AreEqual((IntPtr)buf.BufferWrite, (IntPtr)buf.BufferRead);
                Assert.AreEqual(0, buf.TotalBytes);
                Assert.AreEqual(IntPtr.Zero, (IntPtr)buf.BufferRead->Next);
            }
        }
    }

    //---------------------------------------------------------------------------------------------------
    // BUFFER RECYCLE PARTIAL TESTS
    // Used when sending data and part of it was blocked until a future frame
    //---------------------------------------------------------------------------------------------------
    public class BufferRecyclePartialTests : PlayerConnectionBufferTestFixture
    {
        [Test]
        public void BufferTestRecycleSmallPart()
        {
            BufferTestRecycleInternal(new string[] { "abcdef" }, 4);
        }

        [Test]
        public void BufferTestRecycleSmallBigPartSmall()
        {
            BufferTestRecycleInternal(new string[] { "abcdef", "abcdefgh - blablabla" }, 4);
        }

        [Test]
        public void BufferTestRecycleSmallBigPartBig()
        {
            BufferTestRecycleInternal(new string[] { "abcdef", "abcdefgh - blablabla" }, 20);
        }

        [Test]
        public void BufferTestRecycleBigPart()
        {
            BufferTestRecycleInternal(new string[] { "abcdefgh nowthisismorethan 16 bytes" }, 6);
        }

        [Test]
        public void BufferTestRecycleBigBigPartBig()
        {
            BufferTestRecycleInternal(new string[] { "abcdefgh nowthisismorethan 16 bytes", "zxckjdsfaklfhds klsadfvndflkjvn" }, 6);
        }

        [Test]
        public void BufferTestRecycleBigBigPartBigBig()
        {
            string firstText = "abcdefgh nowthisismorethan 16 bytes";
            BufferTestRecycleInternal(new string[] { firstText, "zxckjdsfaklfhds klsadfvndflkjvn" }, firstText.Length * 2 + 8);
        }

        [Test]
        public void BufferTestRecycleSmallBigBigPartSmall()
        {
            BufferTestRecycleInternal(new string[] { "mnopqr", "abcdefgh nowthisismorethan 16 bytes", "zxckjdsfaklfhds klsadfvndflkjvn" }, 4);
        }

        [Test]
        public void BufferTestRecycleBigBigBigPartBig()
        {
            BufferTestRecycleInternal(new string[] { "mnopqrkljasdhfviuloevanljmkl  lksdfva.32565", "abcdefgh nowthisismorethan 16 bytes", "zxckjdsfaklfhds klsadfvndflkjvn" }, 4);
        }

        [Test]
        public void BufferTestRecycleBigBigBigPartBigBig()
        {
            string firstText = "mnopqrkljasdhfviuloevanljmkl  lksdfva.32565";
            BufferTestRecycleInternal(new string[] { firstText, "abcdefgh nowthisismorethan 16 bytes", "zxckjdsfaklfhds klsadfvndflkjvn" }, firstText.Length * 2 + 8);
        }

        [Test]
        public void BufferTestRecycleNullOffsetThrows()
        {
            MessageStream buf = new MessageStream(16);
            BufferTestWriteInternal(ref buf, "abcdef");
            unsafe
            {
                Assert.Throws<ArgumentException>(() => buf.RecyclePartialStream(null, 14));
            }
        }

        [Test]
        public void BufferTestRecycleNull()
        {
            MessageStream buf = new MessageStream(16);
            BufferTestWriteInternal(ref buf, "abcdef");
            unsafe
            {
                buf.RecyclePartialStream(null, 0);
                Assert.AreEqual(0, buf.BufferRead->Size);
                Assert.AreEqual(0, buf.BufferWrite->Size);
                Assert.AreEqual((IntPtr)buf.BufferWrite, (IntPtr)buf.BufferRead);
                Assert.AreEqual(0, buf.TotalBytes);
            }
        }

        [Test]
        public void BufferTestRecycleOffsetTooBigThrows()
        {
            MessageStream buf = new MessageStream(16);
            BufferTestWriteInternal(ref buf, "abcdef");
            unsafe
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => buf.RecyclePartialStream(buf.BufferRead, 14));
            }
        }

        [Test]
        public void BufferTestRecycle0()
        {
            BufferTestRecycleInternal(new string[] { "abcdef" }, 0);
        }

        [Test]
        public void BufferTestRecycleEmptyThrows()
        {
            MessageStream buf = new MessageStream(16);
            unsafe
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => buf.RecyclePartialStream(buf.BufferRead, 4));
            }
        }

        [Test]
        public void BufferTestRecycleEmpty0DoesntThrow()
        {
            MessageStream buf = new MessageStream(16);
            unsafe
            {
                Assert.DoesNotThrow(() => buf.RecyclePartialStream(buf.BufferRead, 0));
            }
        }
    }

    //---------------------------------------------------------------------------------------------------
    // BUFFER RECYCLE PARTIAL TESTS
    // Used when sending data and part of it was blocked until a future frame
    //---------------------------------------------------------------------------------------------------
    public class MessageStreamManagerTests : PlayerConnectionBufferTestFixture
    {
        [Test]
        public unsafe void CheckShutdownFrees()
        {
            // (because of PlayerConnectionTestFixture.PerTestSetUp)
            MessageStreamManager.DestroyStreamBuilder(messageBuilder);

            // Kill any buffers already registered from the profiler system
            MessageStreamManager.Shutdown();

            long sizePreInit = UnsafeUtility.GetHeapSize(Collections.Allocator.Persistent);

            // Start fresh
            MessageStreamManager.Initialize();

            // The pointers returned by these 2 methods will get lost surely...
            MessageStreamManager.CreateStreamBuilder();
            MessageStreamManager.CreateStreamBuilder();
            long sizeCreate = UnsafeUtility.GetHeapSize(Collections.Allocator.Persistent);
            MessageStreamManager.Shutdown();

            long sizeShutdown = UnsafeUtility.GetHeapSize(Collections.Allocator.Persistent);

            // (because of PlayerConnectionTestFixture.PerTestTearDown)
            // (also do this prior to asserts in case of test failure so tear down doesn't fail and we miss the real issue)
            MessageStreamManager.Initialize();
            messageBuilder = MessageStreamManager.CreateStreamBuilder();

            Assert.IsTrue(sizePreInit < sizeCreate);
            Assert.IsTrue(sizePreInit == sizeShutdown);
        }
    }
}
