using System;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Tiny.Tests.Common;
using Unity.Development.PlayerConnection;
using UnityEngine;
using static Unity.Baselib.LowLevel.Binding;
using UnityEngine.Networking.PlayerConnection;
#if ENABLE_PROFILER
using Unity.Development.Profiling;
#endif
using Unity.Collections;
using Unity.Core;

// Tests for
// - basic functionality (connect, listen, accept, send, receive, disconnect)
// - fault tolerance
// - connection persistenence and re-establishing

namespace Unity.ZeroJobs.Tests
{
    public class PlayerConnectionTestFixture
    {
        //---------------------------------------------------------------------------------------------------
        // GLOBAL HELPERS
        //---------------------------------------------------------------------------------------------------

        internal const string spoofIp = "127.0.0.1";
        internal const ushort spoofPort = 1050;
        internal readonly UnityGuid spoofMessage = new UnityGuid("abcd0123fdec4567afbd1094deca6574");
        internal bool spoofInitCalled = false;
        internal Baselib_Socket_Handle hSpoofSocket = Baselib_Socket_Handle_Invalid;
        internal Baselib_Socket_Handle hSpoofSocketConnected = Baselib_Socket_Handle_Invalid;
        internal Baselib_NetworkAddress hSpoofAddress;
        internal bool SpoofConnected => hSpoofSocketConnected.handle != Baselib_Socket_Handle_Invalid.handle;
        internal bool SpoofConnectedAsClient => hSpoofSocket.handle != Baselib_Socket_Handle_Invalid.handle;

        // Retry for connection in player connection occurs every x number of frames (i.e. TransmitAndReceive() calls)
        internal const float kConnectTime = 4;

        internal unsafe MessageStreamBuilder* messageBuilder;

        internal unsafe void SpoofInitListenInternal()
        {
            spoofInitCalled = true;
            Baselib_ErrorState errState = new Baselib_ErrorState();

            hSpoofSocket = Baselib_Socket_Create(Baselib_NetworkAddress_Family.IPv4, Baselib_Socket_Protocol.TCP, (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));

            if (errState.code != Baselib_ErrorCode.Success)
            {
                SpoofExitInternal();
                Assert.AreEqual(Baselib_ErrorCode.Success, errState.code, "Unable to create socket when initializing spoof player listener server");
            }

            if (errState.code == Baselib_ErrorCode.Success)
            {
                fixed (byte* bip = System.Text.Encoding.UTF8.GetBytes(spoofIp))
                {
                    Baselib_NetworkAddress_Encode((Baselib_NetworkAddress*)UnsafeUtility.AddressOf(ref hSpoofAddress), Baselib_NetworkAddress_Family.IPv4,
                        bip, spoofPort, (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));
                }
            }

            if (errState.code != Baselib_ErrorCode.Success)
            {
                SpoofExitInternal();
                Assert.AreEqual(Baselib_ErrorCode.Success, errState.code, "Unable to encode network address when initializing spoof player listener server");
            }

            if (errState.code == Baselib_ErrorCode.Success)
            {
                Baselib_Socket_Bind(hSpoofSocket, (Baselib_NetworkAddress*)UnsafeUtility.AddressOf(ref hSpoofAddress), Baselib_NetworkAddress_AddressReuse.Allow,
                    (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));
            }

            if (errState.code != Baselib_ErrorCode.Success)
            {
                SpoofExitInternal();
                Assert.AreEqual(Baselib_ErrorCode.Success, errState.code, "Unable to bind socket when initializing spoof player listener server");
            }

            if (errState.code == Baselib_ErrorCode.Success)
            {
                Baselib_Socket_TCP_Listen(hSpoofSocket, (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));
            }

            if (errState.code != Baselib_ErrorCode.Success)
            {
                SpoofExitInternal();
                Assert.AreEqual(Baselib_ErrorCode.Success, errState.code, "Unable to initiate listen mode when initializing spoof player listener server");
            }
        }

        internal unsafe void SpoofInitConnectInternal()
        {
            spoofInitCalled = true;
            Baselib_ErrorState errState = new Baselib_ErrorState();

            hSpoofSocket = Baselib_Socket_Create(Baselib_NetworkAddress_Family.IPv4, Baselib_Socket_Protocol.TCP, (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));

            if (errState.code != Baselib_ErrorCode.Success)
            {
                SpoofExitInternal();
                Assert.AreEqual(Baselib_ErrorCode.Success, errState.code, "Unable to create socket when initializing spoof player connection server");
            }

            if (errState.code == Baselib_ErrorCode.Success)
            {
                fixed (byte* bip = System.Text.Encoding.UTF8.GetBytes(spoofIp))
                {
                    Baselib_NetworkAddress_Encode((Baselib_NetworkAddress*)UnsafeUtility.AddressOf(ref hSpoofAddress), Baselib_NetworkAddress_Family.IPv4,
                        bip, spoofPort, (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));
                }
            }

            if (errState.code != Baselib_ErrorCode.Success)
            {
                SpoofExitInternal();
                Assert.AreEqual(Baselib_ErrorCode.Success, errState.code, "Unable to encode network address when initializing spoof player connection server");
            }

            if (errState.code == Baselib_ErrorCode.Success)
            {
                Baselib_Socket_TCP_Connect(hSpoofSocket, (Baselib_NetworkAddress*)UnsafeUtility.AddressOf(ref hSpoofAddress), Baselib_NetworkAddress_AddressReuse.Allow,
                    (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));
            }

            if (errState.code != Baselib_ErrorCode.Success)
            {
                SpoofExitInternal();
                Assert.AreEqual(Baselib_ErrorCode.Success, errState.code, "Unable to connect socket when initializing spoof player connection server");
            }
        }

        internal unsafe void SpoofAcceptInternal()
        {
            Baselib_ErrorState errState = new Baselib_ErrorState();

            hSpoofSocketConnected = Baselib_Socket_Handle_Invalid;
            var start = Time.timeAsDouble;
            while (hSpoofSocketConnected.handle == Baselib_Socket_Handle_Invalid.handle && Time.timeAsDouble - start < kConnectTime)
                hSpoofSocketConnected = Baselib_Socket_TCP_Accept(hSpoofSocket, (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));
            Assert.AreEqual(Baselib_ErrorCode.Success, errState.code, "Error accepting spoof player connection");
            Assert.IsFalse(hSpoofSocketConnected.handle == Baselib_Socket_Handle_Invalid.handle, "Unable to accept spoof player connection");
        }

        internal unsafe void SpoofSendInternal(byte* data, int numBytes)
        {
            Baselib_ErrorState errState = new Baselib_ErrorState();
            uint bytesLeft = (uint)numBytes;
            uint bytesSent = 0;
            while (bytesLeft > 0)
            {
                uint sent = Baselib_Socket_TCP_Send(hSpoofSocketConnected, (IntPtr)data + (int)bytesSent, (uint)bytesLeft, (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));
                if (errState.code != Baselib_ErrorCode.Success)
                    Assert.AreEqual(Baselib_ErrorCode.Success, errState.code);
                bytesSent += sent;
                bytesLeft -= sent;
            }
        }

        internal unsafe void SpoofReceiveInternal(byte* data, int numBytes)
        {
            Baselib_ErrorState errState = new Baselib_ErrorState();
            uint bytesLeft = (uint)numBytes;
            uint bytesReceived = 0;
            while (bytesLeft > 0)
            {
                uint received = Baselib_Socket_TCP_Recv(hSpoofSocketConnected, (IntPtr)data + (int)bytesReceived, (uint)bytesLeft, (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref errState));
                Assert.AreEqual(Baselib_ErrorCode.Success, errState.code);
                bytesReceived += received;
                bytesLeft -= received;
            }
        }

        internal void SpoofExitInternal()
        {
            if (!spoofInitCalled)
                return;

            spoofInitCalled = false;

            if (hSpoofSocket.handle != Baselib_Socket_Handle_Invalid.handle)
            {
                Baselib_Socket_Close(hSpoofSocket);
                hSpoofSocket = Baselib_Socket_Handle_Invalid;
            }

            if (hSpoofSocketConnected.handle != Baselib_Socket_Handle_Invalid.handle)
            {
                Baselib_Socket_Close(hSpoofSocketConnected);
                hSpoofSocketConnected = Baselib_Socket_Handle_Invalid;
            }
        }

        internal unsafe void PingInternal()
        {
            // Connection commonly reports success when async, even if it hasn't actually been established
            // Send a message and TCP send should succeed
            messageBuilder->WriteMessage(EditorMessageIds.kPingAlive, null, 0);
            messageBuilder->TrySubmitStream(true);

            var start = Time.timeAsDouble;
            while (Connection.HasSendDataQueued && Time.timeAsDouble - start < kConnectTime)
                Connection.TransmitAndReceive();

            Assert.IsFalse(Connection.HasSendDataQueued, "Timed out! (Froze)");
        }

        internal unsafe void PingInternalTimeout()
        {
            // Connection commonly reports success when async, even if it hasn't actually been established
            // Send a message and TCP send should succeed
            unsafe
            {
                messageBuilder->WriteMessage(EditorMessageIds.kPingAlive, null, 0);
            }

            messageBuilder->TrySubmitStream(true);

            var start = Time.timeAsDouble;
            while (Connection.HasSendDataQueued && Time.timeAsDouble - start < kConnectTime)
                Connection.TransmitAndReceive();

            Assert.IsTrue(Connection.HasSendDataQueued, "Didn't timed out! (expected Froze)");
        }

        [OneTimeSetUp]
        public void TopLevelSetUp()
        {
#if ENABLE_PROFILER
            // The test runner has already initialized these so kill them and do them per test
            Profiler.Shutdown();
#endif
            Connection.Shutdown();
        }

        [SetUp]
        public void PerTestSetUp()
        {
            TempMemoryScope.EnterScope();
            Connection.Initialize();
#if ENABLE_PROFILER
            Profiler.Initialize();
#endif
            unsafe
            {
                messageBuilder = MessageStreamManager.CreateStreamBuilder();
            }
        }

        [TearDown]
        public void PerTestTearDown()
        {
            unsafe
            {
                MessageStreamManager.DestroyStreamBuilder(messageBuilder);
            }
#if ENABLE_PROFILER
            Profiler.Shutdown();
#endif
            Connection.Shutdown();
            SpoofExitInternal();
            TempMemoryScope.ExitScope();
        }

        [OneTimeTearDown]
        public void TopLevelShutdown()
        {
            // The test runner expects these to be initialized when it shuts down Dots Runtime static systems
            Connection.Initialize();
#if ENABLE_PROFILER
            Profiler.Initialize();
#endif
        }
    }


    //---------------------------------------------------------------------------------------------------
    // UNITY GUID TESTS
    //---------------------------------------------------------------------------------------------------
    public class UnityGuidTests : PlayerConnectionTestFixture
    {
        [Test]
        public void UnityGuidFromString()
        {
            UnityGuid guidlc = new UnityGuid("394ada038ba04f26b0011a6cdeb05a62");
            UnityGuid guiduc = new UnityGuid("394ADA038BA04F26B0011A6CDEB05A62");
            byte[] expected = new byte[] { 0x93, 0xa4, 0xad, 0x30, 0xb8, 0x0a, 0xf4,
                                           0x62, 0x0b, 0x10, 0xa1, 0xc6, 0xed, 0x0b, 0xa5, 0x26 };

            unsafe
            {
                Assert.AreEqual(16, sizeof(UnityGuid));

                byte* plc = (byte*)&guidlc;
                byte* puc = (byte*)&guiduc;
                for (int i = 0; i < 16; i++)
                {
                    Assert.AreEqual(plc[i], expected[i]);
                    Assert.AreEqual(puc[i], expected[i]);
                }
            }
        }

        [Test]
        public void UnityGuidFromGUID()
        {
            UnityGuid guid = new Guid("394ada038ba04f26b0011a6cdeb05a62");
            byte[] expected = new byte[] { 0x93, 0xa4, 0xad, 0x30, 0xb8, 0x0a, 0xf4,
                                           0x62, 0x0b, 0x10, 0xa1, 0xc6, 0xed, 0x0b, 0xa5, 0x26 };

            unsafe
            {
                byte* p = (byte*)&guid;
                for (int i = 0; i < 16; i++)
                {
                    Assert.AreEqual(p[i], expected[i]);
                }
            }
        }

        [Test]
        public void UnityGuidToGUID()
        {
            UnityGuid guidUnity = new UnityGuid("394ada038ba04f26b0011a6cdeb05a62");
            Guid guidCs = new Guid("394ada038ba04f26b0011a6cdeb05a62");
            Assert.AreEqual(guidCs, guidUnity.ToGuid());
        }
    }


    //---------------------------------------------------------------------------------------------------
    // PLAYER CONNECTION DIRECT CONNECTION TESTS
    //---------------------------------------------------------------------------------------------------
    public class PlayerConnectionDirectConnectionTests : PlayerConnectionTestFixture
    {
        [Test]
        public void DirectConnectionFailureUnavailable()
        {
            Connection.ConnectDirect(spoofIp, spoofPort);
            Assert.IsTrue(Connection.ConnectionInitialized);
            PingInternal();
            Assert.IsFalse(Connection.ConnectionInitialized);
            Assert.IsFalse(Connection.Connected);
        }

        [Test]
        public void DirectConnectionCacheAddress()
        {
            Connection.ConnectDirect(spoofIp, spoofPort);
            PingInternal();
            Assert.IsFalse(Connection.ConnectionInitialized);
            Assert.IsFalse(Connection.Connected);
            Assert.IsFalse(SpoofConnected);

            // Make sure it doesn't try to re-init to default address, which would actually pass here as it connects to Unity
            PingInternal();
            Assert.IsFalse(Connection.ConnectionInitialized);
            Assert.IsFalse(Connection.Connected);
            Assert.IsFalse(SpoofConnected);
        }

        [Test]
        public void DirectConnectionRetry()
        {
            Connection.ConnectDirect(spoofIp, spoofPort);
            PingInternal();
            Assert.IsFalse(Connection.ConnectionInitialized);
            Assert.IsFalse(Connection.Connected);
            Assert.IsFalse(SpoofConnected);

            SpoofInitListenInternal();
            PingInternal();
            SpoofAcceptInternal();
            Assert.IsTrue(Connection.ConnectionInitialized);
            Assert.IsTrue(Connection.Connected);
            Assert.IsTrue(SpoofConnected);
            SpoofExitInternal();
        }

        [Test]
        public void DirectConnectionDisconnect()
        {
            SpoofInitListenInternal();
            Connection.ConnectDirect(spoofIp, spoofPort);
            SpoofAcceptInternal();
            PingInternal();
            Assert.IsTrue(Connection.ConnectionInitialized, "Player connection is not initialized but should be");
            Assert.IsTrue(Connection.Connected, "Player connection is not connected but should be");
            Assert.IsTrue(SpoofConnected, "Spoof is not connected but should be");
            SpoofExitInternal();

            Baselib_Timer_WaitForAtLeast(500);

            PingInternal();
            Assert.IsFalse(Connection.ConnectionInitialized, "Player connection is initialized but shouldn't be");
            Assert.IsFalse(Connection.Connected, "Player connection is connected but shouldn't be");
            Assert.IsFalse(SpoofConnected, "Spoof is connected but shouldn't be");
        }

        [Test]
        public void DirectConnectionReconnect()
        {
            DirectConnectionDisconnect();

            SpoofInitListenInternal();
            PingInternal();
            SpoofAcceptInternal();
            Assert.IsTrue(Connection.ConnectionInitialized, "Player connection is not initialized but should be");
            Assert.IsTrue(Connection.Connected, "Player connection is not connected but should be");
            Assert.IsTrue(SpoofConnected, "Spoof is not connected but should be");
            SpoofExitInternal();
        }
    }


    //---------------------------------------------------------------------------------------------------
    // PLAYER CONNECTION DIRECT LISTENER TESTS
    //---------------------------------------------------------------------------------------------------
#if !UNITY_WEBGL
    public class PlayerConnectionListenTests : PlayerConnectionTestFixture
    {
        [Test]
        public void DirectListenFailureUnavailable()
        {
            Connection.ConnectListen(spoofIp, spoofPort);
            PingInternalTimeout();
            Assert.IsTrue(Connection.Listening, "Player connection should be listening but isn't");
            Assert.IsFalse(Connection.ConnectionInitialized);
            Assert.IsFalse(Connection.Connected);
        }

        [Test]
        public void DirectListenCacheAddress()
        {
            Connection.ConnectListen(spoofIp, spoofPort);
            PingInternalTimeout();
            Assert.IsTrue(Connection.Listening, "Player connection should be listening but isn't");
            Assert.IsFalse(Connection.ConnectionInitialized);
            Assert.IsFalse(Connection.Connected);
            Assert.IsFalse(SpoofConnectedAsClient);

            // Make sure it doesn't try to re-init to default address, which would actually pass here as it connects to Unity
            PingInternalTimeout();
            Assert.IsTrue(Connection.Listening, "Player connection should be listening but isn't");
            Assert.IsFalse(Connection.ConnectionInitialized);
            Assert.IsFalse(Connection.Connected);
            Assert.IsFalse(SpoofConnectedAsClient);
        }

        [Test]
        public void DirectListenRetry()
        {
            Connection.ConnectListen(spoofIp, spoofPort);
            PingInternalTimeout();
            Assert.IsTrue(Connection.Listening, "Player connection should be listening but isn't");
            Assert.IsFalse(Connection.ConnectionInitialized);
            Assert.IsFalse(Connection.Connected);
            Assert.IsFalse(SpoofConnectedAsClient);

            SpoofInitConnectInternal();
            PingInternal();
            Assert.IsTrue(Connection.ConnectionInitialized);
            Assert.IsTrue(Connection.Connected);
            Assert.IsTrue(SpoofConnectedAsClient);
            SpoofExitInternal();
        }

        [Test]
        public void DirectListenDisconnect()
        {
            Connection.ConnectListen(spoofIp, spoofPort);
            SpoofInitConnectInternal();
            PingInternal();
            Assert.IsTrue(Connection.ConnectionInitialized, "Player connection is not initialized but should be");
            Assert.IsTrue(Connection.Connected, "Player connection is not connected but should be");
            Assert.IsTrue(SpoofConnectedAsClient, "Spoof is not connected but should be");
            SpoofExitInternal();


            Baselib_Timer_WaitForAtLeast(200);

            PingInternal();
            Assert.IsFalse(Connection.ConnectionInitialized, "Player connection is initialized but shouldn't be");
            Assert.IsFalse(Connection.Connected, "Player connection is connected but shouldn't be");
            Assert.IsFalse(SpoofConnectedAsClient, "Spoof is connected but shouldn't be");
        }

        [Test]
        public void DirectListenReconnect()
        {
            DirectListenDisconnect();

            PingInternalTimeout();
            Assert.IsTrue(Connection.Listening, "Player connection should be listening but isn't");

            SpoofInitConnectInternal();
            PingInternal();
            Assert.IsTrue(Connection.ConnectionInitialized, "Player connection is not initialized but should be");
            Assert.IsTrue(Connection.Connected, "Player connection is not connected but should be");
            Assert.IsTrue(SpoofConnectedAsClient, "Spoof is not connected but should be");
            SpoofExitInternal();
        }
    }
#endif

    //---------------------------------------------------------------------------------------------------
    // PLAYER CONNECTION RECEIVE MESSAGE TESTS
    //---------------------------------------------------------------------------------------------------
    public class PlayerConnectionReceiveMessageTests : PlayerConnectionTestFixture
    {
        // In ForceSize
        internal const int kForceSizeSucceed = -1;
        internal const int kForceSizeFailPreviousSizeSmall = -2;
        internal const int kForceSizeFailMagic = -3;
        internal const int kForceSizeFailPreviousSizeBig = -4;
        internal void ReceiveMessageInternal(string testText, int[] forceSize = null)
        {
            SpoofInitListenInternal();
            Connection.ConnectDirect(spoofIp, spoofPort);
            Baselib_Timer_WaitForAtLeast(200);
            SpoofAcceptInternal();

            if (forceSize == null)
                forceSize = new int[] { kForceSizeSucceed };

            foreach (int s in forceSize)
            {
                int sendSize = testText.Length * 2;
                int size = s < 0 ? sendSize : s;

                bool gotMessage = false;

                PlayerConnection.instance.Register(spoofMessage.ToGuid(), (MessageEventArgs test) =>
                {
                    if (s == kForceSizeSucceed)
                        Assert.AreEqual(testText.Length * 2, test.data.Length);
                    else
                        Assert.AreNotEqual(testText.Length * 2, test.data.Length);
                    unsafe
                    {
                        fixed (byte* b = test.data)
                        {
                            if (s == kForceSizeSucceed)
                                Assert.AreEqual(testText, new string((char*)b, 0, test.data.Length / 2));
                            else
                                Assert.AreNotEqual(testText, new string((char*)b, 0, test.data.Length / 2));
                        }
                    }
                    gotMessage = true;
                });

                unsafe
                {
                    uint magic = s == kForceSizeFailMagic ? 0x12345678 : EditorMessageIds.kMagicNumber;
                    SpoofSendInternal((byte*)&magic, 4);
                    fixed (UnityGuid* u = &spoofMessage)
                    {
                        SpoofSendInternal((byte*)u, 16);
                    }
                    SpoofSendInternal((byte*)&size, 4);
                    fixed (char* t = testText)
                    {
                        SpoofSendInternal((byte*)t, sendSize);
                    }
                }

                var start = Time.timeAsDouble;
                while (!gotMessage && Time.timeAsDouble - start < kConnectTime)
                {
                    Connection.TransmitAndReceive();
                    Baselib_Timer_WaitForAtLeast(20);
                }

                if (s == kForceSizeFailPreviousSizeBig || s == kForceSizeFailPreviousSizeSmall || s == kForceSizeFailMagic)  // should have disconnected, but may have already auto-reconnected
                {
                    PingInternal();
                    SpoofAcceptInternal();
                    // We got a message if too big, but we still disconnected because we also get a header and the magic is bad
                    if (s == kForceSizeFailPreviousSizeBig)
                        Assert.IsTrue(gotMessage);
                    else
                        Assert.IsFalse(gotMessage);
                }
                else if (size > sendSize)
                    Assert.IsFalse(gotMessage);
                else
                    Assert.IsTrue(gotMessage);

                Connection.UnregisterAllMessages();
            }

            SpoofExitInternal();
        }

        [Test]
        public void ReceiveMessageSizeNormal()
        {
            ReceiveMessageInternal("Hello World!");
        }

        [Test]
        public void ReceiveMessageSizeZero()
        {
            ReceiveMessageInternal("");
        }

        [Test]
        public void ReceiveMessageSizeBig()
        {
            int chars = Connection.kReserveCapacityReceive * 4;
            string msg = "";
            string piece = "aslkjncslkhbvfdalkjve98vmlkjvme04fimalkfwaj984awcmr32rcjmwa4p98cjrm432rc09wacrma048rcjwa408jr94a8jr09j04am04wac94kc0a94kfc0,4a9f,39kfc43";
            for (int i = 0; i < chars; i += piece.Length)
                msg += piece;
            ReceiveMessageInternal(msg);
        }

        [Test]
        public void ReceiveMessageBadSizeTooSmall()
        {
            // Should fail miserably, then recover gracefully
            string text = "Hello World!";
            ReceiveMessageInternal(text, new int[] { text.Length * 2 - 7, kForceSizeFailPreviousSizeSmall, kForceSizeSucceed });
        }

        [Test]
        public void ReceiveMessageBadSizeTooBig()
        {
            // Should fail miserably, then recover gracefully
            string text = "Hello World!";
            ReceiveMessageInternal(text, new int[] { text.Length * 2 + 7, kForceSizeFailPreviousSizeBig, kForceSizeSucceed });
        }

        [Test]
        public void ReceiveMessageBadHeader()
        {
            // Should fail miserably, then recover gracefully
            ReceiveMessageInternal("Hello World!", new int[] { kForceSizeFailMagic, kForceSizeSucceed });
        }
    }


    //---------------------------------------------------------------------------------------------------
    // PLAYER CONNECTION SEND MESSAGE TESTS
    //---------------------------------------------------------------------------------------------------
    public class PlayerConnectionSendMessageTests : PlayerConnectionTestFixture
    {
        private int GetSizeAlign(int size)
        {
            while ((size & 3) != 0)
                size++;
            return size;
        }

        internal unsafe void SendMessageInternal(string[] testText)
        {
            SpoofInitListenInternal();
            Connection.ConnectDirect(spoofIp, spoofPort);
            Baselib_Timer_WaitForAtLeast(200);
            SpoofAcceptInternal();

            foreach (string text in testText)
            {
                fixed(char* t = text)
                {
                    messageBuilder->WriteMessage(spoofMessage, (byte*)t, text.Length * 2);
                }
            }

            int messageSize = messageBuilder->DataToSendBytes;

            messageBuilder->TrySubmitStream(true);

            var start = Time.timeAsDouble;
            while (Connection.HasSendDataQueued && Time.timeAsDouble - start < kConnectTime)
                Connection.TransmitAndReceive();

            Assert.IsFalse(Connection.HasSendDataQueued, "Timed out! (Froze)");

            byte[] data = new byte[messageSize];
            fixed(byte* dptr = data)
            {
                SpoofReceiveInternal(dptr, messageSize);
                byte* d = dptr;

                foreach (string text in testText)
                {
                    Assert.AreEqual(EditorMessageIds.kMagicNumber, *(uint*)d);
                    Assert.AreEqual(spoofMessage, *(UnityGuid*)(d + 4));
                    Assert.AreEqual(GetSizeAlign(text.Length * 2), *(uint*)(d + 20));
                    Assert.AreEqual(text, new String((char*)(d + 24), 0, text.Length));
                    d += 24 + text.Length * 2;
                }
            }

            SpoofExitInternal();
        }

        [Test]
        public void SendMessageSizeNormal()
        {
            SendMessageInternal(new string[] { "Hello World!" });
        }

        [Test]
        public void SendMessageSizeZero()
        {
            SendMessageInternal(new string[] { "" });
        }

        [Test]
        public void SendMessageSizeBig()
        {
            int chars = Connection.kReserveCapacityReceive * 4;
            string msg = "";
            string piece = "aslkjncslkhbvfdalkjve98vmlkjvme04fimalkfwaj984awcmr32rcjmwa4p98cjrm432rc09wacrma048rcjwa408jr94a8jr09j04am04wac94kc0a94kfc0,4a9f,39kfc43";
            for (int i = 0; i < chars; i += piece.Length)
                msg += piece;
            SendMessageInternal(new string[] { msg });
        }

        [Test]
        public void SendMessageSizeSmallZeroSmall()
        {
            SendMessageInternal(new string[] { "Hello World!", "", "Try another" });
        }

        [Test]
        public void SendMessageSizeSmallBig()
        {
            int chars = Connection.kReserveCapacityReceive * 4;
            string msg = "";
            string piece = "aslkjncslkhbvfdalkjve98vmlkjvme04fimalkfwaj984awcmr32rcjmwa4p98cjrm432rc09wacrma048rcjwa408jr94a8jr09j04am04wac94kc0a94kfc0,4a9f,39kfc43";
            for (int i = 0; i < chars; i += piece.Length)
                msg += piece;
            SendMessageInternal(new string[] { "Hello World!", msg });
        }

        [Test]
        public void SendMessageSizeBigBig()
        {
            int chars = Connection.kReserveCapacityReceive * 4;
            string msg = "";
            string piece = "aslkjncslkhbvfdalkjve98vmlkjvme04fimalkfwaj984awcmr32rcjmwa4p98cjrm432rc09wacrma048rcjwa408jr94a8jr09j04am04wac94kc0a94kfc0,4a9f,39kfc43";
            for (int i = 0; i < chars; i += piece.Length)
                msg += piece;
            SendMessageInternal(new string[] { msg, msg });
        }

        //---------------------------------------------------------------------------------------------------
        // PLAYER CONNECTION MESSAGE HANDLER TESTS
        //---------------------------------------------------------------------------------------------------

        // This needs to be part of command line suite of tests outside of Unity Test Runner.
        // Profiler tests connecting to the editor will have to do the same.
        /*
		[Test]
		public void VerifyMessageHandlerFromUnity()
		{
			bool gotMessage = false;
			PlayerConnection.instance.Register(UnityMessageIds.kProfilerSetMode.ToGuid(), (MessageEventArgs test) => gotMessage = true);
			Connection.ConnectDirect("127.0.0.1", 34999);  // Unity Direct Connection Port

			Assert.IsTrue(Connection.Initialized, "Player connection is not initialized but should be");

			var start = Time.timeAsDouble;
			while (!gotMessage && Time.timeAsDouble - start < 8)
			{
				Connection.TransmitAndReceive();
				Baselib_Timer_WaitForAtLeast(20);
			}

			Connection.UnregisterAllMessages();

			Assert.IsTrue(gotMessage, "Message handler from Unity not received");
		}

        [Test]
        public void DirectConnectionSuccessUnity()
        {
            Connection.ConnectDirect("127.0.0.1", 34999);  // Unity Direct Connection Port
            PingInternal();
            Assert.IsTrue(Connection.ConnectionInitialized);
            Assert.IsTrue(Connection.Connected);
            Assert.IsFalse(SpoofConnected);
        }

        [Test]
        public void DirectConnectionReconnectUnity()
        {
            Connection.ConnectDirect("127.0.0.1", 34999);  // Unity Direct Connection Port
            PingInternal();
            Assert.IsTrue(Connection.ConnectionInitialized, "Player connection is not initialized but should be (1)");
            Assert.IsTrue(Connection.Connected, "Player connection is not connected but should be (1)");
            Assert.IsFalse(SpoofConnected, "Spoof is connected but shouldn't be (1)");
            Connection.Disconnect();

            Baselib_Timer_WaitForAtLeast(500);

            Connection.Connect();
            PingInternal();
            Assert.IsTrue(Connection.ConnectionInitialized, "Player connection is not initialized but should be (2)");
            Assert.IsTrue(Connection.Connected, "Player connection is not connected but should be (2)");
            Assert.IsFalse(SpoofConnected, "Spoof is connected but shouldn't be (2)");
            Connection.Disconnect();
        }
        */

        //---------------------------------------------------------------------------------------------------
        // SIDE CASES
        //---------------------------------------------------------------------------------------------------

        [Test]
        public void LogAfterShutdown()
        {
            // Get a fresh start
            Logger.Shutdown();

            Logger.Initialize();
            Logger.Log("This should work");
            PerTestTearDown();
            Logger.Shutdown();
            Logger.Log("This shouldn't crash");
            PerTestSetUp();

            // Test framework expects static systems to be initialized and to shut them down at the end of all tests            
            Logger.Initialize();
        }
    }

    //---------------------------------------------------------------------------------------------------
    // UNITY INSTANCE TRANSMIT AND RECEIVE TESTS
    //---------------------------------------------------------------------------------------------------
    public class PlayerConnectionUnityInstanceTests : PlayerConnectionTestFixture
    {
        public void InitSpoofConnection()
        {
            SpoofInitListenInternal();
            Connection.ConnectDirect(spoofIp, spoofPort);
            Baselib_Timer_WaitForAtLeast(200);
            SpoofAcceptInternal();
        }

        public void ShutdownSpoofConnection()
        {
            Connection.UnregisterAllMessages();
            SpoofExitInternal();
        }

        public unsafe void SendSpoofMessage()
        {
            string testText = "Hello!";
            int sendSize = testText.Length * 2;

            uint magic = EditorMessageIds.kMagicNumber;
            SpoofSendInternal((byte*)&magic, 4);
            fixed (UnityGuid* u = &spoofMessage)
            {
                SpoofSendInternal((byte*)u, 16);
            }
            SpoofSendInternal((byte*)&sendSize, 4);
            fixed (char* t = testText)
            {
                SpoofSendInternal((byte*)t, sendSize);
            }
        }

        [Test]
        public void ReceiveMessage()
        {
            // The entire test harness is already in scope, so we are breaking that scope
            // so we can test if the playerconnection can allow message handlers to make temp allocs
            TempMemoryScope.ExitScope();

            InitSpoofConnection();

            PlayerConnection.instance.Register(spoofMessage.ToGuid(), (MessageEventArgs test) =>
            {
                // We want to ensure we can still use the temp allocator in our registered handler
                var tempAllocatedContainer = new NativeList<int>(Allocator.Temp);
                tempAllocatedContainer.Add(1); // Perform an operation that will validate the safetyhandle
                tempAllocatedContainer.Dispose();
            });

            DotsRuntime.UpdatePreFrame();
            SendSpoofMessage();
            Baselib_Timer_WaitForAtLeast(100);
            DotsRuntime.UpdatePostFrame(true); // Internally calls TransmistAndReceive();

            // Note we don't validate we received the spoofed message.
            // We do this to avoid ci issues that might occur if our message doesn't
            // get picked up over the socket in time. I feel comfortable enough that this test
            // will still validate it's purpose and not frequently fail silently on the farm
            // We don't loop until we receive the message as we explicitly want UpdatePostFrame()
            // to be the code that receives and handles the messages and we currently don't separate
            // the logic for Transmitting and Receiving messages.

            ShutdownSpoofConnection();

            TempMemoryScope.EnterScope(); // restore temp memscope
        }
    }
}
