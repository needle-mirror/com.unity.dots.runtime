#if UNITY_PORTABLE_TEST_RUNNER
using NUnit.Framework;
using UnityEngine;
#else
using NUnitLite;
#endif
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Tiny;
using Unity.Jobs.LowLevel.Unsafe;
using System;
using Unity.Core;

public static class Program {
    public static int Main(string[] args)
    {
        // Not using UnityInstance.Initialize here because it also creates a world, and some tests exist
        // that expect to handle their own world life cycle which currently conflicts with our world design
        UnityInstance.BurstInit();

        DotsRuntime.Initialize();

        TempMemoryScope.EnterScope();
        // Static storage used for the whole lifetime of the process. Create it once here
        // so heap tracking tests won't fight over the storage causing alloc/free mismatches
        WordStorage.Initialize();

        // TypeManager initialization uses temp memory, so let's free it before creating a global scope
        Unity.Entities.TypeManager.Initialize();
        TempMemoryScope.ExitScope();

        // Should have stack trace with tests
        NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;

        int result = 0;
#if UNITY_PORTABLE_TEST_RUNNER
        double start = Time.timeAsDouble;
        UnitTestRunner.Run();
        double end = Time.timeAsDouble;
        PrintResults(end - start);
#else
        result = new AutoRun().Execute(args);
#endif

        TempMemoryScope.EnterScope();
        Unity.Entities.TypeManager.Shutdown();
        WordStorage.Shutdown();
        TempMemoryScope.ExitScope();

        DotsRuntime.Shutdown();

        return result;
    }

#if UNITY_PORTABLE_TEST_RUNNER
    static void PrintResults(double deltaTime)
    {
        /*
            NUnit output.

            Test Run Summary
              Overall result: Warning
              Test Count: 992, Passed: 898, Failed: 0, Warnings: 0, Inconclusive: 0, Skipped: 94
                Skipped Tests - Ignored: 82, Explicit: 12, Other: 0
              Start time: 2020-04-02 14:45:59Z
                End time: 2020-04-02 14:46:11Z
                Duration: 12.243 seconds
         */

        int skipped = Assert.testsLimitation + Assert.testsIgnored + Assert.testsNotSupported;
        int totalTests = Assert.testsRan + skipped;

        // Editor assumes error if "Test Run Summary" is absent.
        Console.WriteLine("");
        Console.WriteLine($"Test Suite Data");
        Console.WriteLine($"  Assertions successful: {Assert.assertPassCount}");
#if UNITY_SINGLETHREADED_JOBS
        Console.WriteLine($"  Single-threaded.");
#else
        Console.WriteLine($"  Multi-threaded, Cores: {JobsUtility.JobWorkerCount}");
#endif
        Console.WriteLine("");
        Console.WriteLine($"Test Run Summary");
        Console.WriteLine($"  Overall result: Passed");
        Console.WriteLine($"  Tests Count: {totalTests}, Passed: {Assert.testsRan}, Skipped: {skipped}");
        Console.WriteLine($"    Passed Tests - Full: {Assert.testsRan - Assert.testsPartiallySupported}, Partial: {Assert.testsPartiallySupported}");
        Console.WriteLine($"    Skipped Tests - Ignored: {Assert.testsIgnored}, NotSupported: {Assert.testsNotSupported}, Limitation: {Assert.testsLimitation}");
        Console.WriteLine($"  Duration: {(float)deltaTime} seconds");
    }
#endif
}
