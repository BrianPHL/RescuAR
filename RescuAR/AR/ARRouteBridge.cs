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

    private static RouteGeometryQualitySnapshot currentGeometryQuality =
        RouteGeometryQualitySnapshot.Unavailable;

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

    public static RouteGeometryQualitySnapshot GeometryQuality
    {
        get
        {
            lock (sync)
            {
                return currentGeometryQuality;
            }
        }
    }

    public static void Publish(
        IReadOnlyList<ArHorizontalRoutePoint> points,
        string algorithm,
        double totalDistanceMeters,
        RouteVisualKind visualKind =
            RouteVisualKind.Unavailable,
        double windowStartProgressMeters =
            double.NaN,
        int sourceSegmentIndex =
            -1)
    {
        ArgumentNullException.ThrowIfNull(
            points);

        string normalizedAlgorithm =
            algorithm ??
                string.Empty;

        RouteNavigationState navigationState =
            RouteNavigationState.Create(
                visualKind,
                windowStartProgressMeters,
                sourceSegmentIndex);

        ARRenderGenerationToken generation =
            ARRenderGenerationBridge.Current;

        RouteSnapshot next;
        int suppressedCount =
            0;

        lock (sync)
        {
            if (IsEquivalentPublication(
                    current,
                    points,
                    normalizedAlgorithm,
                    totalDistanceMeters,
                    navigationState))
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
                        generation,
                        copy.Length >= 2,
                        normalizedAlgorithm,
                        totalDistanceMeters,
                        copy,
                        navigationState);

                equivalentPublicationSuppressionCount =
                    0;

                current =
                    next;

                currentGeometryQuality =
                    next.IsAvailable
                        ? RouteGeometryQualitySnapshot.Pending(
                            next.Version,
                            next.Generation,
                            copy.Length)
                        : RouteGeometryQualitySnapshot.Unavailable;
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
            $"totalDistance={totalDistanceMeters:F1} m, " +
            $"visualKind={next.NavigationState.VisualKind}, " +
            $"windowProgress=" +
            $"{(next.NavigationState.IsAvailable ? next.NavigationState.WindowStartProgressMeters.ToString("F1") : "<none>")} m, " +
            $"sourceSegment={next.NavigationState.SourceSegmentIndex}");
    }

    internal static void PublishGeometryQuality(
        RouteSnapshot route,
        ARRouteGeometrySanitizer.GeometryPreparationResult prepared,
        int renderedSegmentCount,
        int segmentCapacity)
    {
        RouteGeometryQualitySnapshot next;

        lock (sync)
        {
            if (route.Version !=
                    current.Version ||
                route.Generation.SessionGeneration !=
                    current.Generation.SessionGeneration ||
                !ARRenderGenerationBridge.IsCurrentSession(
                    route.Generation))
            {
                return;
            }

            RouteGeometryQualityState state =
                renderedSegmentCount <=
                    0
                    ? RouteGeometryQualityState.Rejected
                    : prepared.WasCapacityResampled
                        ? RouteGeometryQualityState.CapacityResampled
                        : RouteGeometryQualityState.WithinCapacity;

            next =
                new RouteGeometryQualitySnapshot(
                    route.Version,
                    route.Generation,
                    state,
                    prepared.InputPointCount,
                    prepared.DetailedPointCount,
                    prepared.Points.Count,
                    renderedSegmentCount,
                    segmentCapacity,
                    prepared.RemovedPointCount,
                    prepared.BeveledCornerCount,
                    prepared.InsertedSubdivisionPointCount,
                    prepared.SourceLengthMeters,
                    prepared.RenderedLengthMeters,
                    prepared.MaximumRenderedSegmentLengthMeters,
                    prepared.FirstPointPreserved &&
                        prepared.FinalPointPreserved);

            if (next ==
                currentGeometryQuality)
            {
                return;
            }

            currentGeometryQuality =
                next;
        }

        string message =
            "AR_ROUTE_GEOMETRY_QUALITY: " +
            $"routeVersion={next.RouteVersion}, " +
            $"state={next.State}, " +
            $"inputPoints={next.InputPointCount}, " +
            $"detailedPoints={next.DetailedPointCount}, " +
            $"renderedPoints={next.RenderedPointCount}, " +
            $"renderedSegments={next.RenderedSegmentCount}/" +
            $"{next.SegmentCapacity}, " +
            $"removedPoints={next.RemovedPointCount}, " +
            $"beveledCorners={next.BeveledCornerCount}, " +
            $"subdivisionPoints={next.InsertedSubdivisionPointCount}, " +
            $"sourceLength={next.SourceLengthMeters:F2}m, " +
            $"renderedLength={next.RenderedLengthMeters:F2}m, " +
            $"maximumSegment={next.MaximumRenderedSegmentLengthMeters:F2}m, " +
            $"endpointPreserved={next.EndpointPreserved}.";

        if (next.State ==
            RouteGeometryQualityState.CapacityResampled ||
            next.State ==
                RouteGeometryQualityState.Rejected)
        {
            AndroidLog.Warn(
                LogTag,
                message);
        }
        else
        {
            AndroidLog.Debug(
                LogTag,
                message);
        }
    }

    private static bool IsEquivalentPublication(
        RouteSnapshot existing,
        IReadOnlyList<ArHorizontalRoutePoint> candidatePoints,
        string candidateAlgorithm,
        double candidateTotalDistanceMeters,
        RouteNavigationState candidateNavigationState)
    {
        bool candidateAvailable =
            candidatePoints.Count >= 2;

        if (existing.Version < 0 ||
            existing.Generation.SessionGeneration !=
                ARRenderGenerationBridge.Current.SessionGeneration ||
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
                EquivalentTotalDistanceToleranceMeters ||
            !AreEquivalentNavigationStates(
                existing.NavigationState,
                candidateNavigationState))
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

    private static bool AreEquivalentNavigationStates(
        RouteNavigationState existing,
        RouteNavigationState candidate)
    {
        if (existing.VisualKind !=
                candidate.VisualKind ||
            existing.IsAvailable !=
                candidate.IsAvailable)
        {
            return false;
        }

        if (!existing.IsAvailable)
        {
            return true;
        }

        return existing.SourceSegmentIndex ==
                candidate.SourceSegmentIndex &&
            Math.Abs(
                existing.WindowStartProgressMeters -
                candidate.WindowStartProgressMeters) <=
                    EquivalentProgressToleranceMeters;
    }

    public static void Clear()
    {
        long nextVersion;

        lock (sync)
        {
            if (current.Version >= 0 &&
                !current.IsAvailable)
            {
                currentGeometryQuality =
                    RouteGeometryQualitySnapshot.Unavailable;

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
                    ARRenderGenerationBridge.Current,
                    false,
                    string.Empty,
                    0.0,
                    [],
                    RouteNavigationState.Unavailable);

            currentGeometryQuality =
                RouteGeometryQualitySnapshot.Unavailable;
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
                ARRenderGenerationToken.Invalid,
                false,
                string.Empty,
                0.0,
                [],
                RouteNavigationState.Unavailable);

        public RouteSnapshot(
            long version,
            ARRenderGenerationToken generation,
            bool isAvailable,
            string algorithm,
            double totalDistanceMeters,
            IReadOnlyList<ArHorizontalRoutePoint> points,
            RouteNavigationState navigationState)
        {
            Version =
                version;

            Generation =
                generation;

            IsAvailable =
                isAvailable;

            Algorithm =
                algorithm;

            TotalDistanceMeters =
                totalDistanceMeters;

            Points =
                points;

            NavigationState =
                navigationState;
        }

        public long Version { get; }
        public ARRenderGenerationToken Generation { get; }
        public bool IsAvailable { get; }
        public string Algorithm { get; }
        public double TotalDistanceMeters { get; }
        public IReadOnlyList<ArHorizontalRoutePoint> Points { get; }
        public RouteNavigationState NavigationState { get; }
    }

    public readonly record struct RouteGeometryQualitySnapshot(
        long RouteVersion,
        ARRenderGenerationToken Generation,
        RouteGeometryQualityState State,
        int InputPointCount,
        int DetailedPointCount,
        int RenderedPointCount,
        int RenderedSegmentCount,
        int SegmentCapacity,
        int RemovedPointCount,
        int BeveledCornerCount,
        int InsertedSubdivisionPointCount,
        float SourceLengthMeters,
        float RenderedLengthMeters,
        float MaximumRenderedSegmentLengthMeters,
        bool EndpointPreserved)
    {
        public static RouteGeometryQualitySnapshot Unavailable =>
            new(
                -1,
                ARRenderGenerationToken.Invalid,
                RouteGeometryQualityState.Unavailable,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0.0f,
                0.0f,
                0.0f,
                false);

        internal static RouteGeometryQualitySnapshot Pending(
            long routeVersion,
            ARRenderGenerationToken generation,
            int inputPointCount) =>
            Unavailable with
            {
                RouteVersion = routeVersion,
                Generation = generation,
                State = RouteGeometryQualityState.Pending,
                InputPointCount = inputPointCount
            };

        public bool IsCapacityLimitedFor(
            RouteSnapshot route) =>
            State ==
                RouteGeometryQualityState.CapacityResampled &&
            EndpointPreserved &&
            IsCurrentFor(
                route);

        public bool IsCurrentFor(
            RouteSnapshot route) =>
            RouteVersion ==
                route.Version &&
            Generation.SessionGeneration ==
                route.Generation.SessionGeneration &&
            ARRenderGenerationBridge.IsCurrentSession(
                Generation);
    }
}

public enum RouteGeometryQualityState
{
    Unavailable,
    Pending,
    WithinCapacity,
    CapacityResampled,
    Rejected
}

public enum RouteVisualKind
{
    Unavailable,
    RouteWindow,
    ApproachConnector
}

public readonly record struct RouteNavigationState(
    bool IsAvailable,
    RouteVisualKind VisualKind,
    double WindowStartProgressMeters,
    int SourceSegmentIndex)
{
    public static RouteNavigationState Unavailable =>
        new(
            false,
            RouteVisualKind.Unavailable,
            double.NaN,
            -1);

    public static RouteNavigationState Create(
        RouteVisualKind visualKind,
        double windowStartProgressMeters,
        int sourceSegmentIndex)
    {
        bool available =
            visualKind ==
                RouteVisualKind.RouteWindow &&
            double.IsFinite(
                windowStartProgressMeters) &&
            sourceSegmentIndex >=
                0;

        return new RouteNavigationState(
            available,
            visualKind,
            available
                ? Math.Max(
                    0.0,
                    windowStartProgressMeters)
                : double.NaN,
            available
                ? sourceSegmentIndex
                : -1);
    }
}
