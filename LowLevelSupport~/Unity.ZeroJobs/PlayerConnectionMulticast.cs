// Multicast is used to announce our existence to the local network - especially to Unity Editor. It can also be useful, for instance, for
// debuggers to know about us.
//
// Multicast should always be enabled if player connection is enabled in non-web builds. Multicast's main purpose is to support
// the editor initiating a connection to us automatically in development builds.
//
// However, in web builds, since there is
// a) no UDP in WebSockets and
// b) no listening for WebSockets connections therefore no auto-connection from the Editor
// we disable multicasting.
//
// SIDE NOTE:
// IL2CPP managed debugging uses multicasting even on web through it is not supported by normal WebSockets. Support for this
// is provided through a posix-sockets emulation layer by a WebSockets based proxy-server included with Emscripten. As mentioned in a later
// comment, this manner of translation is too slow to be used for profiler, livelink, etc, and so we do not take "advantage"
// of it for the general case here.


#if ENABLE_PLAYERCONNECTION && ENABLE_MULTICAST

using System;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using static Unity.Baselib.LowLevel.Binding;
using static System.Text.Encoding;

namespace Unity.Development.PlayerConnection
{
    // This is used inside Connection directly
    internal class Multicast
    {
#if !UNITY_WEBGL
        [DllImport("lib_unity_zerojobs")]
        private static extern unsafe int GetIPs(byte* addr_10_16);
#endif

        private static Baselib_Socket_Handle m_hSocket = Baselib_Socket_Handle_Invalid;
        private static Baselib_NetworkAddress m_hAddress;
        private static Baselib_ErrorState m_errState;
        private static string m_broadcastIp = "225.0.0.222";  // local network multicast address that unity editor will observe for multicast messages
        private static ushort m_broadcastPort = (ushort)EditorPorts.Multicast;
        private static int m_broadcastCountdown = 0;
        private static bool m_WebBroadcastInitialized = false;
        private static bool m_initialized = false;
        private static string m_whoAmI;
        private static byte[] m_whoAmIBytes;

        private const uint kPlayerConnectionVersion = 0x00100100;  // must match with editor build
        private const uint kPlayerGuidDirectConnect = 1337;  // special player id that we must provide if we aren't listening for unity editor connection request
        private const int kBroadcastCounter = 30;

        [Flags]
        private enum Flags : ushort
        {
            kRequestImmediateConnect = 1 << 0,  // must be enabled for auto connect to have effect
            kSupportsProfile = 1 << 1,
            //kCustomMessage = 1 << 2,  // unused
            //kUseAlternateIP = 1 << 3,  // unused
            kAutoConnect = 1 << 4
        };

        private static void CreateWhoAmI(bool directConnect, ushort listenPort, uint editorGuid32, string gameName)
        {
            Flags flags = 0;
            if (!directConnect)
                flags |= Flags.kAutoConnect | Flags.kRequestImmediateConnect;
#if ENABLE_PROFILER
            flags |= Flags.kSupportsProfile;
#endif
            uint playerGuid32 = (uint)Baselib_Timer_GetHighPrecisionTimerTicks();
            if (playerGuid32 == 0)  // id 0 is special in the editor
                playerGuid32--;
            if (directConnect)
                playerGuid32 = kPlayerGuidDirectConnect;

#if UNITY_WINDOWS
            string platform = "DotsRuntimeWindowsPlayer";
#elif UNITY_LINUX
            string platform = "DotsRuntimeLinuxPlayer";
#elif UNITY_MACOSX
            string platform = "DotsRuntimeOsxPlayer";
#elif UNITY_IOS
            string platform = "DotsRuntimeIosPlayer";
#elif UNITY_ANDROID
            string platform = "DotsRuntimeAndroidPlayer";
#elif UNITY_WEBGL
            string platform = "DotsRuntimeWebglPlayer";
#else
            string platform = "DotsRuntimePlayer";
#endif

#if UNITY_DOTSRUNTIME_IL2CPP_MANAGED_DEBUGGER // This is irrelevant for non-il2cpp builds
            int debugEnabled = 1;
#else
            int debugEnabled = 0;
#endif

            string localIp = "127.0.0.1";
#if !UNITY_WEBGL
            byte[] localIpUtf8 = new byte[10 * 16];
            int localCount = 0;
            unsafe
            {
                fixed (byte* lip = localIpUtf8)
                {
                    localCount = GetIPs(lip);
                }
            }

            if (localCount > 0)
            {
                int end = 15;
                while (localIpUtf8[end] == 0)
                    end--;
                localIp = UTF8.GetString(localIpUtf8, 0, end + 1);
            }
#endif

            m_whoAmI = $"[IP] {localIp}";
            m_whoAmI += $" [Port] {listenPort}";
            m_whoAmI += $" [Flags] {(ushort)flags}";
            m_whoAmI += $" [Guid] {playerGuid32}";
            m_whoAmI += $" [EditorId] {editorGuid32}";
            m_whoAmI += $" [Version] {kPlayerConnectionVersion}";
#if UNITY_DOTSRUNTIME_IL2CPP_MANAGED_DEBUGGER // This is irrelevant for non-il2cpp builds
            m_whoAmI += $" [Id] {platform}({localIp}):56000";
#else
            m_whoAmI += $" [Id] {platform}";
#endif
            m_whoAmI += $" [Debug] {debugEnabled}";
            m_whoAmI += $" [PackageName] {platform}";
            m_whoAmI += $" [ProjectName] {gameName}";

            m_whoAmIBytes = UTF8.GetBytes(m_whoAmI);
        }

        public static void Initialize(bool directConnect, ushort listenPort)
        {
            Initialize(directConnect, listenPort, 0, "DOTS_Runtime_Game");
            m_WebBroadcastInitialized = true;
        }

        public static void Initialize(bool directConnect, ushort listenPort, uint editorGuid32, string gameName)
        {
            if (m_WebBroadcastInitialized)
                Shutdown();

            if (m_initialized)
                return;

            unsafe
            {
                m_hSocket = Baselib_Socket_Create(Baselib_NetworkAddress_Family.IPv4, Baselib_Socket_Protocol.UDP, (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref m_errState));
                if (m_errState.code == Baselib_ErrorCode.Success)
                {
                    fixed (byte* bip = UTF8.GetBytes(m_broadcastIp))
                    {
                        Baselib_NetworkAddress_Encode((Baselib_NetworkAddress*)UnsafeUtility.AddressOf(ref m_hAddress), Baselib_NetworkAddress_Family.IPv4,
                            bip, m_broadcastPort, (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref m_errState));
                    }
                }
            }

            if (m_errState.code != Baselib_ErrorCode.Success)
            {
                if (m_hSocket.handle != Baselib_Socket_Handle_Invalid.handle)
                {
                    Baselib_Socket_Close(m_hSocket);
                    m_hSocket = Baselib_Socket_Handle_Invalid;
                }
                return;
            }

            CreateWhoAmI(directConnect, listenPort, editorGuid32, gameName);

            m_initialized = true;
        }

        public static void Shutdown()
        {
            if (!m_initialized)
                return;

            if (m_hSocket.handle != Baselib_Socket_Handle_Invalid.handle)
            {
                Baselib_Socket_Close(m_hSocket);
                m_hSocket = Baselib_Socket_Handle_Invalid;
            }

            m_errState.code = Baselib_ErrorCode.Success;
            m_initialized = false;
            m_WebBroadcastInitialized = false;
        }

        [MonoPInvokeCallback]
        public static void Broadcast()
        {
            if (!m_initialized)
                return;

            if (m_broadcastCountdown > 0)
            {
                m_broadcastCountdown--;
                return;
            }

            Baselib_Socket_Message message = new Baselib_Socket_Message();
            unsafe
            {
                fixed (byte* bip = m_whoAmIBytes)
                {
                    message.data = (IntPtr)bip;
                }
                message.dataLen = (uint)m_whoAmIBytes.Length;
                message.address = (Baselib_NetworkAddress*)UnsafeUtility.AddressOf(ref m_hAddress);

                Baselib_Socket_UDP_Send(m_hSocket, &message, 1, (Baselib_ErrorState*)UnsafeUtility.AddressOf(ref m_errState));
            }

            m_broadcastCountdown = kBroadcastCounter;
        }
    }
}
#endif
