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

        RouteSnapshot next =
            new(
                nextVersion,
                copy.Length >= 2,
                algorithm ??
                    string.Empty,
                totalDistanceMeters,
                copy);

        lock (sync)
        {
            current =
                next;
        }

        AndroidLog.Debug(
            LogTag,
            "Route bridge published: " +
            $"version={nextVersion}, " +
            $"available={next.IsAvailable}, " +
            $"points={copy.Length}, " +
            $"algorithm='{next.Algorithm}', " +
            $"totalDistance={totalDistanceMeters:F1} m");
    }

    public static void Clear()
    {
        long nextVersion =
            Interlocked.Increment(
                ref version);

        lock (sync)
        {
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
