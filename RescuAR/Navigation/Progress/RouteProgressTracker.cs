using RescuAR.Diagnostics;
using RescuAR.Navigation.Models;
using System;

namespace RescuAR.Navigation.Progress;

/// <summary>
/// GPS-only route progress tracker for the current navigation milestone.
///
/// Responsibilities:
/// - snap a WGS84 GPS sample to the nearest plausible route segment;
/// - calculate cumulative route progress and remaining distance;
/// - reject clearly poor/off-route samples;
/// - prevent ordinary GPS jitter from moving progress backwards;
/// - request a new AR window only after enough forward movement.
///
/// This is intentionally NOT the final map-matching/PDR implementation.
/// Stronger map matching, dead reckoning, off-route rerouting, and turn-state
/// logic remain later milestones.
/// </summary>
public sealed class RouteProgressTracker
{
    private const string LogTag =
        "RescuAR-NavProgress";

    private const double EarthRadiusMeters =
        6371008.8;

    /*
     * Pedestrian GPS commonly wanders by several meters, especially indoors
     * or near buildings. Samples farther than this from the routed geometry
     * are retained for diagnostics but do not advance the AR route window.
     */
    private const double MaximumCrossTrackErrorMeters =
        35.0;

    /*
     * Reject very low-quality samples before they are allowed to move route
     * progress. A later fusion layer can make this adaptive.
     */
    private const double MaximumAcceptedAccuracyMeters =
        50.0;

    /*
     * Once progress has been established, prefer nearby route segments rather
     * than scanning the whole route. This reduces accidental jumps at road
     * crossings/parallel streets.
     */
    private const int SearchBackwardSegments =
        4;

    private const int SearchForwardSegments =
        24;

    /*
     * If the local search is clearly bad, perform one full-route fallback.
     * This lets the first useful GPS update recover after a larger movement.
     */
    private const double FullSearchFallbackCrossTrackMeters =
        25.0;

    /*
     * Ordinary GPS noise should not continually rebuild Evergine geometry.
     */
    private const double MinimumProgressAdvanceForPublishMeters =
        1.50;

    private readonly object sync =
        new();

    private RouteResult? route;

    private int lastSegmentIndex =
        -1;

    private bool hasProgress;

    private double committedProgressMeters;

    private bool hasPublishedWindow;

    private double lastPublishedProgressMeters;

    private ProgressSnapshot current =
        ProgressSnapshot.Unavailable;

    public ProgressSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public void SetRoute(
        RouteResult route)
    {
        ArgumentNullException.ThrowIfNull(
            route);

        if (route.Points.Count <
            2)
        {
            throw new ArgumentException(
                "Route progress tracking requires at least two route points.",
                nameof(route));
        }

        lock (sync)
        {
            this.route =
                route;

            lastSegmentIndex =
                -1;

            hasProgress =
                false;

            committedProgressMeters =
                0.0;

            /*
             * Force the first accepted GPS match to publish a moving window.
             * The initial MLD request already renders a route, but this first
             * progress publication rebases the short window near the current
             * AR camera/ground location.
             */
            hasPublishedWindow =
                false;

            lastPublishedProgressMeters =
                0.0;

            current =
                new ProgressSnapshot(
                    true,
                    false,
                    false,
                    -1,
                    0.0,
                    0.0,
                    Math.Max(
                        0.0,
                        route.TotalDistanceMeters),
                    double.NaN,
                    double.NaN,
                    default,
                    default);
        }

        AndroidLog.Debug(
            LogTag,
            "Route progress tracker initialized: " +
            $"points={route.Points.Count}, " +
            $"geometryDistance={route.Points[^1].DistanceFromStartMeters:F1} m, " +
            $"routeDistance={route.TotalDistanceMeters:F1} m");
    }

    public void Clear()
    {
        lock (sync)
        {
            route =
                null;

            lastSegmentIndex =
                -1;

            hasProgress =
                false;

            committedProgressMeters =
                0.0;

            hasPublishedWindow =
                false;

            lastPublishedProgressMeters =
                0.0;

            current =
                ProgressSnapshot.Unavailable;
        }

        AndroidLog.Debug(
            LogTag,
            "Route progress tracker cleared.");
    }

    /// <summary>
    /// Matches one GPS sample to the currently retained route.
    /// </summary>
    public RouteProgressUpdate Update(
        GeoCoordinate gpsCoordinate,
        double? accuracyMeters)
    {
        if (!gpsCoordinate.IsValid)
        {
            return Reject(
                "invalid GPS coordinate",
                gpsCoordinate,
                accuracyMeters);
        }

        RouteResult? currentRoute;
        int previousSegment;
        bool alreadyHasProgress;
        double previousCommittedProgress;
        bool alreadyPublished;
        double previousPublishedProgress;

        lock (sync)
        {
            currentRoute =
                route;

            previousSegment =
                lastSegmentIndex;

            alreadyHasProgress =
                hasProgress;

            previousCommittedProgress =
                committedProgressMeters;

            alreadyPublished =
                hasPublishedWindow;

            previousPublishedProgress =
                lastPublishedProgressMeters;
        }

        if (currentRoute is null ||
            currentRoute.Points.Count <
                2)
        {
            return Reject(
                "no active route",
                gpsCoordinate,
                accuracyMeters);
        }

        if (accuracyMeters.HasValue &&
            double.IsFinite(
                accuracyMeters.Value) &&
            accuracyMeters.Value >
                MaximumAcceptedAccuracyMeters)
        {
            return Reject(
                $"GPS accuracy {accuracyMeters.Value:F1} m exceeds " +
                $"{MaximumAcceptedAccuracyMeters:F1} m limit",
                gpsCoordinate,
                accuracyMeters);
        }

        SegmentMatch best;

        if (previousSegment >=
            0)
        {
            int startIndex =
                Math.Max(
                    0,
                    previousSegment -
                    SearchBackwardSegments);

            int endIndex =
                Math.Min(
                    currentRoute.Points.Count -
                        2,
                    previousSegment +
                    SearchForwardSegments);

            best =
                FindBestMatch(
                    currentRoute,
                    gpsCoordinate,
                    startIndex,
                    endIndex);

            if (!best.IsAvailable ||
                best.CrossTrackErrorMeters >
                    FullSearchFallbackCrossTrackMeters)
            {
                SegmentMatch fullSearch =
                    FindBestMatch(
                        currentRoute,
                        gpsCoordinate,
                        0,
                        currentRoute.Points.Count -
                            2);

                if (fullSearch.IsAvailable &&
                    (!best.IsAvailable ||
                     fullSearch.CrossTrackErrorMeters +
                         1.0 <
                     best.CrossTrackErrorMeters))
                {
                    best =
                        fullSearch;
                }
            }
        }
        else
        {
            best =
                FindBestMatch(
                    currentRoute,
                    gpsCoordinate,
                    0,
                    currentRoute.Points.Count -
                        2);
        }

        if (!best.IsAvailable)
        {
            return Reject(
                "no valid route segment match",
                gpsCoordinate,
                accuracyMeters);
        }

        if (best.CrossTrackErrorMeters >
            MaximumCrossTrackErrorMeters)
        {
            RouteProgressUpdate offRoute =
                new(
                    false,
                    true,
                    false,
                    best.SegmentIndex,
                    best.ProgressMeters,
                    alreadyHasProgress
                        ? previousCommittedProgress
                        : 0.0,
                    Math.Max(
                        0.0,
                        currentRoute.TotalDistanceMeters -
                        (alreadyHasProgress
                            ? previousCommittedProgress
                            : 0.0)),
                    best.CrossTrackErrorMeters,
                    accuracyMeters,
                    gpsCoordinate,
                    best.SnappedCoordinate,
                    $"cross-track error exceeds " +
                    $"{MaximumCrossTrackErrorMeters:F1} m");

            StoreSnapshot(
                currentRoute,
                offRoute);

            AndroidLog.Warn(
                LogTag,
                "GPS sample is off the current route window: " +
                $"segment={best.SegmentIndex}, " +
                $"rawProgress={best.ProgressMeters:F1} m, " +
                $"crossTrack={best.CrossTrackErrorMeters:F1} m, " +
                $"accuracy={FormatNullable(accuracyMeters)} m. " +
                "No reroute is performed in this milestone.");

            return offRoute;
        }

        double committedProgress =
            best.ProgressMeters;

        int committedSegment =
            best.SegmentIndex;

        GeoCoordinate committedCoordinate =
            best.SnappedCoordinate;

        /*
         * GPS jitter can produce a nearby point behind the previous sample.
         * Keep route progress monotonic for this first pedestrian milestone.
         * Intentional backtracking/recovery is a later map-matching concern.
         */
        if (alreadyHasProgress &&
            committedProgress <
                previousCommittedProgress)
        {
            committedProgress =
                previousCommittedProgress;

            committedSegment =
                previousSegment;

            committedCoordinate =
                GetCoordinateAtDistance(
                    currentRoute,
                    committedProgress);
        }

        double remainingMeters =
            Math.Max(
                0.0,
                currentRoute.TotalDistanceMeters -
                committedProgress);

        bool shouldPublish =
            !alreadyPublished ||
            committedProgress -
                previousPublishedProgress >=
                MinimumProgressAdvanceForPublishMeters;

        RouteProgressUpdate accepted =
            new(
                true,
                false,
                shouldPublish,
                committedSegment,
                best.ProgressMeters,
                committedProgress,
                remainingMeters,
                best.CrossTrackErrorMeters,
                accuracyMeters,
                gpsCoordinate,
                committedCoordinate,
                string.Empty);

        lock (sync)
        {
            hasProgress =
                true;

            committedProgressMeters =
                committedProgress;

            lastSegmentIndex =
                Math.Max(
                    0,
                    committedSegment);

            current =
                new ProgressSnapshot(
                    true,
                    true,
                    false,
                    committedSegment,
                    best.ProgressMeters,
                    committedProgress,
                    remainingMeters,
                    best.CrossTrackErrorMeters,
                    accuracyMeters ??
                        double.NaN,
                    gpsCoordinate,
                    committedCoordinate);
        }

        AndroidLog.Debug(
            LogTag,
            "GPS route match: " +
            $"segment={committedSegment}, " +
            $"rawProgress={best.ProgressMeters:F1} m, " +
            $"committedProgress={committedProgress:F1} m, " +
            $"remaining={remainingMeters:F1} m, " +
            $"crossTrack={best.CrossTrackErrorMeters:F1} m, " +
            $"accuracy={FormatNullable(accuracyMeters)} m, " +
            $"publishWindow={shouldPublish}");

        return accepted;
    }

    public void MarkWindowPublished(
        double progressMeters)
    {
        lock (sync)
        {
            hasPublishedWindow =
                true;

            lastPublishedProgressMeters =
                Math.Max(
                    0.0,
                    progressMeters);
        }

        AndroidLog.Debug(
            LogTag,
            $"AR moving window committed at progress={progressMeters:F1} m.");
    }

    private RouteProgressUpdate Reject(
        string reason,
        GeoCoordinate gpsCoordinate,
        double? accuracyMeters)
    {
        RouteResult? currentRoute;

        lock (sync)
        {
            currentRoute =
                route;
        }

        RouteProgressUpdate rejected =
            new(
                false,
                false,
                false,
                -1,
                0.0,
                Current.CommittedProgressMeters,
                currentRoute is null
                    ? 0.0
                    : Math.Max(
                        0.0,
                        currentRoute.TotalDistanceMeters -
                        Current.CommittedProgressMeters),
                double.NaN,
                accuracyMeters,
                gpsCoordinate,
                default,
                reason);

        AndroidLog.Warn(
            LogTag,
            "GPS route-progress sample rejected: " +
            $"{reason}; " +
            $"accuracy={FormatNullable(accuracyMeters)} m");

        return rejected;
    }

    private void StoreSnapshot(
        RouteResult route,
        RouteProgressUpdate update)
    {
        lock (sync)
        {
            current =
                new ProgressSnapshot(
                    true,
                    hasProgress,
                    update.IsOffRoute,
                    update.SegmentIndex,
                    update.RawProgressMeters,
                    update.CommittedProgressMeters,
                    update.RemainingMeters,
                    update.CrossTrackErrorMeters,
                    update.AccuracyMeters ??
                        double.NaN,
                    update.GpsCoordinate,
                    update.SnappedCoordinate);
        }
    }

    private static SegmentMatch FindBestMatch(
        RouteResult route,
        GeoCoordinate gpsCoordinate,
        int startSegmentIndex,
        int endSegmentIndex)
    {
        SegmentMatch best =
            SegmentMatch.Unavailable;

        for (int i = startSegmentIndex;
             i <=
             endSegmentIndex;
             i++)
        {
            RoutePoint start =
                route.Points[i];

            RoutePoint end =
                route.Points[i + 1];

            SegmentMatch candidate =
                ProjectOntoSegment(
                    gpsCoordinate,
                    start,
                    end,
                    i);

            if (!candidate.IsAvailable)
            {
                continue;
            }

            if (!best.IsAvailable ||
                candidate.CrossTrackErrorMeters <
                    best.CrossTrackErrorMeters)
            {
                best =
                    candidate;
            }
        }

        return best;
    }

    private static SegmentMatch ProjectOntoSegment(
        GeoCoordinate gpsCoordinate,
        RoutePoint start,
        RoutePoint end,
        int segmentIndex)
    {
        double referenceLatitudeRadians =
            DegreesToRadians(
                gpsCoordinate.Latitude);

        double cosReferenceLatitude =
            Math.Cos(
                referenceLatitudeRadians);

        /*
         * Express both segment endpoints in an East/North tangent plane whose
         * origin is the GPS sample. The GPS sample is therefore (0,0).
         */
        double startNorth =
            DegreesToRadians(
                start.Coordinate.Latitude -
                gpsCoordinate.Latitude) *
            EarthRadiusMeters;

        double startEast =
            DegreesToRadians(
                start.Coordinate.Longitude -
                gpsCoordinate.Longitude) *
            EarthRadiusMeters *
            cosReferenceLatitude;

        double endNorth =
            DegreesToRadians(
                end.Coordinate.Latitude -
                gpsCoordinate.Latitude) *
            EarthRadiusMeters;

        double endEast =
            DegreesToRadians(
                end.Coordinate.Longitude -
                gpsCoordinate.Longitude) *
            EarthRadiusMeters *
            cosReferenceLatitude;

        double deltaEast =
            endEast -
            startEast;

        double deltaNorth =
            endNorth -
            startNorth;

        double lengthSquared =
            deltaEast *
            deltaEast +
            deltaNorth *
            deltaNorth;

        if (!double.IsFinite(
                lengthSquared) ||
            lengthSquared <
                0.0001)
        {
            return SegmentMatch.Unavailable;
        }

        /*
         * Projection of the origin (GPS sample) onto A + t(B-A):
         *
         * t = dot(-A, B-A) / |B-A|^2
         */
        double t =
            -(
                startEast *
                    deltaEast +
                startNorth *
                    deltaNorth)
            /
            lengthSquared;

        t =
            Math.Clamp(
                t,
                0.0,
                1.0);

        double snappedEast =
            startEast +
            deltaEast *
            t;

        double snappedNorth =
            startNorth +
            deltaNorth *
            t;

        double crossTrack =
            Math.Sqrt(
                snappedEast *
                snappedEast +
                snappedNorth *
                snappedNorth);

        double distanceSpan =
            end.DistanceFromStartMeters -
            start.DistanceFromStartMeters;

        double progress =
            start.DistanceFromStartMeters +
            Math.Max(
                0.0,
                distanceSpan) *
            t;

        GeoCoordinate snapped =
            new(
                start.Coordinate.Latitude +
                    (end.Coordinate.Latitude -
                     start.Coordinate.Latitude) *
                    t,
                start.Coordinate.Longitude +
                    (end.Coordinate.Longitude -
                     start.Coordinate.Longitude) *
                    t);

        return new SegmentMatch(
            true,
            segmentIndex,
            t,
            progress,
            crossTrack,
            snapped);
    }

    private static GeoCoordinate GetCoordinateAtDistance(
        RouteResult route,
        double distanceMeters)
    {
        if (route.Points.Count ==
            0)
        {
            return default;
        }

        if (distanceMeters <=
            route.Points[0]
                .DistanceFromStartMeters)
        {
            return route.Points[0]
                .Coordinate;
        }

        for (int i = 1;
             i < route.Points.Count;
             i++)
        {
            RoutePoint end =
                route.Points[i];

            if (end.DistanceFromStartMeters <
                distanceMeters)
            {
                continue;
            }

            RoutePoint start =
                route.Points[i - 1];

            double span =
                end.DistanceFromStartMeters -
                start.DistanceFromStartMeters;

            if (span <=
                0.0001)
            {
                return end.Coordinate;
            }

            double t =
                Math.Clamp(
                    (distanceMeters -
                     start.DistanceFromStartMeters)
                    /
                    span,
                    0.0,
                    1.0);

            return new GeoCoordinate(
                start.Coordinate.Latitude +
                    (end.Coordinate.Latitude -
                     start.Coordinate.Latitude) *
                    t,
                start.Coordinate.Longitude +
                    (end.Coordinate.Longitude -
                     start.Coordinate.Longitude) *
                    t);
        }

        return route.Points[^1]
            .Coordinate;
    }

    private static string FormatNullable(
        double? value)
    {
        return value.HasValue &&
            double.IsFinite(
                value.Value)
            ? value.Value.ToString(
                "F1")
            : "<unknown>";
    }

    private static double DegreesToRadians(
        double degrees)
    {
        return degrees *
            Math.PI /
            180.0;
    }

    private readonly record struct SegmentMatch(
        bool IsAvailable,
        int SegmentIndex,
        double SegmentT,
        double ProgressMeters,
        double CrossTrackErrorMeters,
        GeoCoordinate SnappedCoordinate)
    {
        public static SegmentMatch Unavailable =>
            new(
                false,
                -1,
                0.0,
                0.0,
                double.PositiveInfinity,
                default);
    }

    public readonly record struct RouteProgressUpdate(
        bool IsAccepted,
        bool IsOffRoute,
        bool ShouldPublishWindow,
        int SegmentIndex,
        double RawProgressMeters,
        double CommittedProgressMeters,
        double RemainingMeters,
        double CrossTrackErrorMeters,
        double? AccuracyMeters,
        GeoCoordinate GpsCoordinate,
        GeoCoordinate SnappedCoordinate,
        string RejectionReason);

    public readonly record struct ProgressSnapshot(
        bool HasRoute,
        bool HasProgress,
        bool IsOffRoute,
        int SegmentIndex,
        double RawProgressMeters,
        double CommittedProgressMeters,
        double RemainingMeters,
        double CrossTrackErrorMeters,
        double AccuracyMeters,
        GeoCoordinate GpsCoordinate,
        GeoCoordinate SnappedCoordinate)
    {
        public static ProgressSnapshot Unavailable =>
            new(
                false,
                false,
                false,
                -1,
                0.0,
                0.0,
                0.0,
                double.NaN,
                double.NaN,
                default,
                default);
    }
}
