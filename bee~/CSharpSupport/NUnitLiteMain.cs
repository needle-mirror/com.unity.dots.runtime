using NUnitLite;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Tiny;
using Unity.Jobs.LowLevel.Unsafe;

public static class Program {
    public static int Main(string[] args)
    {
        // Not using UnityInstance.Initialize here because it also creates a world, and some tests exist
        // that expect to handle their own world life cycle which currently conflicts with our world design
        UnityInstance.BurstInit();

        // Don't call Dots Runtime Initialize here - only initialize safety handles
        // Anything else such as Player Connection or Profiler should be initialized/shutdown
        // on an individual basis for the test(s) that require these subsystems.
        AtomicSafetyHandle.Initialize();
        Unity.Entities.TypeManager.Initialize();

        // Should have stack trace with tests
        NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace; 

        var result = new AutoRun().Execute(args);

        // Currently, Windows (.NET) will exit without requiring other threads to complete
        // OSX (Mono), on the other hand, requires all other threads to complete
        JobsUtility.Shutdown();

        UnsafeUtility.FreeTempMemory();

        Unity.Entities.TypeManager.Shutdown();
        AtomicSafetyHandle.Shutdown();

        return result;
    }
}
