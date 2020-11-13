#if ENABLE_PROFILER

using System;
using System.Runtime.InteropServices;
using UnityEngine.Networking.PlayerConnection;
using static System.Text.Encoding;
using static Unity.Baselib.LowLevel.Binding;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using Unity.Burst;
using Unity.Development.PlayerConnection;
using Unity.Collections;
using UnityEngine.Assertions;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Development.Profiling
{
    // unity\Runtime\Profiler\Profiler.h
    [Flags]
    public enum ProfilerModes : int
    {
        ProfileDisabled = 0,
        ProfileCPU = 1 << 0,
        ProfileGPU = 1 << 1,
        ProfileRendering = 1 << 2,
        ProfileMemory = 1 << 3,
        ProfileAudio = 1 << 4,
        ProfileVideo = 1 << 5,
        ProfilePhysics = 1 << 6,
        ProfilePhysics2D = 1 << 7,
        ProfileNetworkMessages = 1 << 8,
        ProfileNetworkOperations = 1 << 9,
        ProfileUI = 1 << 10,
        ProfileUIDetails = 1 << 11,
        ProfileGlobalIllumination = 1 << 12,
        ProfileCount = 13,
        ProfileAll = (1 << ProfileCount) - 1
    };

    internal enum ProfilerMemoryRecordMode : int
    {
        RecordNone = 0,
        RecordManagedCallstack = 1,
        RecordAllCallstackFast = 2,
        RecordAllCallstackFull = 3,
    };

    public class PlayerConnectionProfiler
    {
        // Since the properties like Enabled are used from Burst, all the static fields
        // here have to be either Burstable or in another class:
        static class Managed
        {
            public static bool init = false;
            public static ProfilerMemoryRecordMode memoryRecordMode = ProfilerMemoryRecordMode.RecordNone;
        }

        public static bool Enabled => Mode != ProfilerModes.ProfileDisabled;
        public unsafe static ProfilerModes Mode => mode.UnsafeDataPointer == null ? ProfilerModes.ProfileDisabled : mode.Data;
        internal static readonly SharedStatic<ProfilerModes> mode = SharedStatic<ProfilerModes>.GetOrCreate<PlayerConnectionProfiler, ProfilerModes>();

        public static unsafe void Initialize()
        {
            if (Managed.init)
                return;

            mode.Data = ProfilerModes.ProfileDisabled;

            Connection.RegisterMessage(EditorMessageIds.kProfilerSetMode, (MessageEventArgs a) =>
            {
                bool oldEnabled = Enabled;
                fixed (byte* d = a.data)
                {
                    mode.Data = (ProfilerModes)(*(int*)d);
                }
                if (Enabled && !oldEnabled)
                {
                    ProfilerProtocolSession.SendProfilingCapabilityMessage();
                    ProfilerProtocolSession.SendProfilingSessionInfo();
                }
            });

            Connection.RegisterMessage(EditorMessageIds.kProfilerSetMemoryRecordMode, (MessageEventArgs a) =>
            {
                fixed (byte* d = a.data)
                {
                    Managed.memoryRecordMode = (ProfilerMemoryRecordMode)(*(int*)d);
                }
            });

            Managed.init = true;
        }

        public static unsafe void Shutdown()
        {
            mode.Data = ProfilerModes.ProfileDisabled;

            Managed.init = false;
        }
    }

    // unity\Modules\Profiler\Runtime\Protocol.h
    internal enum ProfilerMessageType : ushort
    {
        // GLOBAL DATA
        ProfilerState = 0,      // Profiler state change: enabled/disabled, mode, etc.
        MarkerInfo = 1,        // Marker information - name, metadata layout
        //Callstack = 3,          // void* to function name
        AllProfilerStats = 4,   // Profiler stats (written on a main thread)
        //AudioInstanceData = 5,  // Combined audio stats
        //UISystemCanvas = 6,     // UI stats
        //UIEvents = 7,
        //MethodJitInfo = 8,
        GlobalMessagesCount = 32,

        // THREAD SPECIFIC DATA
        ThreadInfo = GlobalMessagesCount + 1,   // Thread name and id
        Frame = GlobalMessagesCount + 2,        // Frame boundary
        BeginSample = GlobalMessagesCount + 4,
        EndSample = GlobalMessagesCount + 5,
        Sample = GlobalMessagesCount + 6,
        //BeginSampleWithInstanceID = GlobalMessagesCount + 7,
        //SampleWithInstanceID = GlobalMessagesCount + 9,
        BeginSampleWithMetadata = GlobalMessagesCount + 10,
        EndSampleWithMetadata = GlobalMessagesCount + 11,
        SampleWithMetadata = GlobalMessagesCount + 12,
        //GCAlloc = GlobalMessagesCount + 20,

        // ASYNC DATA
        //LocalAsyncMetadataAnchor = GlobalMessagesCount + 21,  // Thread local async metadata anchor (e.g. GPUSamples generated on render thread.)
        //LocalAsyncMetadata = GlobalMessagesCount + 22,
        //LocalGPUSample = GlobalMessagesCount + 23,
        //CleanupThread = GlobalMessagesCount + 24,
        //FlowEvent = GlobalMessagesCount + 25,
    };

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProfilerProtocolStream
    {
        internal const uint kPacketBlockHeader = 0xB10C7EAD;
        internal const uint kPacketBlockFooter = 0xB10CF007;

        // ProfilerMarkers already provide a Begin() and End() API, so this is only useful
        // for procedural profiling when you don't have a ProfilerMarker object instantiated i.e.
        //   IntPtr marker = GetOrCreateMarker("MarkerName", ...);
        //   MarkerBegin(marker);
        //   stack.PushMarker(marker);
        //   [...]
        //   marker = stack.PopMarker();
        //   MarkerEnd(marker);
        //
        // This is very slow due to the lookup of the marker by name, and is reserved only for
        // cases where there are no other options (such as the profiling callbacks built into bgfx)
        // It is how older Unity players profiled through the UnityEngine.Profiling API,
        // but is deprecated now in favor of the faster ProfilerMarkers.
        internal struct MarkerStack
        {
            public const int k_stackSize = 32;
            public int stackPos;
            public unsafe fixed long beginStack[k_stackSize];

            [BurstDiscard]
            private static void ThrowError(bool push)
            {
                if (push)
                    throw new InvalidOperationException("Too many nested BeginSample() calls");
                else
                    throw new InvalidOperationException("Too many EndSample() calls (no matching BeginSample)");
            }

            public unsafe void PushMarker(IntPtr marker)
            {
                if (stackPos == k_stackSize)
                    ThrowError(true);

                beginStack[stackPos++] = (long)marker;
            }

            public unsafe IntPtr PopMarker()
            {
                if (stackPos == 0)
                    ThrowError(true);

                stackPos--;
                return (IntPtr)beginStack[stackPos];
            }
        }

        internal ulong threadId;

        internal uint blockIndex;
        internal int blockSizeExpected;
        internal int blockSizeStart;

        internal uint profiledFrame;

        internal unsafe MessageStreamBuilder *buffer;

        internal MarkerStack markerStack;

        internal ProfilerProtocolStream(ulong streamId)
        {
            threadId = (ulong)streamId;
            blockIndex = 0;
            blockSizeStart = 0;
            blockSizeExpected = 0;
            profiledFrame = 0;
            unsafe
            {
                // This builder is tracked so all can be freed during application shutdown - no need to destroy manually
                buffer = MessageStreamManager.CreateStreamBuilder();
            }
            markerStack = new MarkerStack();
        }

        internal unsafe void EmitBlockBegin(ProfilerMessageType type, int byteSize)
        {
            // message type and 0 padded 32 bit alignment are part of block size
            byteSize += 4;
            blockSizeExpected = byteSize;

            // Block end data will start with a 32 bit value which must be aligned, so consider that when
            // expressing size of the block data
            while ((byteSize & 3) != 0)
                byteSize++;
            buffer->WriteData<uint>(kPacketBlockHeader);
            buffer->WriteData<uint>(blockIndex);
            buffer->WriteData<ulong>(threadId);
            buffer->WriteData<int>(byteSize);
            blockSizeStart = buffer->DataToSendBytes;
            buffer->WriteData<uint>((uint)type);
        }

        internal unsafe void EmitBlockEnd()
        {
            Profiler.CheckError(blockSizeExpected == buffer->DataToSendBytes - blockSizeStart);

            while ((buffer->m_BufferList->TotalBytes & 3) != 0)
                buffer->WriteData((byte)0);

            // The block end message is validated by containing the potential NEXT block's index
            blockIndex++;
            buffer->WriteData<uint>(blockIndex);
            buffer->WriteData<uint>(kPacketBlockFooter);
        }

        internal unsafe void EmitSample(ProfilerMessageType type, uint markerId, ulong sysTicks, byte metadataCount = 0)
        {
            int bytesData = 16;
            if (metadataCount > 0)
                bytesData += 4;

            EmitBlockBegin(type, bytesData);

            buffer->WriteData<uint>(0);  // flag (1 for mono sample)
            buffer->WriteData<uint>(markerId);
            buffer->WriteData<ulong>(sysTicks);
            if (metadataCount > 0)
                buffer->WriteData<uint>(metadataCount);

            EmitBlockEnd();
        }
    }

    public class ProfilerProtocolThread
    {
        private struct ProfilerThreadTlsKey { }
        static private readonly SharedStatic<UIntPtr> streamTls = SharedStatic<UIntPtr>.GetOrCreate<ProfilerThreadTlsKey>();

        static unsafe internal ref ProfilerProtocolStream Stream
        {
            get
            {
                UIntPtr tlsHandle = streamTls.Data;
                ProfilerProtocolStream* stream = (ProfilerProtocolStream*)Baselib_TLS_Get(tlsHandle);
                if (stream == null)
                {
                    stream = (ProfilerProtocolStream*)UnsafeUtility.Malloc(sizeof(ProfilerProtocolStream), 0, Allocator.Persistent);
                    *stream = new ProfilerProtocolStream((ulong)Baselib_Thread_GetCurrentThreadId());
                    Baselib_TLS_Set(tlsHandle, (UIntPtr)stream);
                }

                return ref *stream;
            }
        }

        [BurstDiscard]
        private static void ThrowErrorInit()
        {
            throw new InvalidOperationException("ProfilerProtocolThread already initialized globally.");
        }

        public unsafe static void Initialize()
        {
            if (streamTls.Data != UIntPtr.Zero)
                ThrowErrorInit();
            streamTls.Data = Baselib_TLS_Alloc();
        }

        public unsafe static void Shutdown()
        {
            if (streamTls.Data != UIntPtr.Zero)
                Baselib_TLS_Free(streamTls.Data);
            streamTls.Data = UIntPtr.Zero;
        }

        // Threadsafe
        static public unsafe bool SendBeginSample(uint markerId, ulong sysTicks)
        {
            if (!PlayerConnectionProfiler.Enabled)
                return false;

            Stream.buffer->MessageBegin(EditorMessageIds.kProfilerDataMessage);
            Stream.EmitSample(ProfilerMessageType.BeginSample, markerId, sysTicks);
            Stream.buffer->MessageEnd();
            return true;
        }

        // Threadsafe
        static public unsafe void SendSample(uint markerId, ulong sysTicks)
        {
            if (!PlayerConnectionProfiler.Enabled)
                return;

            Stream.buffer->MessageBegin(EditorMessageIds.kProfilerDataMessage);
            Stream.EmitSample(ProfilerMessageType.Sample, markerId, sysTicks);
            Stream.buffer->MessageEnd();
        }

        // Threadsafe
        static public unsafe void SendEndSample(uint markerId, ulong sysTicks)
        {
            if (!PlayerConnectionProfiler.Enabled)
                return;

            Stream.buffer->MessageBegin(EditorMessageIds.kProfilerDataMessage);
            Stream.EmitSample(ProfilerMessageType.EndSample, markerId, sysTicks);
            Stream.buffer->MessageEnd();
        }

        static public unsafe void SendBeginSampleWithMetadata(uint markerId, ulong sysTicks, byte metadataCount)
        {
            if (!PlayerConnectionProfiler.Enabled)
                return;

            Stream.buffer->MessageBegin(EditorMessageIds.kProfilerDataMessage);
            Stream.EmitSample(ProfilerMessageType.BeginSampleWithMetadata, markerId, sysTicks, metadataCount);
            Stream.buffer->MessageEnd();
        }

        static public unsafe void SendSampleWithMetadata(uint markerId, ulong sysTicks, byte metadataCount)
        {
            if (!PlayerConnectionProfiler.Enabled)
                return;

            Stream.buffer->MessageBegin(EditorMessageIds.kProfilerDataMessage);
            Stream.EmitSample(ProfilerMessageType.SampleWithMetadata, markerId, sysTicks, metadataCount);
            Stream.buffer->MessageEnd();
        }

        static public unsafe void SendEndSampleWithMetadata(uint markerId, ulong sysTicks, byte metadataCount)
        {
            if (!PlayerConnectionProfiler.Enabled)
                return;

            Stream.buffer->MessageBegin(EditorMessageIds.kProfilerDataMessage);
            Stream.EmitSample(ProfilerMessageType.EndSampleWithMetadata, markerId, sysTicks, metadataCount);
            Stream.buffer->MessageEnd();
        }

        static public unsafe void SendNewFrame()
        {
            if (!PlayerConnectionProfiler.Enabled)
                return;

            // This needs to happen with any thread that tracks by frame, and it must be the only message in the block
            Stream.buffer->MessageBegin(EditorMessageIds.kProfilerDataMessage);
            Stream.EmitBlockBegin(ProfilerMessageType.Frame, 12);
            Stream.buffer->WriteData<uint>(Stream.profiledFrame++);
            Stream.buffer->WriteData<ulong>(Profiler.GetProfilerTime());
            Stream.EmitBlockEnd();
            Stream.buffer->MessageEnd();
        }
    }

    // unity\Modules\Profiler\Runtime\Protocol.h/cpp
    // unity\Modules\Profiler\Runtime\PreThreadProfiler.h/cpp
    // [Header]
    // [
    //   [BlockHeader[Message ... Message]BlockFooter] ... [BlockHeader[Message ... Message]BlockFooter]
    // ]
    public class ProfilerProtocolSession
    {
        [DllImport("lib_unity_zerojobs")]
        private static extern ulong Time_GetTicksToNanosecondsConversionRatio_Numerator();

        [DllImport("lib_unity_zerojobs")]
        private static extern ulong Time_GetTicksToNanosecondsConversionRatio_Denominator();

        // kProtocolVersion is the date of the last protocol modification
        // It should match the protocol we use as defined in big unity, and will enforce only working with versions
        // of unity that support it or later.
        internal const uint kProtocolVersion = 0x20191122;

        internal const uint kSessionGlobalHeader = 0x55334450;  // 'U3DP'
        internal const ulong kSessionId = 0xffff_ffff_ffff_ffff;

        static unsafe internal ProfilerProtocolStream streamSession;
        static public int TotalMarkers { get; private set; }

        static internal void Initialize()
        {
            streamSession = new ProfilerProtocolStream(kSessionId);
        }

        //---------------------------------------------------------------------------------------------------
        // Helpers
        //---------------------------------------------------------------------------------------------------
        static private int GetStringBytesCount(int byteCountUtf8)
        {
            // 32-bit string length + utf8 string data + 0-padded 32 bit byte alignment
            while ((byteCountUtf8 & 3) != 0)
                byteCountUtf8++;
            return 4 + byteCountUtf8;
        }

        static private unsafe void EmitStringUtf8(byte* textUtf8, int byteCountUtf8)
        {
            streamSession.buffer->WriteData<int>(byteCountUtf8);
            streamSession.buffer->WriteRaw(textUtf8, byteCountUtf8);
            while ((streamSession.buffer->DeferredSize & 3) != 0)
                streamSession.buffer->WriteData<byte>(0);
        }

        //---------------------------------------------------------------------------------------------------
        // Global Data (Profiling Session Information - must be sent with session "thread id")
        //---------------------------------------------------------------------------------------------------
        static private unsafe void EmitThreadInfo(ulong threadId, ulong sysTicksStart, bool frameIndependent, byte* groupUtf8, int groupBytes, byte* nameUtf8, int nameBytes)
        {
            // Thread info can be sent as session information or belonging to running thread

            int bytesData = 20 + GetStringBytesCount(groupBytes) + GetStringBytesCount(nameBytes);

            streamSession.EmitBlockBegin(ProfilerMessageType.ThreadInfo, bytesData);

            streamSession.buffer->WriteData<ulong>(threadId);
            streamSession.buffer->WriteData<ulong>(sysTicksStart);
            streamSession.buffer->WriteData<uint>(frameIndependent ? 1u : 0u);  // flags

            EmitStringUtf8(groupUtf8, groupBytes);
            EmitStringUtf8(nameUtf8, nameBytes);

            streamSession.EmitBlockEnd();
        }

        static private unsafe void EmitMarkerInfo(uint markerId, ushort categoryId, ushort flags, byte* nameUtf8, int nameBytes, byte metadataCount)
        {
            int bytesData = 8 + GetStringBytesCount(nameBytes) + 4;

            streamSession.EmitBlockBegin(ProfilerMessageType.MarkerInfo, bytesData);

            streamSession.buffer->WriteData<uint>(markerId);
            streamSession.buffer->WriteData<ushort>(flags);
            streamSession.buffer->WriteData<ushort>(categoryId);
            EmitStringUtf8(nameUtf8, nameBytes);
            streamSession.buffer->WriteData<uint>(metadataCount);

            streamSession.EmitBlockEnd();
        }

        // NOT THREAD SAFE
        // This must be protected externally with the same lock that protects the hash tables.
        // It isn't handle inside this method because during the same lock, we must then and only then
        // submit all player connection buffers.
        // Otherwise, new markers may be created after this call and prior to submitting buffers
        // and marker data could be sent to the editor profiler without the proper marker info having been sent.
        static internal unsafe void EmitNewMarkersAndThreads(bool forceEmitAll)
        {
            // All markers we know about
            var markerBufferNode = Profiler.MarkersHeadBufferNode;
            while (markerBufferNode != null)
            {
                if (markerBufferNode->isNew)
                {
                    // We don't do this on initial allocation because it can happen before all statics are initialized, when
                    // AccumStats is not be ready yet
                    ProfilerStats.AccumStats.memReservedProfiler.Accumulate(Profiler.k_HashChunkSize);
                    ProfilerStats.AccumStats.memUsedProfiler.Accumulate(Profiler.k_HashChunkSize);
                    markerBufferNode->isNew = false;
                }
                for (int i = 0; i < markerBufferNode->size; i++)
                {
                    var marker = &markerBufferNode->MarkersBuffer[i];
                    if (marker->nameBytes > 0 && (forceEmitAll || !marker->init))
                    {
                        EmitMarkerInfo(marker->markerId, marker->categoryId, marker->flags, marker->nameUtf8, marker->nameBytes, 0);
                        if (!marker->init)
                        {
                            TotalMarkers++;
                            marker->init = true;
                        }
                    }
                }
                markerBufferNode = markerBufferNode->next;
            }

            // Main thread
            var threadBufferNode = Profiler.ThreadsHeadBufferNode;
            while (threadBufferNode != null)
            {
                if (threadBufferNode->isNew)
                {
                    // We don't do this on initial allocation because it can happen before all statics are initialized, when
                    // AccumStats is not be ready yet
                    ProfilerStats.AccumStats.memReservedProfiler.Accumulate(Profiler.k_HashChunkSize);
                    ProfilerStats.AccumStats.memUsedProfiler.Accumulate(Profiler.k_HashChunkSize);
                    threadBufferNode->isNew = false;
                }
                for (int i = 0; i < threadBufferNode->size; i++)
                {
                    var thread = &threadBufferNode->ThreadsBuffer[i];
                    if (thread->nameBytes > 0 && (forceEmitAll || !thread->init))
                    {
                        EmitThreadInfo(thread->threadId, thread->sysTicksStart, thread->frameIndependent,
                            thread->groupUtf8, thread->groupBytes, thread->nameUtf8, thread->nameBytes);
                        thread->init = true;
                    }
                }
                threadBufferNode = threadBufferNode->next;
            }
        }

        // Threadsafe
        static internal unsafe void SendProfilingCapabilityMessage()
        {
            streamSession.buffer->MessageBegin(EditorMessageIds.kProfilerPlayerInfoMessage);
            streamSession.buffer->WriteData<uint>(1);  // version - ONLY supported value and it must be this value
            streamSession.buffer->WriteData<byte>(0);  // is deep profiling supported
            streamSession.buffer->WriteData<byte>(0);  // is deep profiler enabled
            streamSession.buffer->WriteData<byte>(0);  // is memory allocation callstack supported
            streamSession.buffer->MessageEnd();
        }

        // Threadsafe but must be called from main thread
        static internal unsafe void SendProfilingSessionInfo()
        {
            ulong ticks = Profiler.GetProfilerTime();
            ulong conversionNum = Time_GetTicksToNanosecondsConversionRatio_Numerator();
            ulong conversionDen = Time_GetTicksToNanosecondsConversionRatio_Denominator();

            streamSession.buffer->MessageBegin(EditorMessageIds.kProfilerDataMessage);

            // Global Session Header
            streamSession.buffer->WriteData<uint>(kSessionGlobalHeader);
            streamSession.buffer->WriteData<byte>(1);     // 1 = little endian  0 = big endian
            streamSession.buffer->WriteData<byte>(1);     // 1 = aligned memory access  0 = unaligned memory access
            streamSession.buffer->WriteData<ushort>(0);   // build target platform (not currently supported)
            streamSession.buffer->WriteData<uint>(kProtocolVersion);
            streamSession.buffer->WriteData<ulong>(conversionNum);     // time numerator (multiply by this ratio to convert time to nanoseconds)
            streamSession.buffer->WriteData<ulong>(conversionDen);   // time denominator (multiply by this ratio to convert time to nanoseconds)
            streamSession.buffer->WriteData<ulong>(ProfilerProtocolThread.Stream.threadId);   // main thread id

            // Profiling Session State
            // The Unity profiler has a handshake
            // 1) Editor->Player - Send a general PlayerConnectionMessage "ProfilerSetMode"
            //                     (telling us profile is/isn't disabled according to user setup and which general things to profile)
            // 2) Player->Editor - Respond with a Profiler message describing current profiling state of the Player
            //                     (triggering the editor to enable the profiling session that the user has setup)
            streamSession.EmitBlockBegin(ProfilerMessageType.ProfilerState, 16);
            streamSession.buffer->WriteData<uint>(1);  // flags - currently only [0x01 : enabled] is defined
            streamSession.buffer->WriteData<ulong>(ticks);
            streamSession.buffer->WriteData<uint>(streamSession.profiledFrame);
            streamSession.EmitBlockEnd();

            EmitNewMarkersAndThreads(true);

            streamSession.buffer->MessageEnd();
        }

        static public unsafe void SendNewMarkersAndThreads()
        {
            if (!PlayerConnectionProfiler.Enabled)
                return;

            if (!Profiler.NeedsUpdate)
                return;

            streamSession.buffer->MessageBegin(EditorMessageIds.kProfilerDataMessage);
            EmitNewMarkersAndThreads(false);
            streamSession.buffer->MessageEnd();

            Profiler.NeedsUpdate = false;
        }

        // Threadsafe - must be only message in the block
        static public unsafe void SendNewFrame()
        {
            if (!PlayerConnectionProfiler.Enabled)
                return;

            // This needs to happen with the main thread id (not the session id) and be the only message in the block.
            // Note it can (and should) also be sent from other threads - the above comment only refers to main thread vs. session "thread",
            // which is the context of the method call we are in.
            streamSession.profiledFrame++;
            ProfilerProtocolThread.SendNewFrame();
        }

        static public unsafe void SendProfilerStats()
        {
            if (!PlayerConnectionProfiler.Enabled)
                return;

            ProfilerProtocolThread.Stream.buffer->MessageBegin(EditorMessageIds.kProfilerDataMessage);

            int bytesData = 4 + 4 + sizeof(ProfilerStats.AllProfilerStats);

            ProfilerProtocolThread.Stream.EmitBlockBegin(ProfilerMessageType.AllProfilerStats, bytesData);
            // While technically 32 bits isn't necessary, the protocol uses it to enforce alignment on following 32 bit values
            ProfilerProtocolThread.Stream.buffer->WriteData((int)ProfilerStats.GatheredStats);
            // The profiler protocol expects the profiler stats stream to be a collection of ints, and size is expected to be number of ints, not byte size
            ProfilerProtocolThread.Stream.buffer->WriteData(sizeof(ProfilerStats.AllProfilerStats) / 4);
            ProfilerProtocolThread.Stream.buffer->WriteData(ProfilerStats.Stats);
            ProfilerProtocolThread.Stream.EmitBlockEnd();

            ProfilerProtocolThread.Stream.buffer->MessageEnd();
        }
    }

    public static class Profiler
    {
        [DllImport("lib_unity_zerojobs")]
        internal static extern void PlayerConnectionMt_LockProfilerHashTables();

        [DllImport("lib_unity_zerojobs")]
        internal static extern void PlayerConnectionMt_UnlockProfilerHashTables();

        private const int k_MaxMarkerNameLength = 107;
        private const int k_MaxThreadNameLength = 47;
        internal const int k_HashChunkSize = 65536;

        // 1 marker info = 124/128 bytes (for 32/64 bit next pointers)
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct MarkerBucketNode
        {
            internal uint markerId;
            internal ushort flags;
            internal ushort categoryId;
            internal int nameBytes;
            internal fixed byte nameUtf8[k_MaxMarkerNameLength];
            internal bool init;
            internal MarkerBucketNode* next;  // offset 120

            internal static unsafe bool SearchFor(MarkerBucketNode* startNode, byte* nameUtf8, int nameBytes, ref MarkerBucketNode* bucketNode)
            {
                MarkerBucketNode* prev = startNode;
                while (startNode != null)
                {
                    if (startNode->nameBytes == nameBytes)
                    {
                        if (UnsafeUtility.MemCmp(nameUtf8, startNode->nameUtf8, nameBytes) == 0)
                        {
                            bucketNode = startNode;
                            return true;
                        }
                    }

                    prev = startNode;
                    startNode = startNode->next;
                }

                bucketNode = prev;
                return false;
            }
        }

        // 1 thread info = 124/128 bytes (for 32/64 bit next pointers)
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct ThreadBucketNode
        {
            internal ulong threadId;
            internal ulong sysTicksStart;
            internal int groupBytes;
            internal fixed byte groupUtf8[k_MaxThreadNameLength];
            internal bool frameIndependent;
            internal int nameBytes;  // offset 68
            internal fixed byte nameUtf8[k_MaxThreadNameLength];
            internal bool init;
            internal ThreadBucketNode* next;  // offset 120

            internal static unsafe bool SearchFor(ThreadBucketNode* startNode, ulong threadId, ref ThreadBucketNode* bucketNode)
            {
                ThreadBucketNode* prev = startNode;
                while (startNode != null)
                {
                    if (startNode->threadId == threadId)
                    {
                        bucketNode = startNode;
                        return true;
                    }

                    prev = startNode;
                    startNode = startNode->next;
                }

                bucketNode = prev;
                return false;
            }
        }

        // Allocating chunks of 64k for Markers
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct FastHashTableBufferNode
        {
            internal fixed byte buffer[k_HashChunkSize - 128];
            internal int capacity;
            internal int size;
            internal FastHashTableBufferNode* next;
            internal bool isNew;

            internal static unsafe FastHashTableBufferNode* Allocate(int typeSize)
            {
                var node = (FastHashTableBufferNode*)UnsafeUtility.Malloc(k_HashChunkSize, 16, Allocator.Persistent);
                UnsafeUtility.MemClear(node, k_HashChunkSize);
                node->capacity = (k_HashChunkSize - 128) / typeSize;
                node->size = 256;  // number of buckets
                node->isNew = true;
                return node;
            }

            // Returns the new tail (old one if pool not extended)
            internal unsafe FastHashTableBufferNode* ExtendPoolIfFull(int typeSize)
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

            internal MarkerBucketNode* MarkersBuffer
            {
                get
                {
                    fixed (byte* b = buffer)
                        return (MarkerBucketNode*)b;
                }
            }

            internal ThreadBucketNode* ThreadsBuffer
            {
                get
                {
                    fixed (byte* b = buffer)
                        return (ThreadBucketNode*)b;
                }
            }
        }

        internal unsafe static FastHashTableBufferNode* MarkersHeadBufferNode => markerHashTableHead;
        internal unsafe static FastHashTableBufferNode* ThreadsHeadBufferNode => threadHashTableHead;

        private unsafe static FastHashTableBufferNode* markerHashTableHead = null;
        private unsafe static FastHashTableBufferNode* markerHashTableTail = null;
        private static uint nextMarkerId = 0;

        private unsafe static FastHashTableBufferNode* threadHashTableHead = null;
        private unsafe static FastHashTableBufferNode* threadHashTableTail = null;

        private static bool initialized = false;
        internal static bool NeedsUpdate { get; set; } = false;

        public static unsafe void Initialize()
        {
            if (initialized)
                return;
            PlayerConnectionProfiler.Initialize();
            ProfilerProtocolSession.Initialize();
            ProfilerProtocolThread.Initialize();

            ThreadSetInfo((ulong)Baselib_Thread_GetCurrentThreadId(), GetProfilerTime(), false, "", "Main Thread");

            initialized = true;
        }

        public static unsafe void Shutdown()
        {
            if (!initialized)
                return;

            FastHashTableBufferNode* node = markerHashTableHead;
            while (node != null)
            {
                var prevNode = node;
                node = node->next;
                UnsafeUtility.Free(prevNode, Allocator.Persistent);
                ProfilerStats.AccumStats.memReservedProfiler.Accumulate(-k_HashChunkSize);
                ProfilerStats.AccumStats.memUsedProfiler.Accumulate(-k_HashChunkSize);
            }
            markerHashTableHead = null;
            markerHashTableTail = null;
            node = threadHashTableHead;
            while (node != null)
            {
                var prevNode = node;
                node = node->next;
                UnsafeUtility.Free(prevNode, Allocator.Persistent);
                ProfilerStats.AccumStats.memReservedProfiler.Accumulate(-k_HashChunkSize);
                ProfilerStats.AccumStats.memUsedProfiler.Accumulate(-k_HashChunkSize);
            }
            threadHashTableHead = null;
            threadHashTableTail = null;

            nextMarkerId = 0;
            NeedsUpdate = false;

            ProfilerProtocolThread.Shutdown();
            PlayerConnectionProfiler.Shutdown();
            initialized = false;
        }

        public static ulong GetProfilerTime()
        {
            return (ulong)Baselib_Timer_GetHighPrecisionTimerTicks();
        }

        public static unsafe void MarkerBegin(IntPtr markerHandle, int metadata)
        {
            var data = new ProfilerMarkerData { Type = (byte)ProfilerMarkerDataType.Int32, Size = (uint)UnsafeUtility.SizeOf<int>(), Ptr = UnsafeUtility.AddressOf(ref metadata) };
            ProfilerUnsafeUtility.BeginSampleWithMetadata(markerHandle, 1, &data);
        }

        public static unsafe void MarkerBegin(IntPtr markerHandle, uint metadata)
        {
            var data = new ProfilerMarkerData { Type = (byte)ProfilerMarkerDataType.UInt32, Size = (uint)UnsafeUtility.SizeOf<uint>(), Ptr = UnsafeUtility.AddressOf(ref metadata) };
            ProfilerUnsafeUtility.BeginSampleWithMetadata(markerHandle, 1, &data);
        }

        public static unsafe void MarkerBegin(IntPtr markerHandle, long metadata)
        {
            var data = new ProfilerMarkerData { Type = (byte)ProfilerMarkerDataType.Int64, Size = (uint)UnsafeUtility.SizeOf<long>(), Ptr = UnsafeUtility.AddressOf(ref metadata) };
            ProfilerUnsafeUtility.BeginSampleWithMetadata(markerHandle, 1, &data);
        }

        public static unsafe void MarkerBegin(IntPtr markerHandle, ulong metadata)
        {
            var data = new ProfilerMarkerData { Type = (byte)ProfilerMarkerDataType.UInt64, Size = (uint)UnsafeUtility.SizeOf<ulong>(), Ptr = UnsafeUtility.AddressOf(ref metadata) };
            ProfilerUnsafeUtility.BeginSampleWithMetadata(markerHandle, 1, &data);
        }

        public static unsafe void MarkerBegin(IntPtr markerHandle, float metadata)
        {
            var data = new ProfilerMarkerData { Type = (byte)ProfilerMarkerDataType.Float, Size = (uint)UnsafeUtility.SizeOf<float>(), Ptr = UnsafeUtility.AddressOf(ref metadata) };
            ProfilerUnsafeUtility.BeginSampleWithMetadata(markerHandle, 1, &data);
        }

        public static unsafe void MarkerBegin(IntPtr markerHandle, double metadata)
        {
            var data = new ProfilerMarkerData { Type = (byte)ProfilerMarkerDataType.Double, Size = (uint)UnsafeUtility.SizeOf<double>(), Ptr = UnsafeUtility.AddressOf(ref metadata) };
            ProfilerUnsafeUtility.BeginSampleWithMetadata(markerHandle, 1, &data);
        }

        public static unsafe void MarkerBegin(IntPtr markerHandle, string metadata)
        {
            var data = new ProfilerMarkerData { Type = (byte)ProfilerMarkerDataType.String16 };
            fixed (char* c = metadata)
            {
                data.Size = ((uint)metadata.Length + 1) * 2;
                data.Ptr = c;
                ProfilerUnsafeUtility.BeginSampleWithMetadata(markerHandle, 1, &data);
            }
        }

        // Burst/Thread safe
        internal static unsafe string MarkerGetStringName(IntPtr markerPtr)
        {
            MarkerBucketNode* marker = (MarkerBucketNode*)markerPtr;
            int charCount = UTF8.GetCharCount(marker->nameUtf8, marker->nameBytes);
            char* chars = stackalloc char[charCount];
            UTF8.GetChars(marker->nameUtf8, marker->nameBytes, chars, charCount);
            return new string(chars, 0, charCount);
        }

        // Burst/thread safe
        internal static unsafe void* MarkerGetOrCreate(ushort categoryId, byte* name, int nameBytes, ushort flags)
        {
            if (nameBytes <= 0)
                return null;

            MarkerBucketNode* marker = null;
            int bucket = (((nameBytes << 5) + (nameBytes >> 2)) ^ name[0]) & 255;

            if (markerHashTableHead != null)
            {
                // No need for locking yet - read operations on hash table are thread safe as long as we are careful about
                // modification during write and only allow one thread to write at a time.
                if (MarkerBucketNode.SearchFor(&markerHashTableHead->MarkersBuffer[bucket], name, nameBytes, ref marker))
                    return marker;
            }

            // The marker didn't exist in hash table. Need to lock so only one thread can modify at a time.
            // This path will usually only be taken during startup - after which markers should be found in the
            // above loop instead of needing to be created. Even this is a worse-case scenario because correct ProfilerMarker
            // usage will create/get them once, and they will exist as an instance which only calls MarkerBegin() and MarkerEnd()
            // when needed.
            PlayerConnectionMt_LockProfilerHashTables();

            if (marker == null)
            {
                if (markerHashTableHead == null)
                {
                    markerHashTableHead = FastHashTableBufferNode.Allocate(sizeof(MarkerBucketNode));
                    markerHashTableTail = markerHashTableHead;
                }

                marker = &markerHashTableHead->MarkersBuffer[bucket];
            }

            // In case this bucket was added to while another thread had the lock, the end-of-bucket
            // pointer needs to be increased. Also, it's possible the same exact name appears now.
            if (MarkerBucketNode.SearchFor(marker, name, nameBytes, ref marker))
            {
                PlayerConnectionMt_UnlockProfilerHashTables();
                return marker;
            }

            MarkerBucketNode* oldMarker = null;
            if (marker->nameBytes > 0)
            {
                // There is already a valid marker here at the end of the linked list - add a new one
                markerHashTableTail = markerHashTableTail->ExtendPoolIfFull(sizeof(MarkerBucketNode));

                MarkerBucketNode* newMarker = &markerHashTableTail->MarkersBuffer[markerHashTableTail->size];
                markerHashTableTail->size++;
                oldMarker = marker;
                marker = newMarker;
            }

            marker->init = false;
            marker->categoryId = categoryId;
            marker->flags = flags;
            marker->markerId = nextMarkerId++;
            marker->nameBytes = nameBytes;

            // Todo: When Burst printing works we should warn the user if a name is too long and will be truncated
            UnsafeUtility.MemCpy(marker->nameUtf8, name, Math.Min(nameBytes, k_MaxMarkerNameLength));

            // Do this last so if we find the node before locking, we don't have access to it unless it is otherwise fully assigned
            if (oldMarker != null)
                oldMarker->next = marker;

            PlayerConnectionMt_UnlockProfilerHashTables();

            NeedsUpdate = true;

            return marker;
        }

        // Burst/Thread safe
        internal static unsafe void MarkerBegin(void* markerPtr, void* metadata, int metadataBytes)
        {
            MarkerBucketNode* marker = (MarkerBucketNode*)markerPtr;
            ProfilerProtocolThread.SendBeginSample(marker->markerId, GetProfilerTime());
        }

        // Burst/Thread safe
        internal static unsafe void MarkerEnd(void* markerPtr)
        {
            MarkerBucketNode* marker = (MarkerBucketNode*)markerPtr;
            ProfilerProtocolThread.SendEndSample(marker->markerId, GetProfilerTime());
        }

        // NOT Burst safe due to string usage
        // Thread safe
        internal static unsafe void ThreadSetInfo(ulong threadId, ulong sysTicksStart, bool frameIndependent, string threadGroup, string threadName)
        {
            ThreadBucketNode* thread = null;
            int bucket = (int)threadId & 255;

            if (threadHashTableHead != null)
            {
                // No need for locking yet - read operations on hash table are thread safe as long as we are careful about
                // modification during write and only allow one thread to write at a time.
                if (ThreadBucketNode.SearchFor(&threadHashTableHead->ThreadsBuffer[bucket], threadId, ref thread))
                    return;
            }

            // The thread info didn't exist in hash table. Need to lock so only one thread can modify at a time.
            // This path will usually only be taken during startup when creating threads - after which thread info
            // should be found in the above loop instead of needing to be created.
            PlayerConnectionMt_LockProfilerHashTables();

            if (thread == null)
            {
                if (threadHashTableHead == null)
                {
                    threadHashTableHead = FastHashTableBufferNode.Allocate(sizeof(ThreadBucketNode));
                    threadHashTableTail = threadHashTableHead;
                }

                thread = &threadHashTableHead->ThreadsBuffer[bucket];
            }

            // In case this bucket was added to while another thread had the lock, the end-of-bucket
            // pointer needs to be increased. Also, it's possible the same exact name appears now.
            if (ThreadBucketNode.SearchFor(thread, threadId, ref thread))
            {
                PlayerConnectionMt_UnlockProfilerHashTables();
                return;
            }

            ThreadBucketNode* oldThread = null;
            if (thread->nameBytes > 0 || thread->groupBytes > 0)
            {
                // There is already a valid thread here at the end of the linked list - add a new one
                threadHashTableTail = threadHashTableTail->ExtendPoolIfFull(sizeof(ThreadBucketNode));

                ThreadBucketNode* newThread = &threadHashTableTail->ThreadsBuffer[threadHashTableTail->size];
                threadHashTableTail->size++;
                oldThread = thread;
                thread = newThread;
            }

            thread->init = false;
            Assert.IsTrue(thread->nameBytes <= k_MaxThreadNameLength);
            Assert.IsTrue(thread->groupBytes <= k_MaxThreadNameLength);
            fixed (char* c = threadGroup)
                thread->groupBytes = UTF8.GetBytes(c, threadGroup.Length, thread->groupUtf8, k_MaxThreadNameLength);
            fixed (char* c = threadName)
                thread->nameBytes = UTF8.GetBytes(c, threadName.Length, thread->nameUtf8, k_MaxThreadNameLength);
            thread->frameIndependent = frameIndependent;
            thread->sysTicksStart = sysTicksStart;
            thread->threadId = threadId;

            // Do this last so if we find the node before locking, we don't have access to it unless it is otherwise fully assigned
            if (oldThread != null)
                oldThread->next = thread;

            PlayerConnectionMt_UnlockProfilerHashTables();

            NeedsUpdate = true;
        }

        [BurstDiscard]
        public static void CheckError(bool condition)
        {
            if (!condition)
                throw new InvalidOperationException("Internal error in DOTS Runtime profiler");
        }
    }
}

#endif
