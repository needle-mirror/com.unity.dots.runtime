using System;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
#if ENABLE_PROFILER
using Unity.Development.Profiling;
#endif

namespace UnityEngine.Profiling
{
    public static class Profiler
    {
        public static unsafe void BeginSample(string s)
        {
#if ENABLE_PROFILER
            // Just gets the marker if it already exists - this is much slower than using ProfilerMarker objects though
            // because they just store the marker internally for reuse (avoiding a lot of potential string comparisons)
            IntPtr marker = ProfilerUnsafeUtility.CreateMarker(s, ProfilerUnsafeUtility.InternalCategoryInternal, MarkerFlags.Default, 0);
            ProfilerUnsafeUtility.BeginSample(marker);
            ProfilerProtocolThread.Stream.markerStack.PushMarker(marker);
#endif
        }

        public static unsafe void EndSample()
        {
#if ENABLE_PROFILER
            ProfilerUnsafeUtility.EndSample(ProfilerProtocolThread.Stream.markerStack.PopMarker());
#endif
        }
    }
}
