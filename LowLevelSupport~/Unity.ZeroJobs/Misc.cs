using System;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Collections.LowLevel.Unsafe;
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
    public struct TempMemoryScope
    {
        // Currently, we only support a single, per-frame scope. If we find need for per-job nested scope as well,
        // or other scopes, then an mpmc stack should be used for tracking temp mem safety handle and possibly bump allocator context.
        // Ref counting to support tests until this can be further upgraded.
        private struct TempHandleData {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            public AtomicSafetyHandle handle;
#endif
            public int refCount;
        };
        private static readonly SharedStatic<TempHandleData> s_TempMemHandle = SharedStatic<TempHandleData>.GetOrCreate<TempMemoryScope, TempHandleData>();

        public static void EnterScope()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (s_TempMemHandle.Data.refCount == 0)
                s_TempMemHandle.Data.handle = AtomicSafetyHandle.Create();
            s_TempMemHandle.Data.refCount++;
#endif
        }

        public static void ExitScope()
        {
            s_TempMemHandle.Data.refCount--;
            if (s_TempMemHandle.Data.refCount == 0)
            {
                UnsafeUtility.FreeTempMemory();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.Release(s_TempMemHandle.Data.handle);
#endif
            }
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal static AtomicSafetyHandle GetSafetyHandle()
        {
            return s_TempMemHandle.Data.handle;
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
            AtomicSafetyHandle.Initialize();
            JobNames.Initialize();
#endif
#if ENABLE_PLAYERCONNECTION
            Connection.Initialize();
            Logger.Initialize();
#endif
#if ENABLE_PROFILER
            Profiler.Initialize();
#endif

            firstFrame = true;
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
        }

        public static void UpdatePreFrame()
        {
            if (firstFrame)
            {
#if ENABLE_PROFILER
                ProfilerProtocolSession.SendNewFrame();
                rootMarker.Begin();
                mainMarker.Begin();
#endif
                firstFrame = false;
            }

            TempMemoryScope.EnterScope();
        }

        public static void UpdatePostFrame(bool willContinue)
        {
#if ENABLE_PROFILER
            ProfilerStats.CalculateStatsSnapshot();
#endif

            TempMemoryScope.ExitScope();
            UnsafeUtility.FreeTempMemory();

#if ENABLE_PROFILER
            mainMarker.End();
            rootMarker.End();

            ProfilerProtocolSession.SendProfilerStats();

            // Calculated per frame
            ProfilerStats.GatheredStats = ProfilerModes.ProfileDisabled;

            ProfilerProtocolSession.SendNewMarkersAndThreads();
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
        }
    }
}
