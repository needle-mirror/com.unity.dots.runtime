using System;
#if UNITY_DOTSRUNTIME_IL2CPP_WAIT_FOR_MANAGED_DEBUGGER
using Unity.Development.PlayerConnection;
#endif
using Unity.Platforms;
using Unity.Core;

namespace Unity.Tiny.EntryPoint
{
    public static class Program
    {
        private static void Main()
        {
#if UNITY_DOTSRUNTIME
            DotsRuntime.Initialize();
#endif
            var unity = UnityInstance.Initialize();

            unity.OnTick = (double timestampInSeconds) =>
            {
                var shouldContinue = unity.Update(timestampInSeconds);
                if (shouldContinue == false)
                {
                    unity.Deinitialize();
                }
                return shouldContinue;
            };

#if UNITY_DOTSRUNTIME_IL2CPP_WAIT_FOR_MANAGED_DEBUGGER
            Connection.InitializeMulticast();
            DebuggerAttachDialog.Show(Multicast.Broadcast);
#endif

            RunLoop.EnterMainLoop(unity.OnTick);

            // Anything which can come after EnterMainLoop must occur in an event because
            // on some platforms EnterMainLoop exits immediately and events drive the application
            // lifecycle.
#if UNITY_DOTSRUNTIME
            // Currently this only works on mobile, but eventually all platforms should use this path
#if UNITY_ANDROID || UNITY_IOS
            PlatformEvents.OnQuit += (object sender, QuitEvent evt) => { DotsRuntime.Shutdown(); };
#else
            DotsRuntime.Shutdown();
#endif
#if UNITY_DOTSRUNTIME_TRACEMALLOCS
            var leaks = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.DebugGetAllocationsByCount();
            UnityEngine.Assertions.Assert.IsTrue(leaks.Count == 0);
#endif
#endif
        }
    }
}

