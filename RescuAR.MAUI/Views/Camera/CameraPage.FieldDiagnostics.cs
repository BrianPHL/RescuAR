using RescuAR.AR;
using RescuAR.Diagnostics;
using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.State;
using RescuAR.Navigation.Data;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Projection;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private string? lastFieldPlacementKey;
    private long lastFieldPlacementLogTimestamp = long.MinValue;
    private HeadingRevalidationDisposition? lastLoggedHeadingDisposition;
    private long lastHeadingDecisionLogTimestamp = long.MinValue;
    private long lastLoggedHeadingResetVersion = -1;

    [System.Diagnostics.Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogAcceptedRoute(RouteResult route, long trip, string reason)
    {
        AndroidLog.Info(RouteLogTag,
            $"NAV_ROUTE_ACCEPTED trip={trip}; reason={reason}; algorithm='{route.Algorithm}'; " +
            $"geometry={route.GeometryFingerprint}; points={route.Points.Count}; " +
            $"distance={route.TotalDistanceMeters:F1}m; embeddedDataset={NavigationDataBootstrap.DatasetId}; " +
            "remoteDataset=unreported.");
    }

    [System.Diagnostics.Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogPositionInnovation(VerifiedPosition? previous, RouteResult route,
        LocationReading reading, RouteBacktrackingPolicy.TrackedMotion? motion)
    {
        if (previous is null || !ReferenceEquals(previous.Route, route) ||
            previous.Motion is not { } from || motion is not { } to || from.Session != to.Session ||
            Math.Abs(Math.IEEERemainder(from.YawDegrees - to.YawDegrees, 360)) > 8) return;
        double east = (reading.Coordinate.Longitude - previous.Coordinate.Longitude) *
            Math.PI / 180 * 6371008.8 * Math.Cos(previous.Coordinate.Latitude * Math.PI / 180);
        double north = (reading.Coordinate.Latitude - previous.Coordinate.Latitude) * Math.PI / 180 * 6371008.8;
        var projected = MapToArCoordinates.Rotate(east, north, to.YawDegrees);
        double arX = to.X - from.X, arZ = to.Z - from.Z;
        AndroidLog.Info(RouteLogTag,
            $"NAV_POSITION_INNOVATION sample={reading.Timestamp.ToUnixTimeMilliseconds()}; " +
            $"previousSample={previous.At.ToUnixTimeMilliseconds()}; session={to.Session}; " +
            $"ground={from.GroundReference}/{to.GroundReference}; " +
            $"gpsAgeMs={(DateTimeOffset.UtcNow - reading.Timestamp).TotalMilliseconds:F0}; " +
            $"accuracy={previous.Accuracy:F1}/{reading.AccuracyMeters:F1}m; " +
            $"gpsEN=({east:F2},{north:F2})m; gpsAR=({projected.X:F2},{projected.Z:F2})m; " +
            $"trackedAR=({arX:F2},{arZ:F2})m; residual=({arX - projected.X:F2},{arZ - projected.Z:F2})m; " +
            $"yaw={to.YawDegrees:F2}; provider=unavailable; bearingAccuracy=unavailable.");
    }

    [System.Diagnostics.Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogNavigationFieldSnapshot(
        ARGuidanceConfidencePolicy.GuidanceConfidenceSnapshot confidence,
        ARRouteBridge.RouteSnapshot route, RouteProgressTracker.ProgressSnapshot progress,
        ARCameraSpatialController.SpatialContinuitySnapshot continuity,
        double gpsAgeSeconds, double? gpsAccuracy, bool identitySuspended, bool headingTrusted)
    {
        if (!pageIsVisible) return;
        var frame = ARCameraPoseBridge.CurrentFrame;
        string key = $"{confidence.AllowsRouteGeometry}|{confidence.DisplayMessage}|" +
            $"{route.Version}|{frame.Anchor.ReferenceGeneration}|{identitySuspended}";
        long now = Environment.TickCount64;
        if (key == lastFieldPlacementKey && lastFieldPlacementLogTimestamp != long.MinValue &&
            now - lastFieldPlacementLogTimestamp < 5_000) return;
        lastFieldPlacementKey = key;
        lastFieldPlacementLogTimestamp = now;
        LocationReading? reading;
        lock (routeProgressFusionSync) reading = latestRouteStartupReading;
        var rendered = ARRouteRenderer.Visibility;
        AndroidLog.Info(RouteLogTag, $"NAV_PLACEMENT visible={confidence.AllowsRouteGeometry}; " +
            $"reason='{confidence.DisplayMessage}'; session={frame.Generation.SessionGeneration}; " +
            $"route={route.Version}; spatial={continuity.State}; tracking={frame.IsTracking}; " +
            $"visual={route.NavigationState.VisualKind}; mode={arRouteVisualMode}; " +
            $"preview={approachRoutePreviewPublished}; entrySamples={roadFollowingReentryConfirmationCount}/{RoadFollowingEntryRequiredSamples}; " +
            $"rerouting={dynamicRerouteInProgress}; rerouteResult={lastRerouteResult}; " +
            $"ground={frame.Anchor.Trust}/{frame.Anchor.ReferenceGeneration}; " +
            $"gpsAgeSec={gpsAgeSeconds:F1}; accuracy={gpsAccuracy:F1}m; " +
            $"identitySuspended={identitySuspended}; identitySamples={lastGpsRouteIdentityConfirmationCount}; " +
            $"headingTrusted={headingTrusted}; yaw={activeMapToArYawDegrees:F1}; " +
            $"headingDeviation={lastHeadingAlignment?.MaxSampleDeviationDegrees:F1}deg.");
        AndroidLog.Info(RouteLogTag, $"NAV_RENDER route={rendered.RouteVersion}; " +
            $"session={rendered.Session}; ground={rendered.GroundReference}; " +
            $"ageMs={now - rendered.MeasuredAt}; " +
            $"rootEnabled={rendered.RootEnabled}; prepared={rendered.PreparedSegments}; " +
            $"enabled={rendered.EnabledSegments}; depthHidden={rendered.DepthHiddenSegments}; " +
            $"prefixHidden={rendered.PrefixHiddenSegments}; inFront={rendered.InFrontSegments}; " +
            $"viewportMidpoints={rendered.ViewportMidpoints}; arrows={rendered.EnabledArrowWings}.");
        AndroidLog.Info(RouteLogTag, $"NAV_PROGRESS algorithm='{activeRoute?.Algorithm}'; " +
            $"trip={NavigationDestinationBridge.Current.Version}; segment={progress.SegmentIndex}; " +
            $"progress={progress.CommittedProgressMeters:F1}m; remaining={progress.RemainingMeters:F1}m; " +
            $"rawGpsProgress={progress.RawProgressMeters:F1}m; " +
            $"displayProgress={route.NavigationState.WindowStartProgressMeters:F1}m; " +
            $"crossTrack={progress.CrossTrackErrorMeters:F1}m; " +
            $"speed={reading?.SpeedMetersPerSecond:F2}; course={reading?.CourseDegrees:F1}; " +
            $"fusion={lastGpsFusionAction}; reliability={lastGpsReliabilityWeight:F2}; " +
            $"gpsPdrDelta={lastGpsPdrDivergenceMeters:F1}m; " +
            $"backtrackSamples={lastBacktrackingDecision.ConfirmationCount}/" +
            $"{lastBacktrackingDecision.RequiredConfirmationCount}; " +
            $"backtrackRetreat={lastBacktrackingDecision.RetreatMeters:F1}m; " +
            $"backtrackArRetreat={lastBacktrackingDecision.ArRetreatMeters:F1}m; " +
            $"backtrackEvidence={lastBacktrackingDecision.EvidenceSource}; " +
            $"backtrackReason='{lastBacktrackingDecision.Reason}'; " +
            $"arrivalDistance={lastSafeZoneDecision.DistanceToDestinationMeters:F1}m; " +
            $"arrivalSamples={lastSafeZoneDecision.ConfirmationCount}/{lastSafeZoneDecision.RequiredConfirmationCount}; " +
            $"arrivalReason='{lastSafeZoneDecision.Reason}'.");
        var placed = route.Placement;
        if (placed.IsAvailable && route.Points.Count >= 2)
        {
            var first = route.Points[0];
            var final = route.Points[^1];
            AndroidLog.Info(RouteLogTag,
                $"NAV_REGISTRATION route={route.Version}; sample={placed.ObservationTime?.ToUnixTimeMilliseconds()}; " +
                $"source='{placed.Source}'; session={placed.Generation.SessionGeneration}; " +
                $"ground={placed.GroundReference}; spatial={placed.SpatialVersion}; " +
                $"placementAgeMs={now - placed.PublishedAt}; " +
                $"poseAtPublicationAgeMs={placed.PublishedAt - placed.PosePublishedAt}; frameNs={placed.FrameTimestamp}; " +
                $"gpsAtPublicationMs={(placed.ObservationTime.HasValue ? placed.PublishedAt - now + (DateTimeOffset.UtcNow - placed.ObservationTime.Value).TotalMilliseconds : double.NaN):F0}; " +
                $"yaw={placed.YawDegrees:F2}; gpsToRoadEN=({placed.GpsToRoadEast:F2},{placed.GpsToRoadNorth:F2})m; " +
                $"gpsToRoadAR=({placed.GpsToRoadX:F2},{placed.GpsToRoadZ:F2})m; " +
                $"origin=({placed.AnchorX:F2},{placed.AnchorZ:F2}); cameraAtPublication=({placed.CameraX:F2},{placed.CameraZ:F2}); " +
                $"first=({first.X:F2},{first.Z:F2}); final=({final.X:F2},{final.Z:F2}); " +
                $"drawRoute={rendered.RouteVersion}; drawGround={rendered.Placement.GroundReference}; " +
                $"drawOrigin=({rendered.RootX:F2},{rendered.RootZ:F2}); " +
                $"drawCamera=({rendered.CameraX:F2},{rendered.CameraZ:F2}).");
        }
    }
}
