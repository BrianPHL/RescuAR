using RescuAR.Diagnostics;
using RescuAR.Navigation.Projection;
using System;
using System.Collections.Generic;
using System.Threading;

namespace RescuAR.AR;

/// <summary>
/// Thread-safe handoff for navigation route geometry.
/// </summary>
public static class ARRouteBridge
{
    private const string LogTag =
        "RescuAR-ARRoute";

    private static readonly object sync =
        new();

    private static RouteSnapshot current =
        RouteSnapshot.Unavailable;

    private static long version;

    private const float EquivalentPointPositionToleranceMeters =
        0.10f;

    private const double EquivalentProgressToleranceMeters =
        0.25;

    private const double EquivalentTotalDistanceToleranceMeters =
        0.50;

    private static int equivalentPublicationSuppressionCount;

    public static long Version =>
        Interlocked.Read(
            ref version);

    public static RouteSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public static void Publish(
        IReadOnlyList<ArHorizontalRoutePoint> points,
        string algorithm,
        double totalDistanceMeters)
    {
        ArgumentNullException.ThrowIfNull(
            points);

        string normalizedAlgorithm =
            algorithm ??
                string.Empty;

        RouteSnapshot next;
        int suppressedCount =
            0;

        lock (sync)
        {
            if (IsEquivalentPublication(
                    current,
                    points,
                    normalizedAlgorithm,
                    totalDistanceMeters))
            {
                equivalentPublicationSuppressionCount++;

                suppressedCount =
                    equivalentPublicationSuppressionCount;

                next =
                    current;
            }
            else
            {
                ArHorizontalRoutePoint[] copy =
                    new ArHorizontalRoutePoint[
                        points.Count];

                for (int i = 0;
                     i < points.Count;
                     i++)
                {
                    copy[i] =
                        points[i];
                }

                long nextVersion =
                    Interlocked.Increment(
                        ref version);

                next =
                    new RouteSnapshot(
                        nextVersion,
                        copy.Length >= 2,
                        normalizedAlgorithm,
                        totalDistanceMeters,
                        copy);

                equivalentPublicationSuppressionCount =
                    0;

                current =
                    next;
            }
        }

        if (suppressedCount > 0)
        {
            if (suppressedCount == 1 ||
                suppressedCount % 10 == 0)
            {
                AndroidLog.Debug(
                    LogTag,
                    "Equivalent route publication suppressed: " +
                    $"version={next.Version}, " +
                    $"consecutiveSuppressed={suppressedCount}, " +
                    $"points={points.Count}.");
            }

            return;
        }

        AndroidLog.Debug(
            LogTag,
            "Route bridge published: " +
            $"version={next.Version}, " +
            $"available={next.IsAvailable}, " +
            $"points={points.Count}, " +
            $"algorithm='{next.Algorithm}', " +
            $"totalDistance={totalDistanceMeters:F1} m");
    }

    private static bool IsEquivalentPublication(
        RouteSnapshot existing,
        IReadOnlyList<ArHorizontalRoutePoint> candidatePoints,
        string candidateAlgorithm,
        double candidateTotalDistanceMeters)
    {
        bool candidateAvailable =
            candidatePoints.Count >= 2;

        if (existing.Version < 0 ||
            existing.IsAvailable !=
                candidateAvailable ||
            !string.Equals(
                existing.Algorithm,
                candidateAlgorithm,
                StringComparison.Ordinal) ||
            existing.Points.Count !=
                candidatePoints.Count ||
            !double.IsFinite(
                existing.TotalDistanceMeters) ||
            !double.IsFinite(
                candidateTotalDistanceMeters) ||
            Math.Abs(
                existing.TotalDistanceMeters -
                candidateTotalDistanceMeters) >
                EquivalentTotalDistanceToleranceMeters)
        {
            return false;
        }

        for (int i = 0;
             i < candidatePoints.Count;
             i++)
        {
            ArHorizontalRoutePoint existingPoint =
                existing.Points[i];

            ArHorizontalRoutePoint candidatePoint =
                candidatePoints[i];

            if (!float.IsFinite(existingPoint.X) ||
                !float.IsFinite(existingPoint.Z) ||
                !float.IsFinite(candidatePoint.X) ||
                !float.IsFinite(candidatePoint.Z) ||
                !double.IsFinite(
                    existingPoint.DistanceFromWindowStartMeters) ||
                !double.IsFinite(
                    candidatePoint.DistanceFromWindowStartMeters) ||
                MathF.Abs(
                    existingPoint.X -
                    candidatePoint.X) >
                    EquivalentPointPositionToleranceMeters ||
                MathF.Abs(
                    existingPoint.Z -
                    candidatePoint.Z) >
                    EquivalentPointPositionToleranceMeters ||
                Math.Abs(
                    existingPoint.DistanceFromWindowStartMeters -
                    candidatePoint.DistanceFromWindowStartMeters) >
                    EquivalentProgressToleranceMeters)
            {
                return false;
            }
        }

        return true;
    }

    public static void Clear()
    {
        long nextVersion;

        lock (sync)
        {
            if (current.Version >= 0 &&
                !current.IsAvailable)
            {
                return;
            }

            nextVersion =
                Interlocked.Increment(
                    ref version);

            equivalentPublicationSuppressionCount =
                0;

            current =
                new RouteSnapshot(
                    nextVersion,
                    false,
                    string.Empty,
                    0.0,
                    []);
        }

        AndroidLog.Debug(
            LogTag,
            $"Route bridge cleared: version={nextVersion}");
    }

    public readonly struct RouteSnapshot
    {
        public static RouteSnapshot Unavailable =>
            new(
                -1,
                false,
                string.Empty,
                0.0,
                []);

        public RouteSnapshot(
            long version,
            bool isAvailable,
            string algorithm,
            double totalDistanceMeters,
            IReadOnlyList<ArHorizontalRoutePoint> points)
        {
            Version =
                version;

            IsAvailable =
                isAvailable;

            Algorithm =
                algorithm;

            TotalDistanceMeters =
                totalDistanceMeters;

            Points =
                points;
        }

        public long Version { get; }
        public bool IsAvailable { get; }
        public string Algorithm { get; }
        public double TotalDistanceMeters { get; }
        public IReadOnlyList<ArHorizontalRoutePoint> Points { get; }
    }
}
