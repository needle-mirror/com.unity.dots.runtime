#if ENABLE_PROFILER
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Development.Profiling;
using Unity.Development.PlayerConnection;
using Unity.Profiling;
using UnityEngine;
using System.Threading;
using Unity.Burst;
using System;
using UnityEngine.Networking.PlayerConnection;
using static Unity.Baselib.LowLevel.Binding;
using Unity.Profiling.LowLevel;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Core;

namespace Unity.ZeroJobs.Tests
{
    //---------------------------------------------------------------------------------------------------
    // PROFILER STATS TESTS
    //---------------------------------------------------------------------------------------------------
    public class ProfilerTestFixture
    {
        [SetUp]
        public void PerTestSetUp()
        {
            TempMemoryScope.EnterScope();
            Unity.Development.Profiling.Profiler.Initialize();
            PlayerConnectionProfiler.mode.Data = ProfilerModes.ProfileCPU;
        }

        [TearDown]
        public void PerTestTearDown()
        {
            PlayerConnectionProfiler.mode.Data = ProfilerModes.ProfileDisabled;
            Unity.Development.Profiling.Profiler.Shutdown();
            TempMemoryScope.ExitScope();
        }

        internal static unsafe void ReceiveConnectMessage()
        {
            MessageStreamBuilder* messageStream = MessageStreamManager.CreateStreamBuilder();
            messageStream->WriteData(ProfilerModes.ProfileCPU);

            MessageEventArgs message = new MessageEventArgs();
            message.data = messageStream->m_BufferList->ToByteArray(0, messageStream->m_BufferList->TotalBytes);

            var callback = Connection.GetMessageEvent(EditorMessageIds.kProfilerSetMode);
            callback.Invoke(message);

            MessageStreamManager.DestroyStreamBuilder(messageStream);
        }
    }

    public class ProfilerStatsTests : ProfilerTestFixture
    {
        private const double kThreadLoopTime = 3;

        private static void AssertHeapKbRange(int stat, int expected)
        {
            // The value should be within a 1 kb range due to a small bit of padding which could accumulate past the next kb
            // if it was already very close
            Assert.IsTrue(stat == expected || stat == expected + 1);
        }

        public static unsafe void CheckNativeHeapStatsInternal()
        {
            ProfilerStats.CalculateStatsSnapshot();
            int oldKb = ProfilerStats.Stats.memoryStats.kbReservedTotal;
            UnsafeUtility.EnterTempScope();

            // Taking a stats snap shot should
            // a) Fill memory stats automatically since they are part of DOTS Runtime core
            // b) Convert all AccumStats (many of which are represented as longs) into Stats (required by current profiler stats protocol)

            void* mem = UnsafeUtility.Malloc(2048, 16, Collections.Allocator.Persistent);
            void* mem2 = UnsafeUtility.Malloc(1024, 16, Collections.Allocator.Temp);
            ProfilerStats.CalculateStatsSnapshot();
            Assert.IsTrue((ProfilerStats.GatheredStats & ProfilerModes.ProfileMemory) != 0);
            // debug build padding could make it 3 or 4 kb
            AssertHeapKbRange(ProfilerStats.Stats.memoryStats.kbReservedTotal, 3 + oldKb);

            UnsafeUtility.Free(mem, Collections.Allocator.Persistent);
            ProfilerStats.CalculateStatsSnapshot();
            // debug build padding could make it 1 or 2 kb
            AssertHeapKbRange(ProfilerStats.Stats.memoryStats.kbReservedTotal, 1 + oldKb);

            // Freeing a temp allocation should not adjust stats (you don't technically need to, and it
            // can cause fake leaks)
            UnsafeUtility.Free(mem2, Collections.Allocator.Temp);
            ProfilerStats.CalculateStatsSnapshot();
            AssertHeapKbRange(ProfilerStats.Stats.memoryStats.kbReservedTotal, 1 + oldKb);

            // This should adjust stats - it's meant to be fast and release all temp allocations in scope
            UnsafeUtility.ExitTempScope();
            ProfilerStats.CalculateStatsSnapshot();
            Assert.IsTrue(ProfilerStats.Stats.memoryStats.kbReservedTotal == oldKb);
        }

        [Test]
        public unsafe void CheckNativeHeapStats()
        {
            CheckNativeHeapStatsInternal();
        }

        [Test]
        public unsafe void CheckAccumulationError()
        {
            ProfilerStats.CalculateStatsSnapshot();
            int oldKb = ProfilerStats.Stats.memoryStats.kbReservedTotal;

            void* mem = UnsafeUtility.Malloc(1024 + 256, 16, Collections.Allocator.Persistent);
            void* mem2 = UnsafeUtility.Malloc(1024 + 256, 16, Collections.Allocator.Persistent);
            void* mem3 = UnsafeUtility.Malloc(1024 + 256, 16, Collections.Allocator.Persistent);
            void* mem4 = UnsafeUtility.Malloc(1024 + 256, 16, Collections.Allocator.Persistent);
            ProfilerStats.CalculateStatsSnapshot();

            // Ensure 5, not 4
            AssertHeapKbRange(ProfilerStats.Stats.memoryStats.kbReservedTotal, 5 + oldKb);

            UnsafeUtility.Free(mem, Collections.Allocator.Persistent);
            UnsafeUtility.Free(mem2, Collections.Allocator.Persistent);
            UnsafeUtility.Free(mem3, Collections.Allocator.Persistent);
            UnsafeUtility.Free(mem4, Collections.Allocator.Persistent);
            ProfilerStats.CalculateStatsSnapshot();

            Assert.IsTrue(ProfilerStats.Stats.memoryStats.kbReservedTotal == oldKb);
        }

        [Test]
        public unsafe void CheckHeapWithExternalAlloc()
        {
            ProfilerStats.CalculateStatsSnapshot();
            int oldReservedKb = ProfilerStats.Stats.memoryStats.kbReservedTotal;
            int oldUsedKb = ProfilerStats.Stats.memoryStats.kbUsedTotal;

            void* mem = UnsafeUtility.Malloc(16384, 16, Collections.Allocator.Persistent);
            // Fake an external allocation
            ProfilerStats.AccumStats.memReservedExternal.Accumulate(4096);
            ProfilerStats.AccumStats.memUsedExternal.Accumulate(3072);
            ProfilerStats.CalculateStatsSnapshot();

            AssertHeapKbRange(ProfilerStats.Stats.memoryStats.kbReservedTotal, 16 + 4 + oldReservedKb);
            AssertHeapKbRange(ProfilerStats.Stats.memoryStats.kbUsedTotal, 16 + 3 + oldUsedKb);

            UnsafeUtility.Free(mem, Collections.Allocator.Persistent);
            ProfilerStats.CalculateStatsSnapshot();

            AssertHeapKbRange(ProfilerStats.Stats.memoryStats.kbReservedTotal, 4 + oldReservedKb);
            AssertHeapKbRange(ProfilerStats.Stats.memoryStats.kbUsedTotal, 3 + oldUsedKb);

            ProfilerStats.AccumStats.memReservedExternal.value = 0;
            ProfilerStats.AccumStats.memUsedExternal.value = 0;
        }

#if !NET_DOTS
        [Test]
        public unsafe void CheckAccumThreadSafety()
        {
            ProfilerStats.AccumStats.audioCodecMemory.value = 0;
            ProfilerStats.AccumStats.audioDspCPUx10.value = 0;

            Thread[] threads = new Thread[16];
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(() =>
                {
                    var start = Time.timeAsDouble;
                    while (Time.timeAsDouble - start < kThreadLoopTime)
                    {
                        ProfilerStats.AccumStats.audioCodecMemory.Accumulate(3);
                        ProfilerStats.AccumStats.audioDspCPUx10.Accumulate(1);
                    }
                });
                threads[i].Start();
            }

            for (int i = 0; i < threads.Length; i++)
            {
                threads[i].Join();
            }

            Assert.AreEqual(ProfilerStats.AccumStats.audioCodecMemory.value, ProfilerStats.AccumStats.audioDspCPUx10.value * 3);
        }
#endif
    }

    public class ProfilerLegacyTests : ProfilerTestFixture
    {
        [Test]
        public unsafe void TestUnityEngineProfiler()
        {
            ProfilerProtocolThread.Stream.buffer->m_BufferList->RecycleStreamAndFreeExtra();

            // for compatibility
            UnityEngine.Profiling.Profiler.BeginSample("SomeMethod.Test");
            UnityEngine.Profiling.Profiler.EndSample();
            var profileMemA = ProfilerProtocolThread.Stream.buffer->m_BufferList->ToByteArray(0, ProfilerProtocolThread.Stream.buffer->m_BufferList->TotalBytes);
            ProfilerProtocolThread.Stream.buffer->m_BufferList->RecycleStreamAndFreeExtra();

            ProfilerMarker marker = new ProfilerMarker("SomeMethod.Test");
            marker.Begin();
            marker.End();
            var profileMemB = ProfilerProtocolThread.Stream.buffer->m_BufferList->ToByteArray(0, ProfilerProtocolThread.Stream.buffer->m_BufferList->TotalBytes);
            ProfilerProtocolThread.Stream.buffer->m_BufferList->RecycleStreamAndFreeExtra();

            const int k_BlockIndexCurr_1 = 28;
            const int k_Time_1 = 56;
            const int k_BlockIndexNext_1 = 64;
            const int k_BlockIndexCurr_2 = 100;
            const int k_Time_2 = 128;
            const int k_BlockIndexNext_2 = 136;

            Assert.NotZero(profileMemA.Length);
            Assert.NotZero(profileMemB.Length);
            Assert.AreEqual(profileMemA.Length, profileMemB.Length);
            for (int i = 0; i < profileMemA.Length; i++)
            {
                if (i != k_BlockIndexCurr_1 && i != k_BlockIndexNext_1 && i != k_BlockIndexCurr_2 && i != k_BlockIndexNext_2 &&
                    !(i >= k_Time_1 && i < k_Time_1 + 8) && !(i >= k_Time_2 && i < k_Time_2 + 8))
                {
                    Assert.AreEqual(profileMemA[i], profileMemB[i]);
                }
            }
            Assert.True(profileMemA[k_BlockIndexNext_1] == profileMemA[k_BlockIndexCurr_1] + 1);
            Assert.True(profileMemA[k_BlockIndexCurr_2] == profileMemA[k_BlockIndexNext_1]);
            Assert.True(profileMemA[k_BlockIndexNext_2] == profileMemA[k_BlockIndexCurr_2] + 1);
            Assert.True(profileMemB[k_BlockIndexCurr_1] == profileMemA[k_BlockIndexCurr_1] + 2);
            Assert.True(profileMemB[k_BlockIndexNext_1] == profileMemA[k_BlockIndexNext_1] + 2);
            Assert.True(profileMemB[k_BlockIndexCurr_2] == profileMemA[k_BlockIndexCurr_2] + 2);
            Assert.True(profileMemB[k_BlockIndexNext_2] == profileMemA[k_BlockIndexNext_2] + 2);
        }
    }

    public class PlayerConnectionProfilerTests : ProfilerTestFixture
    {
        [Test]
        public unsafe void TestEnableFromPlayerConnection()
        {
            PlayerConnectionProfiler.Shutdown();

            PlayerConnectionProfiler.Initialize();
            Assert.IsFalse(PlayerConnectionProfiler.Enabled);
            ReceiveConnectMessage();

            Assert.IsTrue(PlayerConnectionProfiler.Enabled);

            PlayerConnectionProfiler.Shutdown();
            Assert.IsFalse(PlayerConnectionProfiler.Enabled);
        }

        [Test]
        public unsafe void TestEnableFromPlayerConnectionResponds()
        {
            ProfilerProtocolSession.streamSession.buffer->m_BufferList->RecycleStreamAndFreeExtra();

            PlayerConnectionProfiler.Shutdown();
            PlayerConnectionProfiler.Initialize();

            Assert.Zero(ProfilerProtocolSession.streamSession.buffer->m_BufferList->TotalBytes);

            ReceiveConnectMessage();

            // When we receive a connection, we should react by sending out some information
            Assert.NotZero(ProfilerProtocolSession.streamSession.buffer->m_BufferList->TotalBytes);

            PlayerConnectionProfiler.Shutdown();
        }

        [Test]
        public unsafe void TestDisableFromPlayerConnectionDoesntSend()
        {
            ProfilerProtocolThread.Stream.buffer->m_BufferList->RecycleStreamAndFreeExtra();

            PlayerConnectionProfiler.Shutdown();

            ProfilerMarker marker = new ProfilerMarker("Test marker");
            marker.Begin();
            marker.End();

            Assert.Zero(ProfilerProtocolThread.Stream.buffer->m_BufferList->TotalBytes);
        }
    }

    public class ProfilerStreamTests : ProfilerTestFixture
    {
        [Test]
        public unsafe void TestProfilerStreamThrowsIfBadSize()
        {
            var stream = new ProfilerProtocolStream(123);
            stream.EmitBlockBegin(ProfilerMessageType.BeginSample, 4);
            stream.buffer->WriteData<uint>(10);
            stream.buffer->WriteData<uint>(11);
            Assert.Throws<InvalidOperationException>(() => stream.EmitBlockEnd());
            stream.buffer->m_BufferList->Free();
        }

        [Test]
        public unsafe void TestProfilerStreamDoesNotThrowIfGoodSize()
        {
            var stream = new ProfilerProtocolStream(123);
            stream.EmitBlockBegin(ProfilerMessageType.BeginSample, 4);
            stream.buffer->WriteData<uint>(10);
            Assert.DoesNotThrow(() => stream.EmitBlockEnd());
            stream.buffer->m_BufferList->Free();
        }

        [Test]
        public unsafe void TestProfilerStreamBlockSizeIsAutoAligned()
        {
            var stream = new ProfilerProtocolStream(123);
            stream.EmitBlockBegin(ProfilerMessageType.BeginSample, 3);
            stream.buffer->WriteData<byte>(10);
            stream.buffer->WriteData<byte>(11);
            stream.buffer->WriteData<byte>(12);
            Assert.DoesNotThrow(() => stream.EmitBlockEnd());

            Assert.Zero(stream.buffer->DataToSendBytes & 3);

            stream.buffer->m_BufferList->Free();
        }
    }

#if !NET_DOTS
    public class ProfilerThreadTests : ProfilerTestFixture
    {
        [Test]
        public unsafe void TestProfilerThreadsCantInitializeTwice()
        {
            ProfilerProtocolThread.Shutdown();
            Thread thread = new Thread(() => { ProfilerProtocolThread.Initialize(); });
            thread.Start();
            thread.Join();
            Assert.Throws<InvalidOperationException>(() => ProfilerProtocolThread.Initialize());
            ProfilerProtocolThread.Shutdown();
        }

        [Test]
        public unsafe void TestProfilerThreadsHaveUniqueStreams()
        {
            ProfilerProtocolThread.Shutdown();
            ProfilerProtocolThread.Initialize();

            Thread[] threads = new Thread[16];
            IntPtr[] streams = new IntPtr[16];
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread((object p) =>
                {
                    int pi = (int)p;
                    Assert.Zero(ProfilerProtocolThread.Stream.buffer->m_BufferList->TotalBytes);
                    fixed (ProfilerProtocolStream* s = &ProfilerProtocolThread.Stream)
                        streams[pi] = (IntPtr)s;
                    ProfilerProtocolThread.SendNewFrame();
                });
                threads[i].Start(i);
            }

            for (int i = 0; i < threads.Length; i++)
                threads[i].Join();

            for (int i = 0; i < threads.Length; i++)
            {
                ProfilerProtocolStream* stream = (ProfilerProtocolStream*)streams[i];
                Assert.NotZero(stream->buffer->m_BufferList->TotalBytes);
                for (int j = 0; j < threads.Length; j++)
                    Assert.IsTrue(streams[i] != streams[j] || i == j);
            }

            ProfilerProtocolThread.Shutdown();
        }
    }
#endif

    public class ProfilerSessionTests : PlayerConnectionTestFixture
    {
        // Most other test types cover all the areas in the profiler session, so we'll only cover the remaining
        // specific bits that aren't

#if !NET_DOTS
        private unsafe void SendAndValidate(HashSet<uint> markerDefined)
#else
        private unsafe void SendAndValidate(Dictionary<uint, bool> markerDefined)
#endif
        {
            Connection.TransmitAndReceive();
            Assert.IsFalse(Connection.HasSendDataQueued, "Profiler data not sent!");

            Baselib_ErrorState errState = new Baselib_ErrorState();

            byte[] buffer = new byte[65536];
            uint received = 0;
            fixed (byte* bufferPtr = buffer)
            {
                received = Baselib_Socket_TCP_Recv(hSpoofSocketConnected, (IntPtr)bufferPtr, (uint)buffer.Length, (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));
                byte* dataPtr = bufferPtr;

                while (dataPtr < bufferPtr + received)
                {
                    Connection.MessageHeader* headerPtr = (Connection.MessageHeader*)dataPtr;

                    // Parse message header
                    Assert.IsTrue(headerPtr->magicId == EditorMessageIds.kMagicNumber);
                    if (headerPtr->messageId == EditorMessageIds.kProfilerPlayerInfoMessage)
                    {
                        dataPtr += sizeof(Connection.MessageHeader) + headerPtr->bytes;
                        continue;
                    }

                    Assert.IsTrue(headerPtr->messageId == EditorMessageIds.kProfilerDataMessage);

                    // Parse message
                    byte* messagePtr = dataPtr + sizeof(Connection.MessageHeader);
                    while (messagePtr < dataPtr + headerPtr->bytes)
                    {
                        // Possibly a profiler global header first
                        bool isGlobalHeader = *(uint*)(messagePtr + 0) == ProfilerProtocolSession.kSessionGlobalHeader;
                        if (isGlobalHeader)
                            messagePtr += 36;

                        // Profiler block header
                        Assert.IsTrue(*(uint*)(messagePtr + 0) == ProfilerProtocolStream.kPacketBlockHeader);
                        int blockIndex = *(int*)(messagePtr + 4);
                        int byteSize = *(int*)(messagePtr + 16);

                        // Profiler block body
                        switch (*(ProfilerMessageType*)(messagePtr + 20))
                        {
                            case ProfilerMessageType.ProfilerState:
                                break;
                            case ProfilerMessageType.ThreadInfo:
                                break;
#if !NET_DOTS
                            case ProfilerMessageType.MarkerInfo:
                                Assert.IsTrue(markerDefined.Add(*(uint*)(messagePtr + 24)));
                                break;
                            case ProfilerMessageType.BeginSample:
                                Assert.IsTrue(markerDefined.Contains(*(uint*)(messagePtr + 28)));
                                break;
                            case ProfilerMessageType.EndSample:
                                Assert.IsTrue(markerDefined.Contains(*(uint*)(messagePtr + 28)));
                                break;
#else
                            case ProfilerMessageType.MarkerInfo:
                                markerDefined.Add(*(uint*)(messagePtr + 24), true);
                                break;
                            case ProfilerMessageType.BeginSample:
                                Assert.IsTrue(markerDefined.ContainsKey(*(uint*)(messagePtr + 28)));
                                break;
                            case ProfilerMessageType.EndSample:
                                Assert.IsTrue(markerDefined.ContainsKey(*(uint*)(messagePtr + 28)));
                                break;
#endif
                        }

                        // Profiler block footer
                        Assert.IsTrue(*(int*)(messagePtr + 20 + byteSize + 0) == blockIndex + 1);
                        Assert.IsTrue(*(uint*)(messagePtr + 20 + byteSize + 4) == ProfilerProtocolStream.kPacketBlockFooter);

                        messagePtr += 20 + byteSize + 8;
                    }

                    dataPtr = messagePtr;
                }
            }
        }

        [Test]
        public unsafe void TestNewFrameInThreadStreamNotSessionStream()
        {
            Assert.Zero(ProfilerProtocolSession.streamSession.buffer->m_BufferList->TotalBytes);

            ProfilerTestFixture.ReceiveConnectMessage();

            int sessionBytes = ProfilerProtocolSession.streamSession.buffer->m_BufferList->TotalBytes;
            Assert.NotZero(sessionBytes);
            Assert.Zero(ProfilerProtocolThread.Stream.buffer->m_BufferList->TotalBytes);

            ProfilerProtocolSession.SendNewFrame();

            Assert.NotZero(ProfilerProtocolThread.Stream.buffer->m_BufferList->TotalBytes);
            Assert.AreEqual(ProfilerProtocolSession.streamSession.buffer->m_BufferList->TotalBytes, sessionBytes);
        }

        [Test]
        public unsafe void TestProfilerSessionEmitsMarkerInfoBeforeMarker()
        {
            SpoofInitListenInternal();
            Connection.ConnectDirect(spoofIp, spoofPort);
            Baselib_Timer_WaitForAtLeast(200);
            SpoofAcceptInternal();

            ProfilerTestFixture.ReceiveConnectMessage();

#if !NET_DOTS
            HashSet<uint> markerDefined = new HashSet<uint>();
#else
            Dictionary<uint, bool> markerDefined = new Dictionary<uint, bool>();
#endif

            for (int i = 0; i < 8; i++)
            {
                for (ulong j = 0; j < 8; j++)
                {
                    ProfilerMarker marker = new ProfilerMarker($"{Baselib_Timer_GetHighPrecisionTimerTicks() + j} test");
                    ProfilerMarker marker2 = new ProfilerMarker($"{Baselib_Timer_GetHighPrecisionTimerTicks() + j + 1} test2");

                    marker.Begin();
                    marker2.Begin();
                    marker2.End();
                    marker.End();
                }

                ProfilerProtocolSession.SendNewMarkersAndThreads();
                SendAndValidate(markerDefined);
            }

            SpoofExitInternal();
        }

#if false //https://unity3d.atlassian.net/browse/DOTSR-2368
#if !NET_DOTS
        [Test]
        public unsafe void TestProfilerSessionEmitsMarkerInfoBeforeMarkerThreaded()
        {
            SpoofInitListenInternal();
            Connection.ConnectDirect(spoofIp, spoofPort);
            Baselib_Timer_WaitForAtLeast(200);
            SpoofAcceptInternal();

            ProfilerTestFixture.ReceiveConnectMessage();

            ManualResetEvent mre = new ManualResetEvent(false);
            HashSet<uint> markerDefined = new HashSet<uint>();

            Thread[] threads = new Thread[16];
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread(() =>
                {
                    for (ulong g = 0; g < 8; g++)
                    {
                        mre.WaitOne();
                        ProfilerMarker marker = new ProfilerMarker($"{Baselib_Timer_GetHighPrecisionTimerTicks() + g * 2} test");
                        ProfilerMarker marker2 = new ProfilerMarker($"{Baselib_Timer_GetHighPrecisionTimerTicks() + g * 2 + 1} test2");

                        marker.Begin();
                        marker2.Begin();
                        marker2.End();
                        marker.End();

                        Thread.Sleep(100);
                    }
                });
                threads[i].Start();
            }

            // This test is set up to try to make new markers get created in the midst of buffers being sent over
            // the player connection. If timed just right without proper locks, we can get into a situation where
            // marker begin sample and marker end sample messages are sent without the marker info having been sent first.
            // (It ends up in the next frame's player connection datastream)
            for (int g = 0; g < 16; g++)
            {
                if ((g & 1) == 0)
                    mre.Set();
                ProfilerProtocolSession.SendNewMarkersAndThreads();
                SendAndValidate(markerDefined);
                if ((g & 1) == 1)
                    mre.Reset();
                Thread.Sleep(50);
            }

            for (int i = 0; i < threads.Length; i++)
                threads[i].Join();

            ProfilerProtocolSession.SendNewMarkersAndThreads();
            SendAndValidate(markerDefined);

            SpoofExitInternal();
        }
#endif
#endif

    }

    public class ProfilerMarkerTests : ProfilerTestFixture
    {
        public static unsafe void TestProfilerTimeGoesForwardInternal(ref ProfilerMarker marker, ref ProfilerMarker marker2, ref ProfilerMarker marker3, ref ProfilerMarker marker4)
        {
            ProfilerProtocolThread.Stream.buffer->m_BufferList->RecycleStreamAndFreeExtra();

            for (int i = 0; i < 4; i++)
            {
                marker.Begin();
                marker2.Begin();
                marker3.Begin();
                marker4.Begin();
                marker4.End();
                marker3.End();
                marker2.End();
                marker.End();

                marker.Begin();
                marker.End();
                marker.Begin();
                marker.End();
                marker.Begin();
                marker.End();
                marker.Begin();
                marker.End();
            }

            const int k_BeginAndEndLength = 72;
            const int k_TimeOffset = 56;

            ulong prevTime = 0;
            int offset = k_TimeOffset;
            for (int i = 0; i < 4 * 16; i++)
            {
                ulong currTime = *(ulong*)((byte*)ProfilerProtocolThread.Stream.buffer->m_BufferList->BufferRead->Buffer + offset);
                if (currTime <= prevTime)
                    throw new Exception("Time did not go forward");
                prevTime = currTime;
                offset += k_BeginAndEndLength;
            }
        }

        [Test]
        public unsafe void TestProfilerTimeGoesForward()
        {
            ProfilerMarker marker = new ProfilerMarker("test");
            ProfilerMarker marker2 = new ProfilerMarker("test2");
            ProfilerMarker marker3 = new ProfilerMarker("test3");
            ProfilerMarker marker4 = new ProfilerMarker("test4");
            Assert.DoesNotThrow(() => TestProfilerTimeGoesForwardInternal(ref marker, ref marker2, ref marker3, ref marker4));
        }

        [Test]
        public unsafe void TestRecreateMarkerJustGetsCached()
        {
            IntPtr[] markers = new IntPtr[256];
            for (int i = 0; i < markers.Length; i++)
            {
                ProfilerMarker marker = new ProfilerMarker($"Test {i}");
                markers[i] = marker.Handle;
            }

            for (int i = 0; i < markers.Length; i++)
            {
                ProfilerMarker marker = new ProfilerMarker($"Test {i}");
                Assert.IsTrue(marker.Handle == markers[i]);
            }
        }

#if !NET_DOTS
        //---------------------------------------------------------------------------------------------------
        // TEST BASIC MULTITHREADED RANDOM ACCESS ON MARKERS
        //---------------------------------------------------------------------------------------------------
        [Test]
        public unsafe void TestRandomlyCreateAndGetMarkersMultithreaded()
        {
            ManualResetEvent mre = new ManualResetEvent(false);
            const int k_NumTotalMarkers = 65536;
            const int k_NumUniqueMarkers = 1024;
            string[] markerName = new string[k_NumUniqueMarkers];

            System.Random rand = new System.Random();

            for (int i = 0; i < k_NumUniqueMarkers; i++)
            {
                for (int j = 0; j < rand.Next(1, 20); j++)
                    markerName[i] += rand.Next(10).ToString();
            }

            Thread[] threads = new Thread[16];
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread((object p) =>
                {
                    System.Random innerRand = (System.Random)p;
                    int len = k_NumTotalMarkers / threads.Length;
                    mre.WaitOne();
                    for (int j = 0; j < len; j++)
                    {
                        ProfilerMarker marker = new ProfilerMarker(markerName[innerRand.Next(k_NumUniqueMarkers)]);
                        Assert.IsTrue(((Development.Profiling.Profiler.MarkerBucketNode*)marker.m_Ptr)->markerId < k_NumUniqueMarkers);
                    }
                });
                System.Random localRand = new System.Random(rand.Next());
                threads[i].Start(localRand);
            }

            Thread.Sleep(100);
            mre.Set();

            for (int i = 0; i < threads.Length; i++)
                threads[i].Join();
        }

        //---------------------------------------------------------------------------------------------------
        // TEST PROBLEMATIC SCENARIO OF UNIQUE MARKERS CREATED CONCURRENTLY ALL IN ONE BUCKET
        //---------------------------------------------------------------------------------------------------
        private IntPtr[] CreateMarkersThreaded()
        {
            ManualResetEvent mre = new ManualResetEvent(false);

            IntPtr[] markers = new IntPtr[256];
            Thread[] threads = new Thread[16];

            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread((object p) =>
                {
                    int section = (int)p;
                    mre.WaitOne();
                    for (int j = 0; j < threads.Length; j++)
                    {
                        int index = section * threads.Length + j;
                        ProfilerMarker marker = new ProfilerMarker($"Test {index}");
                        markers[index] = marker.Handle;
                    }
                });
                threads[i].Start(i);
            }

            Thread.Sleep(100);
            mre.Set();

            for (int i = 0; i < threads.Length; i++)
                threads[i].Join();

            return markers;
        }

        [Test]
        public unsafe void TestCreateMarkersMultithreaded()
        {
            var markers = CreateMarkersThreaded();

            for (int i = 0; i < markers.Length; i++)
            {
                string markerName = Unity.Development.Profiling.Profiler.MarkerGetStringName(markers[i]);
                Assert.IsTrue(markerName == $"Test {i}");
            }
        }

        [Test]
        public unsafe void TestRecreateMarkersMultithreadedGetCachedOnce()
        {
            var markers = CreateMarkersThreaded();

            for (int i = 0; i < markers.Length; i++)
            {
                ProfilerMarker marker = new ProfilerMarker($"Test {i}");
                Assert.IsTrue(marker.Handle == markers[i]);
            }
        }

        [Test]
        public unsafe void TestRecreateMarkersMultithreadedGetCachedOnceInThreads()
        {
            var markers = CreateMarkersThreaded();

            ManualResetEvent mre = new ManualResetEvent(false);
            Thread[] threads = new Thread[16];

            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread((object p) =>
                {
                    int section = (int)p;
                    mre.WaitOne();
                    for (int j = 0; j < threads.Length; j++)
                    {
                        int index = section * threads.Length + j;
                        ProfilerMarker marker = new ProfilerMarker($"Test {index}");
                        Assert.IsTrue(marker.Handle == markers[index]);
                    }
                });
                threads[i].Start(i);
            }

            Thread.Sleep(100);
            mre.Set();

            for (int i = 0; i < threads.Length; i++)
                threads[i].Join();
        }

        //---------------------------------------------------------------------------------------------------
        // TEST WORST CASE SCENARIO OF DUPLICATE MARKERS CREATED CONCURRENTLY ALL IN ONE BUCKET
        //---------------------------------------------------------------------------------------------------
        private IntPtr[] CreateMarkersThreadedDuplicates()
        {
            ManualResetEvent mre = new ManualResetEvent(false);

            IntPtr[] markers = new IntPtr[256];
            Thread[] threads = new Thread[16];
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread((object p) =>
                {
                    int section = (int)p;
                    mre.WaitOne();
                    for (int j = 0; j < threads.Length; j++)
                    {
                        int index = section * threads.Length + j;
                        ProfilerMarker marker = new ProfilerMarker($"Test {j}");
                        markers[index] = marker.Handle;
                    }
                });
                threads[i].Start(i);
            }

            Thread.Sleep(100);
            mre.Set();

            for (int i = 0; i < threads.Length; i++)
                threads[i].Join();

            return markers;
        }

        [Test]
        public unsafe void TestCreateDuplicateMarkersMultithreaded()
        {
            var markers = CreateMarkersThreadedDuplicates();

            for (int i = 0; i < markers.Length; i += 16)
            {
                for (int j = 0; j < 16; j++)
                {
                    string markerName = Unity.Development.Profiling.Profiler.MarkerGetStringName(markers[i + j]);
                    Assert.IsTrue(markerName == $"Test {j}");
                }
            }
        }

        [Test]
        public unsafe void TestRecreateDuplicateMarkersMultithreadedGetCachedOnce()
        {
            var markers = CreateMarkersThreadedDuplicates();

            for (int i = 0; i < markers.Length; i += 16)
            {
                for (int j = 0; j < 16; j++)
                {
                    ProfilerMarker marker = new ProfilerMarker($"Test {j}");
                    Assert.IsTrue(marker.Handle == markers[i + j]);
                }
            }
        }

        [Test]
        public unsafe void TestRecreateDuplicateMarkersMultithreadedGetCachedOnceInThreads()
        {
            var markers = CreateMarkersThreadedDuplicates();

            ManualResetEvent mre = new ManualResetEvent(false);
            Thread[] threads = new Thread[16];

            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new Thread((object p) =>
                {
                    int section = (int)p;
                    mre.WaitOne();
                    for (int j = 0; j < threads.Length; j++)
                    {
                        int index = section * threads.Length + j;
                        ProfilerMarker marker = new ProfilerMarker($"Test {j}");
                        Assert.IsTrue(marker.Handle == markers[index]);
                    }
                });
                threads[i].Start(i);
            }

            Thread.Sleep(100);
            mre.Set();

            for (int i = 0; i < threads.Length; i++)
                threads[i].Join();
        }
#endif
    }

#if UNITY_PORTABLE_TEST_RUNNER
    [BurstCompile]
    public class ProfilerBurstSupportTests : ProfilerTestFixture
    {
        [BurstDiscard]
        private static void EnsureBurstedOrThrow()
        {
            throw new Exception("This wasn't burst compiled");
        }

        [BurstCompile]
        public static void BurstedProfilerMethods(ref ProfilerMarker marker)
        {
            EnsureBurstedOrThrow();

            // These API calls actually prove out a LOT of internal API burst-ability
            marker.Begin();
            marker.End();

            using (marker.Auto())
            {
            }

            ProfilerProtocolThread.SendNewFrame();
        }

        [Test]
        public void CanBurstProfilerMethods()
        {
            ProfilerMarker marker = new ProfilerMarker("Test marker");
            BurstedProfilerMethods(ref marker);
        }

        [BurstCompile]
        public static void BurstedTestProfilerTimeGoesForwardInternal(ref ProfilerMarker marker, ref ProfilerMarker marker2, ref ProfilerMarker marker3, ref ProfilerMarker marker4)
        {
            EnsureBurstedOrThrow();
            ProfilerMarkerTests.TestProfilerTimeGoesForwardInternal(ref marker, ref marker2, ref marker3, ref marker4);
        }

        [Test]
        public void BurstedTestProfilerTimeGoesForward()
        {
            ProfilerMarker marker = new ProfilerMarker("test");
            ProfilerMarker marker2 = new ProfilerMarker("test2");
            ProfilerMarker marker3 = new ProfilerMarker("test3");
            ProfilerMarker marker4 = new ProfilerMarker("test4");
            BurstedTestProfilerTimeGoesForwardInternal(ref marker, ref marker2, ref marker3, ref marker4);
        }
    }
#endif
}
#endif
