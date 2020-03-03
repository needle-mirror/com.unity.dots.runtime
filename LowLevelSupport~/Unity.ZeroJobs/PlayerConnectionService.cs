#if DEBUG && !UNITY_WEBGL

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using static Unity.Baselib.LowLevel.Binding;

namespace Unity.Development
{
    internal enum PlayerConnectionPort : ushort
    {
        // Game initiates connection to Unity
        // - Good for localhost
        // - Good for external Unity host's IP if known
        DirectConnect = 34999,

        // Manually initiated from Unity
        // (Unity attempts a range of 512 ports when user manually initiates connection)
        // - Good for custom IPs such as mobile or web
        DirectListenFirst = 55000,
        DirectListenLast = 55511,

        // CURRENTLY UNSUPPORTED
        // Automatically initiated from Unity
        // (Must respond first with a specifically formatted message to verify we are a valid target for connection)
        // - Good for localhost, REQUIRED for locally served web builds
        //MulticastListen = 54997,
    }

    // Unity Guid is in byte order of string version with nibbles swapped
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct UnityGuid
    {
        private fixed byte data[16];

        public UnityGuid(Guid guid) {
            fixed (byte* s = guid.ToByteArray())
            {
                fixed (byte* d = data)
                {
                    Convert(d, s);
                }
            }
        }
        public static implicit operator UnityGuid(Guid guid) { return new UnityGuid(guid); }
        public Guid ToGuid()
        {
            byte[] dest = new byte[16];
            fixed (byte* s = data)
            {
                fixed (byte* d = dest)
                {
                    Convert(d, s);
                }
            }
            return new Guid(dest);
        }

        unsafe public UnityGuid(string guidString)
        {
            byte[] k_LiteralToHex =
            {
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff
            };

            // Convert every hex char into an int [0...16]
            var hex = stackalloc byte[32];
            for (int i = 0; i < 32; i++)
            {
                int intValue = guidString[i];
                if (intValue < 0 || intValue > 255)
                    return;

                hex[i] = k_LiteralToHex[intValue];
            }

            for (int i = 0; i < 16; i++)
            {
                data[i] = (byte)(hex[i * 2] | (hex[i * 2 + 1] << 4));
            }
        }

        public static bool operator ==(UnityGuid a, UnityGuid b)
        {
            for (int i = 0; i < 16; i++)
            {
                if (a.data[i] != b.data[i])
                    return false;
            }
            return true;
        }

        public static bool operator !=(UnityGuid a, UnityGuid b)
        {
            return !(a == b);
        }

        private void Convert(byte* dest, byte* src)
        {
            int[] k_Swap = { 3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15 };
            for (int i = 0; i < 16; i++)
            {
                byte b = src[k_Swap[i]];
                dest[i] = (byte)((b >> 4) | (b << 4));
            }
        }

        public override bool Equals(object obj)
        {
            if (obj is UnityGuid guid)
                return this == guid;
            throw new NotImplementedException();
        }

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }
    }

    internal class BufferNode
    {
        public BufferNode Next { get; set; }
        public int Size { get; set; }
        public IntPtr Buffer { get; private set; } = IntPtr.Zero;
        public int Capacity { get; private set; } = 0;

        public void Alloc(int bytes)
        {
            Free();

            unsafe
            {
                Buffer = (IntPtr)UnsafeUtility.Malloc(bytes, 0, Unity.Collections.Allocator.Persistent);
            }

            Capacity = bytes;
            Size = 0;
            Next = null;
        }

        public void Free()
        {
            if (Buffer == IntPtr.Zero)
                return;

            unsafe
            {
                UnsafeUtility.Free((void*)Buffer, Unity.Collections.Allocator.Persistent);
            }

            Buffer = IntPtr.Zero;
            Capacity = 0;
            Size = 0;
            Next = null;
        }
    }

    internal class BufferList
    {
        internal readonly int reserve = 0;

        public BufferNode BufferWrite { get; private set; }  // always tail node (may also be head)
        public BufferNode BufferRead { get; private set; }  // always head node
        public int TotalBytes { get; private set; }

        private void FreeRange(BufferNode beginNode, BufferNode endNode)
        {
            var node = beginNode;
            while (node != endNode)
            {
                var next = node.Next;
                TotalBytes -= node.Size;
                node.Free();
                node = next;
            }

            beginNode = null;
            if (endNode == null)
                BufferWrite = BufferRead;
            else
            {
                BufferWrite = endNode;
                while (BufferWrite.Next != null)
                    BufferWrite = BufferWrite.Next;
            }
        }

        public BufferList(int reserveSize)
        {
            reserve = reserveSize;
            BufferRead = new BufferNode();
            BufferRead.Alloc(reserve);
            BufferWrite = BufferRead;
            TotalBytes = 0;
        }

        ~BufferList()
        {
            FreeRange(BufferRead, null);
            BufferRead = null;
        }

        public void Flush(BufferNode flushUntil, int flushUntilOffset)
        {
            if (flushUntil != BufferRead)
            {
                FreeRange(BufferRead?.Next, flushUntil);
                TotalBytes -= BufferRead.Size;
                BufferRead.Size = 0;
                BufferRead.Next = flushUntil;
            }

            if (flushUntilOffset > 0)
            {
                if (flushUntil == null)
                    throw new ArgumentException("Flushing buffer past end");
                if (flushUntilOffset > flushUntil.Size)
                    throw new ArgumentOutOfRangeException("Flushing buffer with offset past end");

                unsafe
                {
                    UnsafeUtility.MemCpy((void*)flushUntil.Buffer, (void*)(flushUntil.Buffer + flushUntilOffset), flushUntil.Size - flushUntilOffset);
                }
                flushUntil.Size -= flushUntilOffset;
                TotalBytes -= flushUntilOffset;

                if (flushUntil.Size == 0)
                {
                    BufferNode newNext = flushUntil.Next;
                    FreeRange(BufferRead?.Next, newNext);
                    BufferRead.Next = newNext;
                }
            }
        }

        public void FlushAll()
        {
            FreeRange(BufferRead?.Next, null);
            BufferRead.Size = 0;
            BufferRead.Next = null;
            TotalBytes = 0;
        }

        public unsafe void AllocAndCopy(void* src, int bytes)
        {
            if (bytes == 0)
                return;
            if (src == null)
                throw new ArgumentNullException("src is null in AllocAndCopy");
            Alloc(bytes);
            UnsafeUtility.MemCpy((void*)(BufferWrite.Buffer + BufferWrite.Size), src, bytes);
            IncSize(bytes);
        }

        public void Alloc(int bytes)
        {
            if (BufferWrite.Size + bytes > BufferWrite.Capacity)
            {
                BufferWrite.Next = new BufferNode();
                BufferWrite = BufferWrite.Next;
                BufferWrite.Alloc(bytes < reserve ? reserve : bytes);
            }
        }

        public void IncSize(int bytes)
        {
            BufferWrite.Size += bytes;
            TotalBytes += bytes;
        }

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
                    BufferNode bufferReadNode = BufferRead;
                    int readOffset = offsetBegin;
                    int writeOffset = 0;

                    while (bytesLeft > 0)
                    {
                        while (readOffset >= bufferReadNode.Size)
                        {
                            readOffset -= bufferReadNode.Size;
                            bufferReadNode = bufferReadNode.Next;
                        }

                        int copyBytes = bufferReadNode.Size - readOffset;
                        if (bytesLeft < copyBytes)
                            copyBytes = bytesLeft;

                        UnsafeUtility.MemCpy(m + writeOffset, (void*)(bufferReadNode.Buffer + readOffset), copyBytes);

                        readOffset += copyBytes;
                        writeOffset += copyBytes;
                        bytesLeft -= copyBytes;
                    }
                }
            }

            return data;
        }
    }

    // Unused messages are commented out while we sort out what is relevant
    internal static class PlayerMessageId
    {
        public static readonly uint kMagicNumber = 0x67A54E8F;

        public static readonly UnityGuid kLog = new UnityGuid("394ada038ba04f26b0011a6cdeb05a62");
        //public static readonly UnityGuid kCleanLog = new UnityGuid("3ded2ddacdf246d8a3f601741741e7a9");
        
        //public static readonly UnityGuid kFileTransfer = new UnityGuid("c2a22f5d7091478ab4d6c163a7573c35");
        //public static readonly UnityGuid kFrameDebuggerEditorToPlayer = new UnityGuid("035c0cae2e03494894aabe3955d4bf43");
        //public static readonly UnityGuid kFrameDebuggerPlayerToEditor = new UnityGuid("8f448ceb744d42ba80a854f56e43b77e");
        
        public static readonly UnityGuid kPingAlive = new UnityGuid("fe9b18127f6045c68db230d993d2a210");  // Won't get a response, but will fail if no connection made (since connection is async)
        public static readonly UnityGuid kApplicationQuit = new UnityGuid("38a5d246506546dfaedb6653f6e22b33");   // notify editor we quit, or editor notifying when it quits
        //public static readonly UnityGuid kNoFurtherConnections = new UnityGuid("62ba6073d907426995e768e2c8a2b368");

        public static readonly UnityGuid kProfileStartupInformation = new UnityGuid("2257466d0e0e47da89826cf04e68135c");  // editor usually sends this as soon as we connect - 1
        //public static readonly UnityGuid kProfilerDataMessage = new UnityGuid("c58d77184f4b4b59b3fffc6f800ae10e");
        public static readonly UnityGuid kProfilerSetMemoryRecordMode = new UnityGuid("c48d097f8fea463494b8f08b0b55d05a");  // editor usually sends this as soon as we connect - 3
        public static readonly UnityGuid kProfilerSetAutoInstrumentedAssemblies = new UnityGuid("6cfdfe5ac10d4b79bfe27e8abe06915f");  // editor usually sends this as soon as we connect - 2
        //public static readonly UnityGuid kProfilerSetAudioCaptureFlags = new UnityGuid("1e792ecb5c9f4a8381d0d03528b6ae7b");
        //public static readonly UnityGuid kProfilerQueryInstrumentableFunctions = new UnityGuid("302b3998e168478eb8713b086c7693a9");
        //public static readonly UnityGuid kProfilerQueryFunctionCallees = new UnityGuid("d8f38a5539cc4b608792c273efe6a969");
        //public static readonly UnityGuid kProfilerFunctionsDataMessage = new UnityGuid("e2acb618e8c8465a901eb7b6f667cc41");
        //public static readonly UnityGuid kProfilerBeginInstrumentFunction = new UnityGuid("027723bb8a12495aa4803c27d10c86b8");
        //public static readonly UnityGuid kProfilerEndInstrumentFunction = new UnityGuid("1db84608522147b8bc57e34cd4d036b1");
        //public static readonly UnityGuid kObjectMemoryProfileSnapshot = new UnityGuid("14473694eb0a4963870aaab63efb7507");
        //public static readonly UnityGuid kObjectMemoryProfileDataMessage = new UnityGuid("8584ee18ea264718873cd92b109a0761");

        //public static readonly UnityGuid kProfilerPlayerInfoMessage = new UnityGuid("94d3ae57ca4b4ed98408b827f180853d");
        //public static readonly UnityGuid kProfilerSetDeepProfilerModeMessage = new UnityGuid("bf0c550cd24d498cbb28380a8467622d");
        //public static readonly UnityGuid kProfilerSetMarkerFiltering = new UnityGuid("18207525e148469ea059ec2cdfb026a5");
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PlayerMessageHeader {
        public uint magicId;
        public UnityGuid messageId;
        public int bytes;
    }

    internal class MessageEvent
    {
        public UnityGuid messageId;
        public event UnityEngine.Events.UnityAction<UnityEngine.Networking.PlayerConnection.MessageEventArgs> callbacks;

        public void Invoke(UnityEngine.Networking.PlayerConnection.MessageEventArgs args)
        {
            callbacks.Invoke(args);
        }

        public void Invoke(int playerId, byte[] data)
        {
            callbacks.Invoke(new UnityEngine.Networking.PlayerConnection.MessageEventArgs { playerId = playerId, data = data });
        }
    }

    public static class PlayerConnectionService
    {
        private enum ConnectionState {
            Init,
            Connect,
            ListenBind,
            ListenListen,
            ListenAccept,

            Ready,
            Invalid,
        }

        private static Baselib_Socket_Handle hSocket = Baselib_Socket_Handle_Invalid;
        private static Baselib_Socket_Handle hSocketListen = Baselib_Socket_Handle_Invalid;
        private static Baselib_NetworkAddress hAddress;
        private static Baselib_ErrorState errState;

        private static List<MessageEvent> m_EventMessageList = new List<MessageEvent>();
        private static ConnectionState state = ConnectionState.Init;

#if DEBUG && (UNITY_WINDOWS || UNITY_LINUX || UNITY_MACOSX)
        private static ConnectionState initType = ConnectionState.Connect;
        private static string initIp = "127.0.0.1";  // default connect to local host
        private static ushort initPort = (ushort)PlayerConnectionPort.DirectConnect;
#else
        private static ConnectionState initType = ConnectionState.ListenBind;
        private static string initIp = "0.0.0.0";  // default listen on all ip address
        private static ushort initPort = (ushort)PlayerConnectionPort.DirectListenFirst;
#endif
        private static int initRetryCounter = 0;

        private static BufferList bufferReceive = new BufferList(kReserveCapacity);
        private static BufferList bufferSend = new BufferList(kReserveCapacity);

        public const int kReserveCapacity = 8192;
        public const int kInitRetryCounter = 30;

        public static bool Initialized => state == ConnectionState.Ready || state == ConnectionState.Invalid;
        public static bool Connected => state == ConnectionState.Ready;
        public static bool Listening => state == ConnectionState.ListenAccept;

        public static bool HasDataToSend => bufferSend.TotalBytes > 0;
        public static int DataToSendBytes => bufferSend.TotalBytes;

#if UNITY_WINDOWS
        // WSAStartup and WSACleanup is needed for windows support currently. This will be removed
        // once socket subsystem startup/shutdown functionality is properly abstracted in platforms library.
        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct WSAData
        {
            public Int16 wVersion;
            public Int16 wHighVersion;
            public fixed byte szDescription[257];
            public fixed byte szSystemStatus[129];
            public Int16 iMaxSockets;
            public Int16 iMaxUdpDg;
            public IntPtr lpVendorInfo;
        }

        [DllImport("ws2_32.dll", CharSet = CharSet.Ansi)]
        static extern private Int32 WSAStartup(Int16 wVersionRequested, out WSAData wsaData);

        [DllImport("ws2_32.dll", CharSet = CharSet.Ansi)]
        static extern private Int32 WSACleanup();

        public static void PlatformInit()
        {
            WSAStartup(0x202, out WSAData data);
        }

        public static void PlatformShutdown()
        {
            WSACleanup();
        }
#else
        public static void PlatformInit()
        {
        }

        public static void PlatformShutdown()
        {
        }
#endif

        public static void InitializeConnect(string forceIp, ushort forcePort)
        {
            initIp = forceIp;
            initPort = forcePort;
            initType = ConnectionState.Connect;
            Initialize();
        }

        public static void InitializeListen(string forceIp, ushort forcePort)
        {
            initIp = forceIp;
            initPort = forcePort;
            initType = ConnectionState.ListenBind;
            Initialize();
        }

        public static void Initialize()
        {
            if (Initialized)
                return;

            if (state == ConnectionState.Init)
            {
                if (initRetryCounter > 0)
                {
                    initRetryCounter--;
                    return;
                }

                PlatformInit();

                unsafe
                {
                    hSocket = Baselib_Socket_Create(Baselib_NetworkAddress_Family.IPv4, Baselib_Socket_Protocol.TCP, (Baselib_ErrorState *)UnsafeUtility.AddressOf(ref errState));
                    if (errState.code == Baselib_ErrorCode.Success)
                    {
                        fixed (byte* bip = System.Text.Encoding.UTF8.GetBytes(initIp))
                        {
                            Baselib_NetworkAddress_Encode((Baselib_NetworkAddress*)UnsafeUtility.AddressOf(ref hAddress), Baselib_NetworkAddress_Family.IPv4, 
                                bip, initPort, (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));
                        }
                    }
                }

                if (errState.code == Baselib_ErrorCode.Success)
                    state = initType;
                else
                    state = ConnectionState.Invalid;
            }

            if (state == ConnectionState.Connect)
            {
                unsafe
                {
                    Baselib_Socket_TCP_Connect(hSocket, (Baselib_NetworkAddress*)UnsafeUtility.AddressOf(ref hAddress), Baselib_NetworkAddress_AddressReuse.Allow,
                        (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));
                }

                if (errState.code == Baselib_ErrorCode.Success)
                    state = ConnectionState.Ready;
                else
                    state = ConnectionState.Invalid;
            }
            else
            {
                if (state == ConnectionState.ListenBind)
                {
                    unsafe
                    {
                        Baselib_Socket_Bind(hSocket, (Baselib_NetworkAddress*)UnsafeUtility.AddressOf(ref hAddress), Baselib_NetworkAddress_AddressReuse.Allow,
                            (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));
                    }

                    if (errState.code == Baselib_ErrorCode.Success)
                        state = ConnectionState.ListenListen;
                    else
                        state = ConnectionState.Invalid;
                }

                if (state == ConnectionState.ListenListen)
                {
                    unsafe
                    {
                        Baselib_Socket_TCP_Listen(hSocket, (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));
                    }

                    if (errState.code == Baselib_ErrorCode.Success)
                        state = ConnectionState.ListenAccept;
                    else
                        state = ConnectionState.Invalid;
                }

                if (state == ConnectionState.ListenAccept)
                {
                    unsafe
                    {
                        hSocketListen = Baselib_Socket_TCP_Accept(hSocket, (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));
                    }

                    if (errState.code != Baselib_ErrorCode.Success)
                        state = ConnectionState.Invalid;
                    else if (hSocketListen.handle != Baselib_Socket_Handle_Invalid.handle)
                    {
                        // Swap so rx/tx code works on same path
                        var hSocketTemp = hSocket;
                        hSocket = hSocketListen;
                        hSocketListen = hSocketTemp;
                        state = ConnectionState.Ready;
                    }
                }
            }

            if (state == ConnectionState.Invalid)
            {
                if (hSocket.handle != Baselib_Socket_Handle_Invalid.handle)
                {
                    Baselib_Socket_Close(hSocket);
                    hSocket = Baselib_Socket_Handle_Invalid;
                }
            }
        }

        public static void CleanUp()
        {
            initRetryCounter = 0;

            if (state == ConnectionState.Init)
                return;

            if (hSocketListen.handle != Baselib_Socket_Handle_Invalid.handle)
            {
                Baselib_Socket_Close(hSocketListen);
                hSocketListen = Baselib_Socket_Handle_Invalid;
            }

            if (hSocket.handle != Baselib_Socket_Handle_Invalid.handle)
            {
                Baselib_Socket_Close(hSocket);
                hSocket = Baselib_Socket_Handle_Invalid;
            }

            PlatformShutdown();

            state = ConnectionState.Init;
            errState.code = Baselib_ErrorCode.Success;

            bufferSend.FlushAll();
            bufferReceive.FlushAll();
        }

        public static unsafe void SendRaw(byte *data, int dataBytes)
        {
            // Buffers the data even if we don't have a connection yet
            if (state != ConnectionState.Invalid)
                bufferSend.AllocAndCopy(data, dataBytes);
        }

        public static unsafe void SendMessage(UnityGuid messageId, byte *d, int dataBytes)
        {
            fixed (uint* b = &PlayerMessageId.kMagicNumber)
            {
                SendRaw((byte*)b, 4);
            }
            SendRaw((byte*)(&messageId), sizeof(UnityGuid));
            SendRaw((byte*)(&dataBytes), 4);
            SendRaw(d, dataBytes);
        }

        public static void TransmitAndReceive()
        {
            Initialize();

            if (!Connected)
                return;

            Baselib_Socket_PollFd pollFd = new Baselib_Socket_PollFd();
            unsafe
            {
                pollFd.handle.handle = hSocket.handle;
                pollFd.errorState = (Baselib_ErrorState*) UnsafeUtility.AddressOf(ref errState);
                pollFd.requestedEvents = Baselib_Socket_PollEvents.Connected;

                Baselib_Socket_Poll(&pollFd, 1, 0, (Baselib_ErrorState*) UnsafeUtility.AddressOf(ref errState));
            };
            
            if (errState.code != Baselib_ErrorCode.Success)
            {
                CleanUp();
                return;
            }

            if (pollFd.resultEvents != Baselib_Socket_PollEvents.Connected)
                return;

            Receive();

            if (!Connected)
            {
                if (state == ConnectionState.Invalid)
                    bufferSend.FlushAll();
                return;
            }
            
            Transmit();
        }

        private static unsafe void Receive()
        {
            // Receive anything sent to us
            // Similar setup for sending data
            PlayerMessageHeader* header = (PlayerMessageHeader*)bufferReceive.BufferRead.Buffer;

            int bytesNeeded = 0;
            if (bufferReceive.TotalBytes < sizeof(PlayerMessageHeader))
                bytesNeeded = sizeof(PlayerMessageHeader) - bufferReceive.TotalBytes;
            else
                bytesNeeded = sizeof(PlayerMessageHeader) + header->bytes - bufferReceive.TotalBytes;

            while (bytesNeeded > 0)
            {
                BufferNode bufferWrite = bufferReceive.BufferWrite;

                int bytesAvail = bufferWrite.Capacity - bufferWrite.Size;
                uint actualWritten = Baselib_Socket_TCP_Recv(hSocket, bufferWrite.Buffer + bufferWrite.Size,
                    (uint)(bytesNeeded <= bytesAvail ? bytesNeeded : bytesAvail), (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));

                if (errState.code != Baselib_ErrorCode.Success)
                {
                    // Something bad happened; lost connection maybe?
                    // After cleaning up, next time we will try to re-initialize
                    CleanUp();
                    initRetryCounter = kInitRetryCounter;
                    return;
                }

                if (bytesNeeded > 0 && actualWritten == 0)
                {
                    // Don't do anything with data until we've received everything
                    return;
                }

                bufferReceive.IncSize((int)actualWritten);
                bytesNeeded -= (int)actualWritten;
                if (bytesNeeded == 0)
                {
                    // Finished receiving header
                    if (bufferReceive.TotalBytes == sizeof(PlayerMessageHeader))
                    {
                        // De-synced somewhere... reset connection
                        if (header->magicId != PlayerMessageId.kMagicNumber)
                        {
                            CleanUp();
                            initRetryCounter = kInitRetryCounter;
                            return;
                        }
                        bytesNeeded = header->bytes;
                        bufferReceive.Alloc(bytesNeeded);
                    }

                    // Finished receiving message
                    if (bytesNeeded == 0)
                    {
                        // Otherwise bytesNeeded becomes 0 after message is finished, which can be immediately in the
                        // case of PlayerMessageId.kApplicationQuit (message size 0)
                        foreach (var e in m_EventMessageList)
                        {
                            if (e.messageId == header->messageId)
                            {
                                // This could be anything from a 4-byte "bool" to an asset sent from the editor
                                byte[] messageData = bufferReceive.ToByteArray(sizeof(PlayerMessageHeader), bufferReceive.TotalBytes);
                                e.Invoke(0, messageData);
                            }
                        }

                        if (header->messageId == PlayerMessageId.kApplicationQuit)
                        {
                            UnityEngine.Debug.Log("Unity editor has been closed");
                        }

                        // Poll for next message
                        bufferReceive.FlushAll();
                        bytesNeeded = sizeof(PlayerMessageHeader);
                    }
                }
            }

            // This code should not be executable
            throw new InvalidOperationException("Internal error receiving network data");
        }

        private static unsafe void Transmit()
        {
            // Transmit anything in buffers
            BufferNode bufferRead = bufferSend.BufferRead;
            int offset = 0;

            while (bufferRead != null)
            {
                uint actualRead = Baselib_Socket_TCP_Send(hSocket, bufferRead.Buffer + offset, (uint)(bufferRead.Size - offset), (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));
                if (errState.code != Baselib_ErrorCode.Success)
                {
                    // Something bad happened; lost connection maybe?
                    // After cleaning up, next time we will try to re-initialize
                    CleanUp();
                    initRetryCounter = kInitRetryCounter;
                    return;
                }

                if (actualRead == 0)
                {
                    bufferSend.Flush(bufferRead, offset);
                    return;
                }

                offset += (int)actualRead;
                if (offset == bufferRead.Size)
                {
                    bufferRead = bufferRead.Next;
                    offset = 0;
                }
            }

            bufferSend.FlushAll();
        }

        public static void RegisterMessage(UnityGuid messageId, UnityEngine.Events.UnityAction<UnityEngine.Networking.PlayerConnection.MessageEventArgs> callback)
        {
            GetMessageEvent(messageId).callbacks += callback;
        }

        public static void UnregisterMessage(UnityGuid messageId, UnityEngine.Events.UnityAction<UnityEngine.Networking.PlayerConnection.MessageEventArgs> callback)
        {
            GetMessageEvent(messageId).callbacks -= callback;
        }

        public static void UnregisterAllMessages()
        {
            m_EventMessageList.Clear();
        }

        private static MessageEvent GetMessageEvent(UnityGuid messageId)
        {
            foreach (var e in m_EventMessageList)
            {
                if (e.messageId == messageId)
                    return e;
            }

            var ret = new MessageEvent { messageId = messageId };
            m_EventMessageList.Add(ret);

            return ret;
        }
    }
}

#endif
