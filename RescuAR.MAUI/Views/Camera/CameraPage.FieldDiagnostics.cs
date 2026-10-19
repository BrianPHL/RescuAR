using RescuAR.AR;
using RescuAR.Diagnostics;
using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.State;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private string? lastFieldPlacementKey;
    private long lastFieldPlacementLogTimestamp = long.MinValue;
    private HeadingRevalidationDisposition? lastLoggedHeadingDisposition;
    private long lastHeadingDecisionLogTimestamp = long.MinValue;

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
        AndroidLog.Info(RouteLogTag, $"NAV_PLACEMENT visible={confidence.AllowsRouteGeometry}; " +
            $"reason='{confidence.DisplayMessage}'; session={frame.Generation.SessionGeneration}; " +
            $"route={route.Version}; spatial={continuity.State}; tracking={frame.IsTracking}; " +
            $"ground={frame.Anchor.Trust}/{frame.Anchor.ReferenceGeneration}; " +
            $"gpsAgeSec={gpsAgeSeconds:F1}; accuracy={gpsAccuracy:F1}m; " +
            $"identitySuspended={identitySuspended}; identitySamples={lastGpsRouteIdentityConfirmationCount}; " +
            $"headingTrusted={headingTrusted}; yaw={activeMapToArYawDegrees:F1}; " +
            $"headingDeviation={lastHeadingAlignment?.MaxSampleDeviationDegrees:F1}deg.");
        AndroidLog.Info(RouteLogTag, $"NAV_PROGRESS algorithm='{activeRoute?.Algorithm}'; " +
            $"trip={NavigationDestinationBridge.Current.Version}; segment={progress.SegmentIndex}; " +
            $"progress={progress.CommittedProgressMeters:F1}m; remaining={progress.RemainingMeters:F1}m; " +
            $"crossTrack={progress.CrossTrackErrorMeters:F1}m; " +
            $"speed={reading?.SpeedMetersPerSecond:F2}; course={reading?.CourseDegrees:F1}; " +
            $"fusion={lastGpsFusionAction}; reliability={lastGpsReliabilityWeight:F2}; " +
            $"gpsPdrDelta={lastGpsPdrDivergenceMeters:F1}m; " +
            $"backtrackSamples={lastBacktrackingDecision.ConfirmationCount}/" +
            $"{lastBacktrackingDecision.RequiredConfirmationCount}; " +
            $"backtrackRetreat={lastBacktrackingDecision.RetreatMeters:F1}m; " +
            $"backtrackReason='{lastBacktrackingDecision.Reason}'; " +
            $"arrivalDistance={lastSafeZoneDecision.DistanceToDestinationMeters:F1}m; " +
            $"arrivalSamples={lastSafeZoneDecision.ConfirmationCount}/{lastSafeZoneDecision.RequiredConfirmationCount}; " +
            $"arrivalReason='{lastSafeZoneDecision.Reason}'.");
    }
}
