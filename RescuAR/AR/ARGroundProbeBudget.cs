using System;

namespace RescuAR.AR;

/// <summary>Rate limit owned by the serialized ground-probe loop; renews during retained sessions.</summary>
public sealed class ARGroundProbeBudget
{
    public const int Capacity = 600;
    public const long WindowMilliseconds = 300_000;
    private long? windowStartedAt;
    public int Used { get; private set; }

    public bool HasCapacity(long now)
    {
        if (!windowStartedAt.HasValue || now < windowStartedAt.Value ||
            now - windowStartedAt.Value >= WindowMilliseconds)
        {
            windowStartedAt = now;
            Used = 0;
        }
        return Used < Capacity;
    }

    public bool TryConsume(long now)
    {
        if (!HasCapacity(now)) return false;
        Used++;
        return true;
    }

    public void Reset()
    {
        windowStartedAt = null;
        Used = 0;
    }
}
