using System;
using System.Runtime.InteropServices;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Burst;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
using Unity.Development.JobsDebugger;
#endif
#if ENABLE_PLAYERCONNECTION
using Unity.Development.PlayerConnection;
#endif
#if ENABLE_PROFILER
using Unity.Development.Profiling;
#endif

namespace AOT
{
    // Mono AOT compiler detects this attribute by name and generates required wrappers for
    // native->managed callbacks. Works only for static methods.
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public class MonoPInvokeCallbackAttribute : Attribute
    {
        public MonoPInvokeCallbackAttribute(Type type) {}
    }
}

namespace Unity.Linker.StrippingControls.Balanced
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class PreserveBodyAttribute : Attribute {}
}

//unity.properties has an unused "using UnityEngine.Bindings".
namespace UnityEngine.Bindings
{
    public class Dummy
    {
    }
}

namespace UnityEngine.Internal
{
    public class ExcludeFromDocsAttribute : Attribute {}
}

namespace Unity.Baselib.LowLevel
{
    public static class BaselibNativeLibrary
    {
        public const string DllName = JobsUtility.nativejobslib;
    }
}

namespace System
{
    public class CodegenShouldReplaceException : NotImplementedException
    {
        public CodegenShouldReplaceException() : base("This function should have been replaced by codegen")
        {
        }

        public CodegenShouldReplaceException(string msg) : base(msg)
        {
        }
    }
}

namespace Unity.Core
{
#if UNITY_WEBGL
    static class HTMLNativeCalls
    {
        [DllImport("lib_unity_core", EntryPoint = "init_html")]
        public static extern void init();
    }
#endif

    public struct TempMemoryScope
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private static unsafe void CreateScopeSafetyHandle()
        {
            AtomicSafetyHandle* handle = (AtomicSafetyHandle*)UnsafeUtility.Malloc(sizeof(AtomicSafetyHandle), 0, Allocator.Temp);
            *handle = AtomicSafetyHandle.Create();
            UnsafeUtility.SetTempScopeUser(handle);
        }

        private static unsafe void ReleaseScopeSafetyHandle()
        {
            void* user = UnsafeUtility.GetTempScopeUser();
            AtomicSafetyHandle.Release(*(AtomicSafetyHandle*)user);
            UnsafeUtility.Free(user, Allocator.Temp);
            UnsafeUtility.SetTempScopeUser(null);
        }
#endif

        public static unsafe void EnterScope()
        {
            UnsafeUtility.EnterTempScope();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CreateScopeSafetyHandle();
#endif
        }

        public static unsafe void ExitScope()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            ReleaseScopeSafetyHandle();
#endif
            UnsafeUtility.ExitTempScope();
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal static unsafe AtomicSafetyHandle GetSafetyHandle()
        {
            void* user = UnsafeUtility.GetTempScopeUser();
            return *(AtomicSafetyHandle*)user;
        }
#endif
    }

    public static class DotsRuntime
    {
#if ENABLE_PROFILER
        private static Unity.Profiling.ProfilerMarker rootMarker = new Profiling.ProfilerMarker("Hidden main root");
        private static Unity.Profiling.ProfilerMarker mainMarker = new Profiling.ProfilerMarker("Main Thread Frame");
#endif
        private static bool firstFrame = true;
#if DEBUG
        public static bool Initialized { get; private set; } = false;
#endif

        public static void Initialize()
        {
#if DEBUG
            // Instead of silently skipping, ensure we have predictable control over initialization and shutdown sequence.
            if (Initialized)
                throw new InvalidOperationException("DotsRuntime.Initialize() already called");
            Initialized = true;
#endif

            JobsUtility.Initialize();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            JobNames.Initialize();
            AtomicSafetyHandle.Initialize();
#endif
#if UNITY_WEBGL
            try
            {
                HTMLNativeCalls.init();
            }
            catch
            {
                Console.WriteLine("  Excepted (Is lib_unity_tiny2d_html.dll missing?).");
            }
#endif

#if ENABLE_PLAYERCONNECTION
            Connection.Initialize();
            Logger.Initialize();
#endif
#if ENABLE_PROFILER
            Profiler.Initialize();
#endif

#if UNITY_DOTSRUNTIME_IL2CPP_WAIT_FOR_MANAGED_DEBUGGER
            Connection.InitializeMulticast();
            DebuggerAttachDialog.Show(Multicast.Broadcast);
#endif

            InvokeEarlyInitMethods();

            firstFrame = true;
        }

        // This is a temporary solution for enabling functions that need to be called before
        // most other code is run (similar to a cctor however executed proactively rather than lazily)
        internal static void InvokeEarlyInitMethods()
        {
            throw new Exception("This function should have been replaced by codegen.");
        }

        public static void Shutdown()
        {
#if DEBUG
            // Instead of silently skipping, ensure we have predictable control over initialization and shutdown sequence.
            if (!Initialized)
                throw new InvalidOperationException("DotsRuntime.Shutdown() already called");
            Initialized = false;
#endif

#if ENABLE_PROFILER
            Profiler.Shutdown();
#endif
#if ENABLE_PLAYERCONNECTION
            Logger.Shutdown();
            Connection.Shutdown();
#endif
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Shutdown();
#endif
            JobsUtility.Shutdown();

#if UNITY_DOTSRUNTIME_TRACEMALLOCS
            var leaks = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.DebugGetAllocationsByCount();
            UnityEngine.Assertions.Assert.IsTrue(leaks.Count == 0);
#endif
        }

        public static void UpdatePreFrame()
        {
            TempMemoryScope.EnterScope();

            if (firstFrame)
            {
#if ENABLE_PROFILER
                ProfilerProtocolSession.SendNewFrame();
                rootMarker.Begin();
                mainMarker.Begin();
#endif
                firstFrame = false;
            }
        }

        public static void UpdatePostFrame(bool willContinue)
        {
#if ENABLE_PROFILER
            ProfilerStats.CalculateStatsSnapshot();
#endif

#if ENABLE_PROFILER
            mainMarker.End();
            rootMarker.End();
#endif

#if ENABLE_PLAYERCONNECTION
            Connection.TransmitAndReceive();
#endif

#if ENABLE_PROFILER
            if (willContinue)
            {
                ProfilerProtocolSession.SendNewFrame();
                rootMarker.Begin();
                mainMarker.Begin();
            }
#endif

            TempMemoryScope.ExitScope();
        }
    }
}
