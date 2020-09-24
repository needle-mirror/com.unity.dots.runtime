using System;
using Unity.Platforms;
using Unity.Core;
using Unity.Collections;

namespace Unity.Tiny.EntryPoint
{
    public static class Program
    {
        private static void Main()
        {
            // One-time initialization per application
            DotsRuntime.Initialize();

            // Setup the global static string interning storage
            TempMemoryScope.EnterScope();
            WordStorage.Setup();
            TempMemoryScope.ExitScope();

            // A UnityInstance can exist per game state (there may potentially be more than one)
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

            // Anything which can come after EnterMainLoop must occur in an event because
            // on some platforms EnterMainLoop exits immediately and the application enters
            // an event-driven lifecycle.
            PlatformEvents.OnQuit += (object sender, QuitEvent evt) => { Shutdown(); };
            PlatformEvents.OnSuspendResume += (object sender, SuspendResumeEvent evt) => { unity.Suspended = evt.Suspend; };

            // Run
            RunLoop.EnterMainLoop(unity.OnTick);

            // DON'T CALL ANY CLEANUP HERE!
        }

        private static void Shutdown()
        {
            // Cleanup of word storage
            TempMemoryScope.EnterScope();
            WordStorage.Instance.Dispose();
            TempMemoryScope.ExitScope();

            DotsRuntime.Shutdown();
        }
    }
}

