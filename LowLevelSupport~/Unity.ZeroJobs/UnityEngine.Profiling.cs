using System;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
#if ENABLE_PROFILER
using Unity.Development.Profiling;
#endif

namespace UnityEngine.Profiling
{
    public class CustomSampler
    {
        public static CustomSampler Create(string s) => throw new NotImplementedException();
        public void Begin() => throw new NotImplementedException();
        public void End() => throw new NotImplementedException();
    }

    public static class Profiler
    {
        public static unsafe void BeginSample(string s)
        {
#if ENABLE_PROFILER
            // Just gets the marker if it already exists
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
