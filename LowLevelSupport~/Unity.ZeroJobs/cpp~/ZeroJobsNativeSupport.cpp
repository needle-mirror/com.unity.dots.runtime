#include <Unity/Runtime.h>
#include <Baselib.h>
#include <C/Baselib_Atomic.h>
#include <C/Baselib_Timer.h>
#include <Cpp/mpmc_node_queue.h>
#include <Cpp/mpmc_node.h>
#include <Cpp/Lock.h>

#define NS_TO_US 1000LL

DOTS_EXPORT(int64_t)
Time_GetTicksMicrosecondsMonotonic()
{
    static Baselib_Timer_TickToNanosecondConversionRatio conversion = Baselib_Timer_GetTicksToNanosecondsConversionRatio();
    return ((int64_t)Baselib_Timer_GetHighPrecisionTimerTicks() * conversion.ticksToNanosecondsNumerator / conversion.ticksToNanosecondsDenominator) / NS_TO_US;
}

DOTS_EXPORT(uint64_t)
Time_GetTicksToNanosecondsConversionRatio_Numerator()
{
    static Baselib_Timer_TickToNanosecondConversionRatio conversion = Baselib_Timer_GetTicksToNanosecondsConversionRatio();
    return conversion.ticksToNanosecondsNumerator;
}

DOTS_EXPORT(uint64_t)
Time_GetTicksToNanosecondsConversionRatio_Denominator()
{
    static Baselib_Timer_TickToNanosecondConversionRatio conversion = Baselib_Timer_GetTicksToNanosecondsConversionRatio();
    return conversion.ticksToNanosecondsDenominator;
}

static void* gTempSliceHandle = NULL;
static baselib::mpmc_node_queue<baselib::mpmc_node> safetyNodeQueue;
static baselib::Lock safetyHashLock;

#if ENABLE_UNITY_COLLECTIONS_CHECKS

DOTS_EXPORT(void) AtomicSafety_PushNode(void* node)
{
    safetyNodeQueue.push_back((baselib::mpmc_node*)node);
}

DOTS_EXPORT(void*) AtomicSafety_PopNode()
{
    return safetyNodeQueue.try_pop_front();
}

DOTS_EXPORT(void) AtomicSafety_LockSafetyHashTables()
{
    safetyHashLock.Acquire();
}

DOTS_EXPORT(void) AtomicSafety_UnlockSafetyHashTables()
{
    safetyHashLock.Release();
}

#endif
