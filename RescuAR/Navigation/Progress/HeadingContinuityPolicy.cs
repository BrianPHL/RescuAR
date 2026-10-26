using System;

namespace RescuAR.Navigation.Progress;

/// <summary>Independent orientation can withhold trust, never supply a replacement yaw.</summary>
public sealed class HeadingContinuityPolicy
{
    private DateTimeOffset? firstAt, lastAt;
    private long session, epoch, world, revision;
    private double firstYaw;
    private int count;
    public enum Verdict { Pending, SupportsRetained, Contradicted }

    public Verdict Observe(double retainedYaw, double observedYaw, DateTimeOffset at,
        long currentSession, long currentEpoch, long currentWorld, long calibrationRevision)
    {
        if (!double.IsFinite(retainedYaw) || !double.IsFinite(observedYaw)) return Verdict.Pending;
        if (session != currentSession || epoch != currentEpoch || world != currentWorld || revision != calibrationRevision ||
            lastAt.HasValue && (at - lastAt > TimeSpan.FromSeconds(3) || at < lastAt))
        {
            count = 0; firstAt = lastAt = null;
            session = currentSession; epoch = currentEpoch; world = currentWorld; revision = calibrationRevision;
        }
        if (lastAt.HasValue && at - lastAt < TimeSpan.FromSeconds(1)) return Verdict.Pending;
        if (count > 0 && Error(firstYaw, observedYaw) > 12) { count = 0; firstAt = null; }
        if (count == 0) { firstYaw = observedYaw; firstAt = at; }
        lastAt = at;
        count++;
        if (count < 3 || at - firstAt < TimeSpan.FromSeconds(2)) return Verdict.Pending;
        double error = Error(retainedYaw, observedYaw);
        // Even 15 degrees projects about 2 m sideways over a 7.5 m local
        // window. Repeated moderate disagreement must not remain trusted.
        return error <= 8 ? Verdict.SupportsRetained : error >= 15 ? Verdict.Contradicted : Verdict.Pending;
    }

    private static double Error(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 360));
}
