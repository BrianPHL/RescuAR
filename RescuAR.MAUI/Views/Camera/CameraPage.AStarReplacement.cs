using RescuAR.AR;
using RescuAR.Diagnostics;
using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Timing;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private AStarMappedPreviewPolicy.Owner? aStarPreviewOwner;

    private readonly record struct AStarReplacementState(
        RouteProgressTracker.RouteProgressUpdate Match, bool RegistrationPreserved,
        bool RoadEntryRetained, bool ProgressMapped, AStarReplacementContinuityPolicy? Continuity);

    // Called inside the existing acceptance lock, only after geographic and
    // hazard validation. No replacement creates a new GPS/arrival observation.
    private AStarReplacementState ApplyAStarReplacementContinuity(RouteResult previous,
        RouteResult replacement, RouteProgressTracker.ProgressSnapshot previousProgress,
        RouteProgressTracker.RouteProgressUpdate match, LocationReading? reading,
        bool wasFollowingRoad, bool hazardAware)
    {
        AStarReplacementContinuityPolicy.TryCreate(previous, replacement, out var continuity);
        bool mapped = previousProgress.HasProgress && continuity is not null &&
            continuity.TryMapProgress(previousProgress.CommittedProgressMeters, out _);
        if (mapped)
        {
            continuity!.TryMapProgress(previousProgress.CommittedProgressMeters, out double progress);
            match = _routeProgressTracker.ApplyFusionCorrection(progress, match, "ASTAR_CORRIDOR_REBASE");
        }
        _gpsPdrFusionPolicy.RebindForAStarReplacement(mapped ? continuity : null);
        var replacementObservationTime = reading?.ObservationTime ?? NavigationClock.UtcNow;
        bool registrationPreserved = _routeDisplayProgressPolicy.RebindForAStarReplacement(previous,
            replacement, continuity, match, replacementObservationTime);

        // These policies retain geographic observations and their watermarks.
        // Clearing them would bless the same disputed fix as a fresh baseline.
        positionObservationPolicy.RebindForAStarReplacement(previous, replacement);
        progressMotionPolicy.RebindForAStarReplacement(previous, replacement);
        navigationStability.RebindForAStarReplacement(previous, replacement);
        if (verifiedPosition is { } verified && ReferenceEquals(verified.Route, previous) &&
            continuity is not null && continuity.TryMapProgress(verified.Progress, out double verifiedProgress) &&
            continuity.TryMapProgress(verified.DisplayProgress, out double displayProgress))
            verifiedPosition = verified with { Route = replacement, Progress = verifiedProgress,
                DisplayProgress = displayProgress };
        else verifiedPosition = null;
        acceptedGpsPosition = reading is not null && RouteDisplayProgressPolicy.TryResolveGps(replacement, match, out _)
            ? new(replacement, reading, match) : null;
        lastRegisteredPublication = null;
        lastRegistrationRejection = null;
        aStarPreviewOwner = null;
        _verifiedRouteWindowHoldPolicy.Reset();
        approachRoutePreviewPublished = false;

        bool retainEntry = !hazardAware && wasFollowingRoad && continuity is not null && registrationPreserved &&
            !arPositionObservationHeld && !_gpsPdrFusionPolicy.IsRouteIdentitySuspended &&
            GetTrackedRouteMotion() is { } motion &&
            _routeDisplayProgressPolicy.CanRetainRoadEntry(replacement, match, NavigationClock.UtcNow, motion);
        AndroidLog.Info(RerouteLogTag,
            $"ASTAR_REPLACEMENT_CONTINUITY sameCorridor={continuity is not null}; progressMapped={mapped}; " +
            $"registrationPreserved={registrationPreserved}; supportAt={_routeDisplayProgressPolicy.RegistrationSupportedAt:O}; " +
            $"registrationState={_routeDisplayProgressPolicy.LastRegistrationDecision.State}; " +
            $"identitySuspended={_gpsPdrFusionPolicy.IsRouteIdentitySuspended}; newGpsEvidence=False.");
        NavigationReplayCapture.Record("route.replacement", new
            {
                previousGeometry = previous.GeometryFingerprint, geometry = replacement.GeometryFingerprint,
                trip = activeDestinationVersion, previousProgress, match,
                readingLatitude = reading?.Coordinate.Latitude, readingLongitude = reading?.Coordinate.Longitude,
                observationTimeUtc = replacementObservationTime, evaluatedAtUtc = NavigationClock.UtcNow,
                progressMapped = mapped, registrationPreserved, roadEntryRetained = retainEntry, hazardAware,
                mappingPreviousStart = continuity?.PreviousStartMeters,
                mappingPreviousEnd = continuity?.PreviousEndMeters
            });
        return new(match, registrationPreserved, retainEntry, mapped, continuity);
    }

    private void BindAStarPublishedWindow(RouteResult route, ARCameraPoseBridge.SpatialSnapshot frame)
    {
        if (!AStarRouteDisplayPolicy.AppliesTo(route)) return;
        var window = ARRouteBridge.Current;
        if (!ReferenceEquals(activeRoute, route) || !window.IsAvailable || !window.Placement.IsFor(frame) ||
            window.Algorithm != route.Algorithm) return;
        aStarPreviewOwner = new(activeDestinationVersion, route.GeometryFingerprint, window.Version,
            frame.Generation.SessionGeneration, frame.Anchor.ReferenceGeneration, frame.TrackingEpoch,
            frame.WorldFrame, _headingAlignmentService.CalibrationRevision);
    }

    private bool CanRetainAStarMappedPreview(RouteResult route)
    {
        if (!AStarRouteDisplayPolicy.AppliesTo(route) || !ReferenceEquals(activeRoute, route) ||
            !approachRoutePreviewPublished || safeZoneConfirmed || dynamicRerouteInProgress ||
            verifiedPosition is not { } verified || !ReferenceEquals(verified.Route, route) ||
            verified.At != latestGpsTimestampForRouting || aStarPreviewOwner is not { } owner) return false;
        var preview = ARRouteBridge.Current;
        var frame = ARCameraPoseBridge.CurrentFrame;
        if (!preview.IsAvailable || preview.NavigationState.VisualKind != RouteVisualKind.RouteWindow ||
            !frame.IsFresh || !frame.IsTracking || !frame.Pose.IsTracking || !HasCurrentRouteHeading() ||
            !preview.Placement.IsFor(frame)) return false;
        var current = new AStarMappedPreviewPolicy.Owner(activeDestinationVersion, route.GeometryFingerprint,
            preview.Version, frame.Generation.SessionGeneration, frame.Anchor.ReferenceGeneration,
            frame.TrackingEpoch, frame.WorldFrame, _headingAlignmentService.CalibrationRevision);
        return AStarMappedPreviewPolicy.CanRetain(route, owner, current, verified.At, NavigationClock.UtcNow,
            HasSupportedRouteRegistration(route, preview.NavigationState.WindowStartProgressMeters, NavigationClock.UtcNow),
            positionObservationHeld || arPositionObservationHeld, _gpsPdrFusionPolicy.IsRouteIdentitySuspended,
            routeHazardBlocksGuidance);
    }

    private bool CanRetainQualifiedMappedPreview(RouteResult route) => AStarRouteDisplayPolicy.AppliesTo(route)
        ? CanRetainAStarMappedPreview(route) : CanRetainMldMappedPreview(route);
}
