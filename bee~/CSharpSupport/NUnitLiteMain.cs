using NUnitLite;
using Unity.Entities;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Tiny;

public static class Program {
    public static int Main(string[] args)
    {
        UnityInstance.BurstInit();
        TypeManager.Initialize();
        // Should have stack trace with tests
        NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace; 

        var result = new AutoRun().Execute(args);

#if !UNITY_SINGLETHREADED_JOBS
        // Currently, Windows (.NET) will exit without requiring other threads to complete
        // OSX (Mono), on the other hand, requires all other threads to complete
        JobsUtility.Shutdown();
#endif

        TypeManager.Shutdown();
        Unity.Collections.LowLevel.Unsafe.UnsafeUtility.FreeTempMemory();

        return result;
    }
}
