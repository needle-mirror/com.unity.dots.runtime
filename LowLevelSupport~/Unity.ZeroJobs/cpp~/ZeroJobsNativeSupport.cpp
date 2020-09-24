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
