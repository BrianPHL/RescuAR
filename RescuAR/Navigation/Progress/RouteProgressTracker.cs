using RescuAR.Diagnostics;
using RescuAR.Navigation.Models;
using System;

namespace RescuAR.Navigation.Progress;

/// <summary>
/// Route progress tracker for GPS + capstone-sized PDR map matching.
///
/// Responsibilities:
/// - snap WGS84 GPS samples to the nearest plausible route segment;
/// - calculate cumulative route progress and remaining distance;
/// - reject clearly poor/off-route GPS samples;
/// - prevent ordinary GPS jitter from moving progress backwards;
/// - accept conservative along-route PDR distance increments between GPS fixes;
/// - request a new AR window only after enough forward movement.
///
/// PDR does not estimate a free 2D geographic position. It advances only along
/// the already-selected route. Good GPS samples remain the global correction
/// source while ARCore remains the rendering/spatial authority.
/// </summary>
public sealed class RouteProgressTracker
{
    private const string LogTag =
        "RescuAR-NavProgress";

    private const double EarthRadiusMeters =
        6371008.8;

    /*
     * Reject very low-quality samples before they are allowed to move route
     * progress. Indoor test mode deliberately relaxes this so a nearby road
     * can still be used while GPS is degraded by the building.
     */
    private const double NormalMaximumAcceptedAccuracyMeters =
        50.0;

    private const double IndoorMaximumAcceptedAccuracyMeters =
        120.0;

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
    private const double NormalFullSearchFallbackCrossTrackMeters =
        25.0;

    private const double IndoorFullSearchFallbackCrossTrackMeters =
        80.0;

    /*
     * Ordinary GPS noise should not continually rebuild Evergine geometry.
     */
    private const double MinimumProgressAdvanceForPublishMeters =
        1.50;

    private readonly object sync =
        new();

    private readonly bool indoorTestMode;

    private double MaximumAcceptedAccuracyMeters =>
        indoorTestMode
            ? IndoorMaximumAcceptedAccuracyMeters
            : NormalMaximumAcceptedAccuracyMeters;

    private double FullSearchFallbackCrossTrackMeters =>
        indoorTestMode
            ? IndoorFullSearchFallbackCrossTrackMeters
            : NormalFullSearchFallbackCrossTrackMeters;

    private RouteResult? route;

    private int lastSegmentIndex =
        -1;

    private bool hasProgress;

    private double committedProgressMeters;

    private bool hasPublishedWindow;

    private double lastPublishedProgressMeters;

    private ProgressSnapshot current =
        ProgressSnapshot.Unavailable;

    public RouteProgressTracker(
        bool indoorTestMode = false)
    {
        this.indoorTestMode =
            indoorTestMode;

        AndroidLog.Debug(
            LogTag,
            indoorTestMode
                ? "RouteProgressTracker created in INDOOR TEST MODE: " +
                  $"accuracyLimit={IndoorMaximumAcceptedAccuracyMeters:F0} m, " +
                  "corridorRadius=100 m."
                : "RouteProgressTracker created in normal GPS mode with an " +
                  "accuracy-aware pedestrian route corridor.");
    }

    public bool IndoorTestMode =>
        indoorTestMode;

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
        double? accuracyMeters,
        double? courseDegrees = null,
        double? speedMetersPerSecond = null)
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
                    endIndex,
                    alreadyHasProgress,
                    previousCommittedProgress,
                    previousSegment,
                    courseDegrees,
                    speedMetersPerSecond);

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
                            2,
                        alreadyHasProgress,
                        previousCommittedProgress,
                        previousSegment,
                        courseDegrees,
                        speedMetersPerSecond);

                if (fullSearch.IsAvailable &&
                    (!best.IsAvailable ||
                     fullSearch.MatchScore +
                         1.0 <
                     best.MatchScore))
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
                        2,
                    alreadyHasProgress,
                    previousCommittedProgress,
                    previousSegment,
                    courseDegrees,
                    speedMetersPerSecond);
        }

        if (!best.IsAvailable)
        {
            return Reject(
                "no valid route segment match",
                gpsCoordinate,
                accuracyMeters);
        }

        double corridorRadiusMeters =
            RouteCorridorPolicy.GetCorridorRadiusMeters(
                accuracyMeters,
                indoorTestMode);

        RouteMatchConfidence matchConfidence =
            RouteCorridorPolicy.ClassifyMatch(
                accuracyMeters,
                best.CourseAlignmentErrorDegrees,
                best.MatchScoreGap);

        if (best.CrossTrackErrorMeters >
            corridorRadiusMeters)
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
                    $"the {corridorRadiusMeters:F1} m accuracy-aware corridor",
                    corridorRadiusMeters,
                    matchConfidence,
                    best.CourseAlignmentErrorDegrees);

            StoreSnapshot(
                currentRoute,
                offRoute);

            AndroidLog.Warn(
                LogTag,
                "GPS sample is off the current route window: " +
                $"segment={best.SegmentIndex}, " +
                $"rawProgress={best.ProgressMeters:F1} m, " +
                $"crossTrack={best.CrossTrackErrorMeters:F1} m, " +
                $"corridor={corridorRadiusMeters:F1} m, " +
                $"matchConfidence={matchConfidence}, " +
                $"courseError={FormatFinite(best.CourseAlignmentErrorDegrees)} deg, " +
                $"accuracy={FormatNullable(accuracyMeters)} m. " +
                "The reroute policy will require repeated trustworthy confirmation.");

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
                string.Empty,
                corridorRadiusMeters,
                matchConfidence,
                best.CourseAlignmentErrorDegrees);

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
            $"corridor={corridorRadiusMeters:F1} m, " +
            $"matchConfidence={matchConfidence}, " +
            $"courseError={FormatFinite(best.CourseAlignmentErrorDegrees)} deg, " +
            $"accuracy={FormatNullable(accuracyMeters)} m, " +
            $"publishWindow={shouldPublish}");

        return accepted;
    }

    /// <summary>
    /// Applies a GPS/PDR fusion target after a normal route-matched GPS Update.
    ///
    /// Update(...) deliberately remains responsible for GPS-to-route matching.
    /// The fusion policy can then bound a large forward jump or, after repeated
    /// high-confidence evidence, apply a small controlled backward correction.
    ///
    /// The returned update is the authoritative value CameraPage should use
    /// for AR-window publication.
    /// </summary>
    public RouteProgressUpdate ApplyFusionCorrection(
        double targetProgressMeters,
        RouteProgressUpdate gpsSource,
        string fusionReason)
    {
        RouteResult? currentRoute;
        bool alreadyPublished;
        double previousPublishedProgress;

        lock (sync)
        {
            currentRoute =
                route;

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
                "no active route for GPS/PDR fusion correction",
                gpsSource.GpsCoordinate,
                gpsSource.AccuracyMeters);
        }

        if (!double.IsFinite(
                targetProgressMeters))
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetProgressMeters));
        }

        double geometryEnd =
            currentRoute.Points[^1]
                .DistanceFromStartMeters;

        double correctedProgress =
            Math.Clamp(
                targetProgressMeters,
                currentRoute.Points[0]
                    .DistanceFromStartMeters,
                geometryEnd);

        int correctedSegment =
            FindSegmentIndexAtDistance(
                currentRoute,
                correctedProgress);

        GeoCoordinate correctedCoordinate =
            GetCoordinateAtDistance(
                currentRoute,
                correctedProgress);

        double remaining =
            Math.Max(
                0.0,
                currentRoute.TotalDistanceMeters -
                    correctedProgress);

        /*
         * Fusion corrections can be forward OR backward. Republish only after
         * the corrected progress differs from the currently visible window by
         * the same 1.5 m threshold already used by normal progress.
         */
        bool shouldPublish =
            !alreadyPublished ||
            Math.Abs(
                correctedProgress -
                previousPublishedProgress) >=
                    MinimumProgressAdvanceForPublishMeters;

        RouteProgressUpdate corrected =
            new(
                true,
                false,
                shouldPublish,
                correctedSegment,
                gpsSource.RawProgressMeters,
                correctedProgress,
                remaining,
                gpsSource.CrossTrackErrorMeters,
                gpsSource.AccuracyMeters,
                gpsSource.GpsCoordinate,
                correctedCoordinate,
                string.IsNullOrWhiteSpace(
                    fusionReason)
                    ? "GPS_PDR_FUSION"
                    : fusionReason,
                gpsSource.CorridorRadiusMeters,
                gpsSource.MatchConfidence,
                gpsSource.CourseAlignmentErrorDegrees);

        lock (sync)
        {
            hasProgress =
                true;

            committedProgressMeters =
                correctedProgress;

            lastSegmentIndex =
                Math.Max(
                    0,
                    correctedSegment);

            current =
                new ProgressSnapshot(
                    true,
                    true,
                    false,
                    correctedSegment,
                    gpsSource.RawProgressMeters,
                    correctedProgress,
                    remaining,
                    gpsSource.CrossTrackErrorMeters,
                    gpsSource.AccuracyMeters ??
                        double.NaN,
                    gpsSource.GpsCoordinate,
                    correctedCoordinate);
        }

        AndroidLog.Debug(
            LogTag,
            "GPS/PDR fusion correction applied: " +
            $"rawGpsProgress={gpsSource.RawProgressMeters:F1} m, " +
            $"correctedProgress={correctedProgress:F1} m, " +
            $"remaining={remaining:F1} m, " +
            $"publishWindow={shouldPublish}, " +
            $"reason='{fusionReason}'");

        return corrected;
    }

    /// <summary>
    /// Advances committed progress along the already-selected route using one
    /// directionally accepted PDR step/distance increment.
    ///
    /// This deliberately does not create a free-running latitude/longitude
    /// estimate. The route geometry itself is the map-matching constraint.
    /// GPS Update(...) can subsequently correct progress forward when a good
    /// geographic sample is available.
    /// </summary>
    public RouteProgressUpdate AdvanceDeadReckoning(
        double advanceMeters)
    {
        if (!double.IsFinite(
                advanceMeters) ||
            advanceMeters <=
                0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(advanceMeters));
        }

        RouteResult? currentRoute;
        double currentProgress;
        bool progressExists;
        bool alreadyPublished;
        double previousPublishedProgress;

        lock (sync)
        {
            currentRoute =
                route;

            currentProgress =
                committedProgressMeters;

            progressExists =
                hasProgress;

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
                "no active route for PDR progress",
                default,
                null);
        }

        if (!progressExists)
        {
            currentProgress =
                currentRoute.Points[0]
                    .DistanceFromStartMeters;
        }

        double geometryEnd =
            currentRoute.Points[^1]
                .DistanceFromStartMeters;

        double nextProgress =
            Math.Min(
                geometryEnd,
                currentProgress +
                    advanceMeters);

        /*
         * If geometry is already exhausted, accept no phantom extra travel.
         */
        if (nextProgress <=
            currentProgress +
                0.0001)
        {
            return new RouteProgressUpdate(
                false,
                false,
                false,
                FindSegmentIndexAtDistance(
                    currentRoute,
                    currentProgress),
                currentProgress,
                currentProgress,
                Math.Max(
                    0.0,
                    currentRoute.TotalDistanceMeters -
                        currentProgress),
                0.0,
                null,
                GetCoordinateAtDistance(
                    currentRoute,
                    currentProgress),
                GetCoordinateAtDistance(
                    currentRoute,
                    currentProgress),
                "PDR reached route geometry end",
                RouteCorridorPolicy.MinimumCorridorRadiusMeters,
                RouteMatchConfidence.Unavailable,
                double.NaN);
        }

        GeoCoordinate snappedCoordinate =
            GetCoordinateAtDistance(
                currentRoute,
                nextProgress);

        int segmentIndex =
            FindSegmentIndexAtDistance(
                currentRoute,
                nextProgress);

        double remaining =
            Math.Max(
                0.0,
                currentRoute.TotalDistanceMeters -
                    nextProgress);

        /*
         * The initial MLD window is already visible. PDR therefore waits until
         * at least the normal publication threshold has accumulated before its
         * first geometry refresh instead of rebuilding after the first step.
         */
        bool shouldPublish =
            alreadyPublished
                ? nextProgress -
                    previousPublishedProgress >=
                        MinimumProgressAdvanceForPublishMeters
                : nextProgress -
                    currentRoute.Points[0]
                        .DistanceFromStartMeters >=
                        MinimumProgressAdvanceForPublishMeters;

        RouteProgressUpdate pdr =
            new(
                true,
                false,
                shouldPublish,
                segmentIndex,
                nextProgress,
                nextProgress,
                remaining,
                0.0,
                null,
                snappedCoordinate,
                snappedCoordinate,
                "PDR_STEP",
                RouteCorridorPolicy.MinimumCorridorRadiusMeters,
                RouteMatchConfidence.Unavailable,
                double.NaN);

        lock (sync)
        {
            hasProgress =
                true;

            committedProgressMeters =
                nextProgress;

            lastSegmentIndex =
                Math.Max(
                    0,
                    segmentIndex);

            current =
                new ProgressSnapshot(
                    true,
                    true,
                    false,
                    segmentIndex,
                    nextProgress,
                    nextProgress,
                    remaining,
                    0.0,
                    double.NaN,
                    snappedCoordinate,
                    snappedCoordinate);
        }

        AndroidLog.Debug(
            "RescuAR-PDR",
            "PDR along-route progress accepted: " +
            $"advance={advanceMeters:F2} m, " +
            $"progress={nextProgress:F1} m, " +
            $"remaining={remaining:F1} m, " +
            $"segment={segmentIndex}, " +
            $"publishWindow={shouldPublish}");

        return pdr;
    }

    /// <summary>
    /// TEST-ONLY progress advance used by CameraPage's Indoor Test Mode.
    /// Production/outdoor navigation must not call this method.
    /// </summary>
    public RouteProgressUpdate AdvanceSynthetic(
        double advanceMeters)
    {
        if (!indoorTestMode)
        {
            return Reject(
                "synthetic progress is disabled outside Indoor Test Mode",
                default,
                null);
        }

        if (advanceMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(advanceMeters));
        }

        RouteResult? currentRoute;
        double currentProgress;
        bool progressExists;

        lock (sync)
        {
            currentRoute = route;
            currentProgress = committedProgressMeters;
            progressExists = hasProgress;
        }

        if (currentRoute is null ||
            currentRoute.Points.Count < 2)
        {
            return Reject(
                "no active route for synthetic progress",
                default,
                null);
        }

        if (!progressExists)
        {
            currentProgress =
                currentRoute.Points[0]
                    .DistanceFromStartMeters;
        }

        double geometryEnd =
            currentRoute.Points[^1]
                .DistanceFromStartMeters;

        double nextProgress =
            Math.Min(
                geometryEnd,
                currentProgress + advanceMeters);

        GeoCoordinate snappedCoordinate =
            GetCoordinateAtDistance(
                currentRoute,
                nextProgress);

        int segmentIndex =
            FindSegmentIndexAtDistance(
                currentRoute,
                nextProgress);

        double remaining =
            Math.Max(
                0.0,
                currentRoute.TotalDistanceMeters - nextProgress);

        RouteProgressUpdate synthetic =
            new(
                true,
                false,
                true,
                segmentIndex,
                nextProgress,
                nextProgress,
                remaining,
                0.0,
                null,
                snappedCoordinate,
                snappedCoordinate,
                "INDOOR_TEST_SYNTHETIC",
                100.0,
                RouteMatchConfidence.High,
                double.NaN);

        lock (sync)
        {
            hasProgress = true;
            committedProgressMeters = nextProgress;
            lastSegmentIndex = segmentIndex;

            current =
                new ProgressSnapshot(
                    true,
                    true,
                    false,
                    segmentIndex,
                    nextProgress,
                    nextProgress,
                    remaining,
                    0.0,
                    double.NaN,
                    snappedCoordinate,
                    snappedCoordinate);
        }

        AndroidLog.Warn(
            LogTag,
            "INDOOR TEST synthetic route advance: " +
            $"progress={nextProgress:F1} m, " +
            $"remaining={remaining:F1} m, " +
            $"segment={segmentIndex}. " +
            "This is test-only and does not represent measured physical movement.");

        return synthetic;
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
                reason,
                RouteCorridorPolicy.GetCorridorRadiusMeters(
                    accuracyMeters,
                    indoorTestMode),
                RouteMatchConfidence.Unavailable,
                double.NaN);

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
        int endSegmentIndex,
        bool hasPreviousProgress,
        double previousProgressMeters,
        int previousSegmentIndex,
        double? courseDegrees,
        double? speedMetersPerSecond)
    {
        SegmentMatch best =
            SegmentMatch.Unavailable;

        double secondBestScore =
            double.PositiveInfinity;

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

            candidate =
                ScoreCandidate(
                    candidate,
                    start,
                    end,
                    hasPreviousProgress,
                    previousProgressMeters,
                    previousSegmentIndex,
                    courseDegrees,
                    speedMetersPerSecond);

            if (!best.IsAvailable ||
                candidate.MatchScore <
                    best.MatchScore)
            {
                if (best.IsAvailable)
                {
                    secondBestScore =
                        best.MatchScore;
                }

                best =
                    candidate;
            }
            else if (candidate.MatchScore <
                secondBestScore)
            {
                secondBestScore =
                    candidate.MatchScore;
            }
        }

        if (!best.IsAvailable)
        {
            return best;
        }

        double scoreGap =
            double.IsFinite(
                secondBestScore)
                ? Math.Max(
                    0.0,
                    secondBestScore -
                        best.MatchScore)
                : double.PositiveInfinity;

        return best.WithScoreGap(
            scoreGap);
    }

    private static SegmentMatch ScoreCandidate(
        SegmentMatch candidate,
        RoutePoint start,
        RoutePoint end,
        bool hasPreviousProgress,
        double previousProgressMeters,
        int previousSegmentIndex,
        double? courseDegrees,
        double? speedMetersPerSecond)
    {
        double score =
            candidate.CrossTrackErrorMeters;

        if (hasPreviousProgress)
        {
            double backwardJump =
                previousProgressMeters -
                candidate.ProgressMeters;

            if (backwardJump >
                3.0)
            {
                score +=
                    Math.Min(
                        60.0,
                        backwardJump *
                            0.80);
            }

            double forwardJump =
                candidate.ProgressMeters -
                previousProgressMeters;

            if (forwardJump >
                45.0)
            {
                score +=
                    Math.Min(
                        60.0,
                        (forwardJump -
                         45.0) *
                            0.60);
            }

            if (previousSegmentIndex >=
                0)
            {
                int segmentJump =
                    Math.Abs(
                        candidate.SegmentIndex -
                        previousSegmentIndex);

                if (segmentJump >
                    SearchForwardSegments)
                {
                    score +=
                        Math.Min(
                            30.0,
                            (segmentJump -
                             SearchForwardSegments) *
                                0.50);
                }
            }
        }

        double courseAlignmentError =
            double.NaN;

        if (RouteCorridorPolicy.IsCourseUsable(
                courseDegrees,
                speedMetersPerSecond))
        {
            double segmentBearing =
                CalculateInitialBearingDegrees(
                    start.Coordinate,
                    end.Coordinate);

            courseAlignmentError =
                AbsoluteHeadingDifferenceDegrees(
                    courseDegrees!.Value,
                    segmentBearing);

            score +=
                courseAlignmentError /
                180.0 *
                25.0;

            if (courseAlignmentError >
                100.0)
            {
                score +=
                    20.0;
            }
        }

        return candidate.WithScore(
            score,
            courseAlignmentError);
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
            snapped,
            crossTrack,
            double.PositiveInfinity,
            double.NaN);
    }

    private static double CalculateInitialBearingDegrees(
        GeoCoordinate start,
        GeoCoordinate end)
    {
        double startLatitude =
            DegreesToRadians(
                start.Latitude);

        double endLatitude =
            DegreesToRadians(
                end.Latitude);

        double longitudeDelta =
            DegreesToRadians(
                end.Longitude -
                start.Longitude);

        double y =
            Math.Sin(
                longitudeDelta) *
            Math.Cos(
                endLatitude);

        double x =
            Math.Cos(
                startLatitude) *
            Math.Sin(
                endLatitude) -
            Math.Sin(
                startLatitude) *
            Math.Cos(
                endLatitude) *
            Math.Cos(
                longitudeDelta);

        double bearing =
            Math.Atan2(
                y,
                x) *
            180.0 /
            Math.PI;

        return (bearing +
                360.0) %
            360.0;
    }

    private static double AbsoluteHeadingDifferenceDegrees(
        double firstDegrees,
        double secondDegrees)
    {
        double difference =
            (firstDegrees -
             secondDegrees +
             540.0) %
            360.0 -
            180.0;

        return Math.Abs(
            difference);
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

    private static int FindSegmentIndexAtDistance(
        RouteResult route,
        double distanceMeters)
    {
        if (route.Points.Count < 2)
        {
            return -1;
        }

        for (int i = 0;
             i < route.Points.Count - 1;
             i++)
        {
            if (route.Points[i + 1]
                    .DistanceFromStartMeters >=
                distanceMeters)
            {
                return i;
            }
        }

        return route.Points.Count - 2;
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

    private static string FormatFinite(
        double value)
    {
        return double.IsFinite(
                value)
            ? value.ToString(
                "F1")
            : "<unavailable>";
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
        GeoCoordinate SnappedCoordinate,
        double MatchScore,
        double MatchScoreGap,
        double CourseAlignmentErrorDegrees)
    {
        public static SegmentMatch Unavailable =>
            new(
                false,
                -1,
                0.0,
                0.0,
                double.PositiveInfinity,
                default,
                double.PositiveInfinity,
                0.0,
                double.NaN);

        public SegmentMatch WithScore(
            double matchScore,
            double courseAlignmentErrorDegrees)
        {
            return new SegmentMatch(
                IsAvailable,
                SegmentIndex,
                SegmentT,
                ProgressMeters,
                CrossTrackErrorMeters,
                SnappedCoordinate,
                matchScore,
                MatchScoreGap,
                courseAlignmentErrorDegrees);
        }

        public SegmentMatch WithScoreGap(
            double matchScoreGap)
        {
            return new SegmentMatch(
                IsAvailable,
                SegmentIndex,
                SegmentT,
                ProgressMeters,
                CrossTrackErrorMeters,
                SnappedCoordinate,
                MatchScore,
                matchScoreGap,
                CourseAlignmentErrorDegrees);
        }
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
        string RejectionReason,
        double CorridorRadiusMeters,
        RouteMatchConfidence MatchConfidence,
        double CourseAlignmentErrorDegrees);

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
