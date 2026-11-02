using System;

namespace RescuAR.Navigation.Timing;

/// <summary>Process-local navigation time. Wall-clock corrections cannot age or
/// reorder live sensor evidence. Original provider UTC times remain diagnostic data.</summary>
public static class NavigationClock
{
    private static readonly long originMonotonic = Environment.TickCount64;
    private static readonly DateTimeOffset originUtc = DateTimeOffset.UtcNow;

    public static DateTimeOffset UtcNow => AtMonotonicMilliseconds(Environment.TickCount64);

    public static DateTimeOffset AtMonotonicMilliseconds(long milliseconds) =>
        originUtc.AddMilliseconds(milliseconds - originMonotonic);

    /// <summary>Convert an Android elapsed-realtime observation into the frame
    /// clock using a simultaneous clock pair, including time spent in deep sleep.</summary>
    public static bool TryMapElapsedNanoseconds(long acquiredNanoseconds, long elapsedNowNanoseconds,
        long monotonicNowMilliseconds, out long acquiredMilliseconds)
    {
        acquiredMilliseconds = 0;
        if (acquiredNanoseconds <= 0 || elapsedNowNanoseconds < acquiredNanoseconds ||
            monotonicNowMilliseconds < 0) return false;
        long ageMilliseconds = (elapsedNowNanoseconds - acquiredNanoseconds) / 1_000_000;
        if (ageMilliseconds > monotonicNowMilliseconds) return false;
        acquiredMilliseconds = monotonicNowMilliseconds - ageMilliseconds;
        return true;
    }
}
