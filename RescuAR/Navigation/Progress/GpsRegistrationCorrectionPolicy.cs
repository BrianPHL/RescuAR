using System;
using System.Collections.Generic;
using System.Numerics;

namespace RescuAR.Navigation.Progress;

/// <summary>Filters actual GPS/pose registration residuals, never device locations.
/// Correlated observations do not reduce the reported GPS uncertainty.</summary>
public sealed class GpsRegistrationCorrectionPolicy
{
    private readonly Queue<(Vector2 Residual, double Accuracy, DateTimeOffset At)> samples = new();
    private DateTimeOffset? lastAt;
    private Vector2 applied;

    public void Reset() { ClearEvidence(); lastAt = null; applied = default; }
    public void ClearEvidence() => samples.Clear();

    public bool Observe(Vector2 residual, double seedAccuracy, double accuracy, DateTimeOffset at,
        out Vector2 correction)
    {
        correction = default;
        if (!float.IsFinite(residual.X + residual.Y) || !double.IsFinite(seedAccuracy + accuracy) ||
            seedAccuracy <= 0 || accuracy <= 0 || accuracy > 20 ||
            lastAt is { } previous && at <= previous) return false;
        lastAt = at;
        // A single jump outside overlapping uncertainty cannot vote to move the origin.
        if (residual.Length() > seedAccuracy + accuracy + RoutePositionObservationPolicy.MotionAllowanceMeters)
            return false;
        while (samples.TryPeek(out var first) && at - first.At > TimeSpan.FromSeconds(30)) samples.Dequeue();
        samples.Enqueue((residual, accuracy, at));
        while (samples.Count > 3) samples.Dequeue();
        if (samples.Count < 3 || at - samples.Peek().At < TimeSpan.FromSeconds(8) ||
            accuracy > seedAccuracy * .85) return false;

        Vector2 mean = default;
        double weights = 0;
        foreach (var sample in samples)
        {
            double weight = 1 / Math.Pow(Math.Max(3, sample.Accuracy), 2);
            mean += sample.Residual * (float)weight;
            weights += weight;
        }
        mean /= (float)weights;
        double scatter = 0;
        foreach (var sample in samples) scatter += Vector2.DistanceSquared(sample.Residual, mean);
        if (Math.Sqrt(scatter / samples.Count) > 3 || mean.Length() <= 2) return false;

        double gain = seedAccuracy * seedAccuracy / (seedAccuracy * seedAccuracy + accuracy * accuracy);
        correction = mean * (float)Math.Min(gain, 2 / mean.Length());
        // Keep all filtered corrections inside the seed's stated uncertainty.
        if ((applied + correction).Length() > seedAccuracy) { correction = default; return false; }
        applied += correction;
        ClearEvidence();
        return true;
    }
}
