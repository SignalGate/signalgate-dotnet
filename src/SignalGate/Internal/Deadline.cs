using System;

namespace SignalGate.Internal;

// Clamps delays and deadlines to the range accepted by timers.
internal static class Deadline
{
    // The largest delay, in milliseconds, accepted by Task.Delay, CancelAfter and WaitAsync.
    internal const long MaxMs = 4_294_967_294;

    // Clamps ms to the range 0 to MaxMs.
    internal static long ClampMs(long ms)
    {
        if (ms < 0)
        {
            return 0;
        }

        return ms > MaxMs ? MaxMs : ms;
    }

    // Converts ms, clamped by ClampMs, to a TimeSpan.
    internal static TimeSpan ToTimeSpan(long ms)
    {
        return TimeSpan.FromTicks(ClampMs(ms) * TimeSpan.TicksPerMillisecond);
    }
}
