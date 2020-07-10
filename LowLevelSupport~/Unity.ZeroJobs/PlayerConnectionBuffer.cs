//-----------------------------------------------------------------------------------------------------------
// Player connection buffer - memory usage patterns
//-----------------------------------------------------------------------------------------------------------
// GENERAL:
// If large messages are sent or received, we don't typically expect a repeat frame-by-frame. This
// could include live-link data (receive), screenshots (send), or other one shot binary large objects.
// For this reason we always free memory associated with buffers larger than the reserve size.
//
// RECEIVE:
// - Wait for exactly message header data size
// - Wait for exactly message byte size data size (information provided by header)
// - Call any callbacks that will use this
// - Reset and do again
//
// If many messages are received, we don't typically expect a repeat frame-by-frame,
// so after we've processed we free the memory used to store it. 
// This is contrary to usage patterns in player connection buffer send use case.
//
// SEND:
// - Fill buffer chunk by chunk from arbitrary outside sources possibly on another thread
// - Swap buffer with a free buffer (allocating if necessary) between frames if not mid-message
// - Send the buffers that were ready
// - Flush what successfully sent and add to free list for next swap
//
// If many messages are sent, we typically expect a repeate frame-by-frame (such as profiler data),
// so cache newly allocated buffers for re-use. 
// This is contrary to usage patterns in player connection buffer receive use case.

#if ENABLE_PLAYERCONNECTION

using System;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
#if ENABLE_PROFILER
using Unity.Development.Profiling;
#endif

namespace Unity.Development.PlayerConnection
{
    // NOT THREAD SAFE - not designed for sharing
    // BURSTABLE
    //
    // This is a general purpose buffer API which allocates in chunks. Expanding avoids moving memory around by maintaining
    // a linked list of memory buffer chunks. Use-cases with specific semantics (namely receiving versus sending data in
    // the player connection based on usage patterns) is denoted with specific method names. Separating these methods into
    // individual classes/structs provides no perceivable organizational benefit and complicates reasoning about code
    // especially when ensuring these branch offs of functionality maintain burst and il2cpp compatiblilty.
    [StructLayout(LayoutKind.Sequential)]
    internal struct MessageStream
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct MessageStreamBuffer
        {
            private int m_Size;

            public IntPtr Buffer { get; private set; }
            public unsafe MessageStreamBuffer* Next { get; set; }
            public int Size
            {
                get => m_Size;
                set
                {
#if ENABLE_PROFILER
                    ProfilerStats.AccumStats.memUsedProfiler.Accumulate(value - m_Size);
#endif
                    m_Size = value;
                }
            }
            public int Capacity { get; private set; }

            public void Alloc(int bytes)
            {
                Free();

                unsafe
                {
                    Buffer = (IntPtr)UnsafeUtility.Malloc(bytes, 0, Unity.Collections.Allocator.Persistent);
#if ENABLE_PROFILER
                    ProfilerStats.AccumStats.memReservedProfiler.Accumulate(bytes);
#endif
                    Next = null;
                }

                Capacity = bytes;
                Size = 0;
            }

            public void Free()
            {
                if (Buffer == IntPtr.Zero)
                    return;

                unsafe
                {
                    UnsafeUtility.Free((void*)Buffer, Unity.Collections.Allocator.Persistent);
#if ENABLE_PROFILER
                    ProfilerStats.AccumStats.memReservedProfiler.Accumulate((long)-Capacity);
#endif
                    Next = null;
                }

                Buffer = IntPtr.Zero;
                Capacity = 0;
                Size = 0;
            }
        }

        private IntPtr m_BaselibNodeNext;  // This allows us to pretend this struct inherits from mpmc_node<pointer> in native code

        public unsafe MessageStreamBuffer* BufferWrite { get; set; }  // always tail node (may also be head)
        public unsafe MessageStreamBuffer* BufferRead { get; set; }  // always head node
        public int TotalBytes { get; set; }
        public readonly int m_Reserve;
        public byte Active { get; set; }

        public unsafe MessageStream(int reserveSize)
        {
            m_BaselibNodeNext = IntPtr.Zero;
            m_Reserve = reserveSize;
            BufferRead = (MessageStreamBuffer*)UnsafeUtility.Malloc(sizeof(MessageStreamBuffer), 0, Collections.Allocator.Persistent);
            *BufferRead = new MessageStreamBuffer();
            BufferRead->Alloc(m_Reserve);
            BufferWrite = BufferRead;
            TotalBytes = 0;
            Active = 0;
        }

        public unsafe void Free()
        {
            FreeRange(BufferRead, null);
            BufferWrite = null;
            BufferRead = null;
        }

        public unsafe void Allocate(int bytes)
        {
            // If there are already additional buffers in the list, we should try to use them.
            // Only if none are available with enough capacity do we allocate a new one on the heap
            while (BufferWrite->Size + bytes > BufferWrite->Capacity && BufferWrite->Next != null)
                BufferWrite = BufferWrite->Next;

            if (BufferWrite->Size + bytes > BufferWrite->Capacity)
            {
                BufferWrite->Next = (MessageStreamBuffer*)UnsafeUtility.Malloc(sizeof(MessageStreamBuffer), 0, Collections.Allocator.Persistent);
                *BufferWrite->Next = new MessageStreamBuffer();
                BufferWrite = BufferWrite->Next;
                BufferWrite->Alloc(bytes < m_Reserve ? m_Reserve : bytes);
            }
        }

        public unsafe void UpdateSize(int bytesUsed)
        {
            BufferWrite->Size += bytesUsed;
            TotalBytes += bytesUsed;
        }

        // Deallocates buffers in the list including beginNode and excluding endNode (null to free all)
        private unsafe void FreeRange(MessageStreamBuffer* beginNode, MessageStreamBuffer* endNode)
        {
            MessageStreamBuffer* node = beginNode;
            while (node != endNode)
            {
                var next = node->Next;
                TotalBytes -= node->Size;
                node->Free();
                UnsafeUtility.Free(node, Collections.Allocator.Persistent);
                node = next;
            }
        }

        // Used for send memory usage patterns (recycle without deallocation)
        // Clear buffers in the list until a desired node, and if there is a buffer which is partially finished
        // reduce it to the remaining data only.
        public unsafe void RecyclePartialStream(MessageStreamBuffer* recycleUntil, int recycleUntilPlusOffset)
        {
            if (recycleUntil != BufferRead)
            {
                MessageStreamBuffer* node = BufferRead;
                while (node != recycleUntil)
                {
                    TotalBytes -= node->Size;
                    node->Size = 0;
                    node = node->Next;
                }
            }

            if (recycleUntilPlusOffset > 0)
            {
                if (recycleUntil == null)
                    throw new ArgumentException("Flushing buffer past end");
                if (recycleUntilPlusOffset > recycleUntil->Size)
                    throw new ArgumentOutOfRangeException("Flushing buffer with offset past end");

                unsafe
                {
                    UnsafeUtility.MemCpy((void*)recycleUntil->Buffer, (void*)(recycleUntil->Buffer + recycleUntilPlusOffset), recycleUntil->Size - recycleUntilPlusOffset);
                }
                recycleUntil->Size -= recycleUntilPlusOffset;
                TotalBytes -= recycleUntilPlusOffset;
            }

            if (recycleUntil == null)
                BufferWrite = BufferRead;
        }

        // Used for send memory usage patterns (recycle without deallocation)
        public unsafe void RecycleStream()
        {
            RecyclePartialStream(null, 0);
        }

        // Used for receiving memory usage patterns (recycle with deallocation)
        public unsafe void RecycleStreamAndFreeExtra()
        {
            FreeRange(BufferRead->Next, null);
            BufferWrite = BufferRead;
            BufferRead->Size = 0;
            BufferRead->Next = null;
            TotalBytes = 0;
        }

        // Used for receiving over PlayerConnection for public API compatibility
        public byte[] ToByteArray(int offsetBegin, int offsetEnd)
        {
            if (offsetEnd > TotalBytes || offsetEnd < offsetBegin)
                throw new ArgumentOutOfRangeException("Bad offsetEnd in Player Connection data size");

            int bytesLeft = offsetEnd - offsetBegin;
            byte[] data = new byte[bytesLeft];

            unsafe
            {
                fixed (byte* m = data)
                {
                    MessageStream.MessageStreamBuffer* bufferReadNode = BufferRead;
                    int readOffset = offsetBegin;
                    int writeOffset = 0;

                    while (bytesLeft > 0)
                    {
                        while (readOffset >= bufferReadNode->Size)
                        {
                            readOffset -= bufferReadNode->Size;
                            bufferReadNode = bufferReadNode->Next;
                        }

                        int copyBytes = bufferReadNode->Size - readOffset;
                        if (bytesLeft < copyBytes)
                            copyBytes = bytesLeft;

                        UnsafeUtility.MemCpy(m + writeOffset, (void*)(bufferReadNode->Buffer + readOffset), copyBytes);

                        readOffset += copyBytes;
                        writeOffset += copyBytes;
                        bytesLeft -= copyBytes;
                    }
                }
            }

            return data;
        }
    }

    // NOT THREAD SAFE
    //   - except lockless submission to main thread
    // BURSTABLE
    //
    // This is used in any context where we want to accumulate data to send over Player Connection to Unity Editor.
    // It builds on the functionality of "Buffer" and specializes the behaviour to specifically send player
    // connection formatted messages.
    // Examples:
    //   Logging (one per thread for multithreaded in-job logging)
    //   Profiler (one per worker thread, one main thread, one non-thread "session" buffer)
    //   Unit Testing (create them as we need them)
    //   Live Link
    //   etc.
    //
    // Multithreaded synchronization is only achieved by TrySubmitBuffer() and is also burstable in case
    // of delayed synchronization due to contention (i.e. trying to submit but a job exists beyond end-of-frame)
    [StructLayout(LayoutKind.Sequential)]
    internal struct MessageStreamBuilder
    {
        [DllImport("lib_unity_zerojobs")]
        private static extern void PlayerConnectionMt_AtomicStore(IntPtr ptr, IntPtr value);

        [DllImport("lib_unity_zerojobs")]
        private static extern int PlayerConnectionMt_AtomicCompareExchange(IntPtr ptr, IntPtr compare, IntPtr value);

        public unsafe MessageStream* m_BufferList;
        private unsafe MessageStream* m_BufferListSwap;
        private unsafe int* m_DeferredSizePtr;
        private int m_DeferredSizeStart;
        private bool m_ResubmitStream;

        public unsafe bool HasDataToSend => m_BufferList->TotalBytes > 0;
        public unsafe int DataToSendBytes => m_BufferList->TotalBytes;
        public unsafe int DeferredSize => (m_DeferredSizePtr == null) ? 0 : m_BufferList->TotalBytes - m_DeferredSizeStart;

        // Note - DON'T USE DIRECTLY
        // Please construct by way of:
        //   messageBuilder = MessageStreamManager.CreateStreamBuilder()
        internal unsafe MessageStreamBuilder(MessageStream* buffer)
        {
            m_BufferList = buffer;
            m_BufferList->Active = 1;
            m_BufferListSwap = m_BufferList;
            m_DeferredSizePtr = null;
            m_DeferredSizeStart = 0;
            m_ResubmitStream = false;
        }

        public unsafe void WriteRaw(byte* data, int dataBytes)
        {
            if (dataBytes == 0)
                return;
            if (data == null)
                throw new ArgumentNullException("'data' is null in WriteRaw");
            m_BufferList->Allocate(dataBytes);
            UnsafeUtility.MemCpy((void*)(m_BufferList->BufferWrite->Buffer + m_BufferList->BufferWrite->Size), data, dataBytes);
            m_BufferList->UpdateSize(dataBytes);
        }

        public unsafe void WriteData<T>(T data) where T : unmanaged
        {
            WriteRaw((byte*)&data, sizeof(T));
        }

        public unsafe void MessageBegin(UnityGuid messageId)
        {
            if (m_DeferredSizePtr != null)
                throw new InvalidOperationException("Can't defer player connection message header - previous one not patched");

            // By making bufferListSwap NOT equivalent to bufferList, we signify "locking" the bufferList.
            fixed (MessageStream** bufferListSwapPtr = &m_BufferListSwap)
            {
                PlayerConnectionMt_AtomicStore((IntPtr)bufferListSwapPtr, IntPtr.Zero);
            }

            WriteData(EditorMessageIds.kMagicNumber);
            WriteData(messageId);
            m_DeferredSizePtr = (int*)(m_BufferList->BufferWrite->Buffer + m_BufferList->BufferWrite->Size);
            WriteData<int>(0);
            // In case the buffer was exactly full, the 32 bit size may have ended up in a new memory buffer
            // which didn't exist prior to reserving it
            if (m_BufferList->BufferWrite->Size == 4)
                m_DeferredSizePtr = (int*)(m_BufferList->BufferWrite->Buffer);
            m_DeferredSizeStart = m_BufferList->TotalBytes;
        }

        public unsafe void MessageEnd()
        {
            if (m_DeferredSizePtr == null)
                throw new InvalidOperationException("Can't patch player connection message header - nothing to patch");

            while ((m_BufferList->TotalBytes & 3) != 0)
                WriteData((byte)0);

            *m_DeferredSizePtr = m_BufferList->TotalBytes - m_DeferredSizeStart;
            m_DeferredSizeStart = 0;
            m_DeferredSizePtr = null;

            // By making bufferListSwap equivalent to bufferList, we signify "unlocking" the bufferList
            // so it can be submitted to the main thread for sending over player connection.
            // Also, bufferListSwap is guaranteed to be null right now
            fixed (MessageStream** bufferListSwapPtr = &m_BufferListSwap)
            {
                PlayerConnectionMt_AtomicStore((IntPtr)bufferListSwapPtr, (IntPtr)m_BufferList);
            }

            if (m_ResubmitStream)
            {
                TrySubmitStream(false);
                // This should always succeed...
            }
        }

        public unsafe void WriteMessage(UnityGuid messageId, byte* d, int dataBytes)
        {
            MessageBegin(messageId);
            WriteRaw(d, dataBytes);
            MessageEnd();
        }

        // This can be called from potentially two threads.
        // 1 - Main
        // 2 - Thread owning this instance
        public unsafe void TrySubmitStream(bool fromMainThread)
        {
            if (m_ResubmitStream == fromMainThread)
                return;
            if (m_BufferList->Active == 0 || m_BufferList->TotalBytes == 0)
                return;

            MessageStream* freeStream = MessageStreamManager.SubmitGetFreeStream();
            MessageStream* sendStream = m_BufferList;

            // If we called MessageBegin() without calling MessageEnd() yet, bufferListSwap will be null.
            // Otherwise, bufferList == bufferListSwap.
            //
            // CAS loop for bufferListSwap "compare not null and exchange" not necessary.
            // Just compare if bufferList == bufferListSwap and if so set bufferlist.
            fixed (MessageStream** bufferListPtr = &m_BufferList)
            {
                m_ResubmitStream = PlayerConnectionMt_AtomicCompareExchange((IntPtr)bufferListPtr, (IntPtr)m_BufferListSwap, (IntPtr)freeStream) == 0;
            }

            // Do note that we this method will never submit the send buffer again until another iteration of MessageBegin() and MessageEnd()
            // which will set bufferListSwap to bufferList. If using a MessageStreamBuilder without actual messages for some reason,
            // the behavior is undefined (it just won't work - it will always be deferred)

            if (m_ResubmitStream)
                MessageStreamManager.SubmitReturnFreeStream(freeStream);
            else
            {
                sendStream->Active = 0;
                MessageStreamManager.SubmitSendStream(sendStream);
                m_BufferList->Active = 1;
            }
        }
    }

    // THREAD SAFE
    // BURSTABLE
    //
    // Owns all MessageStreamBuilders.
    // Handles multithreaded synchronization, holding buffers for sending over player connection on main thread,
    // and maintaining/allocating replacement buffers for uninterrupted data flow while the previous set are
    // waiting on the async tcp sends to full process the data.
    [StructLayout(LayoutKind.Sequential)]
    internal struct MessageStreamManager
    {
        [DllImport("lib_unity_zerojobs")]
        private static extern void PlayerConnectionMt_Init();

        [DllImport("lib_unity_zerojobs")]
        private static extern void PlayerConnectionMt_Shutdown();

        [DllImport("lib_unity_zerojobs")]
        private static extern unsafe void PlayerConnectionMt_QueueFreeStream(IntPtr messageStream);

        [DllImport("lib_unity_zerojobs")]
        private static extern unsafe MessageStream* PlayerConnectionMt_DequeFreeStream();

        [DllImport("lib_unity_zerojobs")]
        internal static extern unsafe void PlayerConnectionMt_QueueSendStream(IntPtr messageStream);

        [DllImport("lib_unity_zerojobs")]
        internal static extern unsafe MessageStream* PlayerConnectionMt_DequeSendStream();

        [DllImport("lib_unity_zerojobs")]
        private static extern int PlayerConnectionMt_IsAvailableSendStream();

        [DllImport("lib_unity_zerojobs")]
        private static extern unsafe MessageStreamBuilder** PlayerConnectionMt_LockStreamBuilders();

        [DllImport("lib_unity_zerojobs")]
        private static extern void PlayerConnectionMt_UnlockStreamBuilders();

        [DllImport("lib_unity_zerojobs")]
        private static extern unsafe void PlayerConnectionMt_RegisterStreamBuilder(IntPtr messageStreamBuilder);

        [DllImport("lib_unity_zerojobs")]
        private static extern unsafe void PlayerConnectionMt_UnregisterStreamBuilder(IntPtr messageStreamBuilder);

        public unsafe static bool HasDataToSend => PlayerConnectionMt_IsAvailableSendStream() == 1;
        private const int k_InitialCapacity = 8192;

        //---------------------------------------------------------------------------------------------------
        // Lifetime
        //---------------------------------------------------------------------------------------------------
        public unsafe static void Initialize()
        {
            PlayerConnectionMt_Init();
        }

        public unsafe static void Shutdown()
        {
            // Free all stream builders
            MessageStreamBuilder** allBuilders = PlayerConnectionMt_LockStreamBuilders();
            if (allBuilders != null)
            {
                while (*allBuilders != null)
                {
                    (*allBuilders)->m_BufferList->Free();
                    UnsafeUtility.Free((*allBuilders)->m_BufferList, Collections.Allocator.Persistent);
                    UnsafeUtility.Free(*allBuilders, Collections.Allocator.Persistent);
                    allBuilders++;
                }
            }
            PlayerConnectionMt_UnlockStreamBuilders();

            PlayerConnectionMt_Shutdown();
        }

        public unsafe static MessageStreamBuilder* CreateStreamBuilder()
        {
            MessageStreamBuilder* buffer = (MessageStreamBuilder*)UnsafeUtility.Malloc(sizeof(MessageStreamBuilder), 0, Collections.Allocator.Persistent);
            PlayerConnectionMt_RegisterStreamBuilder((IntPtr)buffer);
            *buffer = new MessageStreamBuilder(SubmitGetFreeStream());
            return buffer;
        }

        public unsafe static void DestroyStreamBuilder(MessageStreamBuilder* buffer)
        {
            UnsafeUtility.Free(buffer, Collections.Allocator.Persistent);
            PlayerConnectionMt_UnregisterStreamBuilder((IntPtr)buffer);
        }


        //---------------------------------------------------------------------------------------------------
        // Send Queue
        //---------------------------------------------------------------------------------------------------
        public unsafe static void TrySubmitAll()
        {
            MessageStreamBuilder** allBuilders = PlayerConnectionMt_LockStreamBuilders();
            while (*allBuilders != null)
            {
                (*allBuilders)->TrySubmitStream(true);
                allBuilders++;
            }
            PlayerConnectionMt_UnlockStreamBuilders();
        }

        public unsafe static MessageStream* SubmitGetFreeStream()
        {
            MessageStream* stream = PlayerConnectionMt_DequeFreeStream();
            if (stream != null)
                return stream;
            
            stream = (MessageStream*)UnsafeUtility.Malloc(sizeof(MessageStream), 0, Collections.Allocator.Persistent);
            *stream = new MessageStream(k_InitialCapacity);
            return stream;
        }

        public unsafe static void SubmitReturnFreeStream(MessageStream* stream)
        {
            PlayerConnectionMt_QueueFreeStream((IntPtr)stream);
        }

        public unsafe static void SubmitSendStream(MessageStream* stream)
        {
            PlayerConnectionMt_QueueSendStream((IntPtr)stream);
        }

        public unsafe static void RecycleStream(MessageStream *stream)
        {
            stream->RecycleStream();
            PlayerConnectionMt_QueueFreeStream((IntPtr)stream);
        }

        public unsafe static void RecycleAll()
        {
            // Instead of queuing for send, we just erase what's there
            while (HasDataToSend)
            {
                MessageStream* stream = PlayerConnectionMt_DequeSendStream();
                RecycleStream(stream);
            }
        }
    }
}

#endif
