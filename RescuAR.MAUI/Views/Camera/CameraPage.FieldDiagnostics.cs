using RescuAR.AR;
using RescuAR.Diagnostics;
using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.State;
using RescuAR.Navigation.Data;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Diagnostics;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private string? lastFieldPlacementKey;
    private long lastFieldPlacementLogTimestamp = long.MinValue;
    private HeadingRevalidationDisposition? lastLoggedHeadingDisposition;
    private long lastHeadingDecisionLogTimestamp = long.MinValue;
    private long lastLoggedHeadingResetVersion = -1;
    private readonly string navigationTraceOwner = Guid.NewGuid().ToString("N")[..8];
    private NavigationGeometryTrace? geographicTrace;
    private long geographicTraceSession = -1;
    private long geographicTraceVersion;
    private string? lastGeographicRouteTrace;
    private ARCameraPoseBridge.SpatialSnapshot lastFieldReference;
    private string? lastNavigationUiTrace;

    [System.Diagnostics.Conditional("RESCUAR_DIAGNOSTICS")]
    private void AttachNavigationUiDiagnostics()
    {
        void Observe(Microsoft.Maui.Controls.BindableObject source, string property)
        {
            source.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == property) LogNavigationUiState();
            };
        }
        Observe(safeZoneConfirmationOverlay, "IsVisible");
        Observe(confirmUserArrivalButton, "IsVisible");
        Observe(turnGuidancePanel, "IsVisible");
        Observe(routeLocatorPanel, "IsVisible");
        Observe(routeLocatorLabel, "Text");
        Observe(cameraModeStatusBanner, "IsVisible");
        Observe(cameraModeStatusTitleLabel, "Text");
        Observe(cameraModeStatusSubtitleLabel, "Text");
    }

    [System.Diagnostics.Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogNavigationUiState()
    {
        LocationReading? noticeReading;
        lock (routeProgressFusionSync) noticeReading = latestRouteStartupReading;
        string key = $"trip={activeDestinationVersion}; pageActive={pageIsVisible}; mode={currentCameraModuleView}; " +
            $"arrivalOverlay={safeZoneConfirmationOverlay.IsVisible}; " +
            $"arrivalSource={(safeZoneConfirmed ? arrivalWasUserReported ? "USER_REPORTED" : "GPS_VICINITY" : "UNCONFIRMED")}; " +
            $"arrivalSamples={lastSafeZoneDecision.ConfirmationCount}; userArrivalButton={confirmUserArrivalButton.IsVisible}; " +
            $"turnCard={turnGuidancePanel.IsVisible}; statusBanner={cameraModeStatusBanner.IsVisible}; " +
            $"nearPanel={routeLocatorPanel.IsVisible}; nearCategory={arrivalNoticeCategory}; " +
            $"nearSample={noticeReading?.Timestamp.ToUnixTimeMilliseconds()}; " +
            $"nearEligible={HasArrivalNoticePriority()}; observationHeld={positionObservationHeld}; " +
            $"nearMargin={routeLocatorPanel.Margin}; nearVertical={routeLocatorPanel.VerticalOptions.Alignment}; " +
            $"arrivalReason='{lastSafeZoneDecision.Reason}'; " +
            $"hazardNotice='{routeHazardNotice ?? "none"}'; emergency={emergencyAdvisoryVisible}";
        if (lastNavigationUiTrace == key) return;
        lastNavigationUiTrace = key;
        AndroidLog.Info(RouteLogTag, $"NAV_UI {key}; nearFixAgeMs=" +
            $"{(noticeReading is null ? double.NaN : (DateTimeOffset.UtcNow - noticeReading.Timestamp).TotalMilliseconds):F0}.");
    }

    [System.Diagnostics.Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogAcceptedRoute(RouteResult route, long trip, string reason)
    {
        AndroidLog.Info(RouteLogTag,
            $"NAV_ROUTE_ACCEPTED trip={trip}; reason={reason}; algorithm='{route.Algorithm}'; " +
            $"geometry={route.GeometryFingerprint}; points={route.Points.Count}; " +
            $"distance={route.TotalDistanceMeters:F1}m; embeddedDataset={NavigationDataBootstrap.DatasetId}; " +
            $"remoteDataset={(route.Provenance.Provider == "MLD" ? route.Provenance.DatasetId : "not-applicable")}.");
        LogGeographicRoute(route, trip, force: true);
    }

    private NavigationGeometryTrace GetGeographicTrace(RouteResult route)
    {
        long session = ARCameraPoseBridge.CurrentFrame.Generation.SessionGeneration;
        if (geographicTrace is null || session > 0 && session != geographicTraceSession)
        {
            geographicTraceSession = session;
            geographicTraceVersion++;
            var origin = route.Provenance.RequestedCoordinates is { Count: > 0 } inputs
                ? inputs[0] : route.Points[0].Coordinate;
            geographicTrace = new(origin.IsValid ? origin : route.Points[0].Coordinate);
            lastGeographicRouteTrace = null;
        }
        return geographicTrace;
    }

    [System.Diagnostics.Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogGeographicRoute(RouteResult route, long trip, bool force = false)
    {
        if (route.Points.Count == 0) return;
        var trace = GetGeographicTrace(route);
        string key = $"{geographicTraceVersion}|{route.GeometryFingerprint}";
        if (!force && lastGeographicRouteTrace == key) return;
        lastGeographicRouteTrace = key;
        string prefix = $"trip={trip}; geoRef={navigationTraceOwner}/{geographicTraceVersion}; geometry={route.GeometryFingerprint}";
        var provenance = route.Provenance;
        AndroidLog.Info(RouteLogTag, $"NAV_GEO_FRAME {prefix}; session={geographicTraceSession}; " +
            $"basis=EAST_NORTH_METERS; origin={DiagnosticPrivacyPolicy.FormatCoordinate(trace.Origin.Latitude, trace.Origin.Longitude)}; " +
            $"provider={provenance.Provider}; dataset={provenance.DatasetId}; dataVersion={provenance.DataVersion}; " +
            $"points={route.Points.Count}; inputCount={provenance.RequestedCoordinates?.Count ?? 0}; " +
            $"snapCount={provenance.SnappedWaypoints?.Count ?? 0}; wayCount={provenance.SourceWayIds?.Count ?? 0}.");
        void LogCoordinates(string kind, IReadOnlyList<GeoCoordinate>? coordinates)
        {
            if (coordinates is null) return;
            foreach (var chunk in coordinates.Select((point, index) => $"{index}:{trace.Point(point)}").Chunk(16).Select((points, index) => (points, index)))
                AndroidLog.Info(RouteLogTag, $"NAV_GEO_INPUT {prefix}; kind={kind}; chunk={chunk.index}; values={string.Join("|", chunk.points)}.");
        }
        LogCoordinates("REQUESTED", provenance.RequestedCoordinates);
        LogCoordinates("SNAPPED", provenance.SnappedWaypoints);
        int part = 0;
        foreach (var chunk in trace.RouteChunks(route.Points))
            AndroidLog.Info(RouteLogTag, $"NAV_GEO_ROUTE {prefix}; chunk={part++}; values={chunk}.");
        if (DiagnosticPrivacyPolicy.HasLocationLoggingConsent && provenance.SourceWayIds is { } ways)
            foreach (var chunk in ways.Select(DiagnosticPrivacyPolicy.FormatRouteLabel).Chunk(16).Select((values, index) => (values, index)))
                AndroidLog.Info(RouteLogTag, $"NAV_GEO_SOURCE {prefix}; chunk={chunk.index}; ways={string.Join("|", chunk.values)}.");
    }

    [System.Diagnostics.Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogPositionPipeline(RouteResult route, LocationReading reading,
        RouteProgressTracker.RouteProgressUpdate matched, RouteProgressTracker.RouteProgressUpdate fused)
    {
        if (route.Points.Count == 0) return;
        LogGeographicRoute(route, activeDestinationVersion);
        var trace = GetGeographicTrace(route);
        var frame = ARCameraPoseBridge.CurrentFrame;
        bool paired = ARCameraPoseBridge.TryGetObservationFrame(reading.Timestamp, DateTimeOffset.UtcNow,
            Environment.TickCount64, out var observed, out long skew);
        bool roadReady = RouteDisplayProgressPolicy.TryAtDistance(route, matched.RawProgressMeters, out var road);
        AndroidLog.Info(ProgressLogTag, $"NAV_POSITION_PIPELINE sample={reading.Timestamp.ToUnixTimeMilliseconds()}; " +
            $"trip={activeDestinationVersion}; geoRef={navigationTraceOwner}/{geographicTraceVersion}; geometry={route.GeometryFingerprint}; " +
            $"gpsEN={trace.Point(reading.Coordinate)}; accuracy={reading.AccuracyMeters:F2}m; " +
            $"ageMs={(DateTimeOffset.UtcNow - reading.Timestamp).TotalMilliseconds:F0}; provider=unavailable; courseAccuracy=unavailable; " +
            $"accepted={matched.IsAccepted}; held={positionObservationHeld}; match={matched.MatchConfidence}; " +
            $"rawProgress={matched.RawProgressMeters:F3}; fusedProgress={fused.CommittedProgressMeters:F3}; " +
            $"roadEN={(roadReady ? trace.Point(road.RoadCoordinate) : "unavailable")}; " +
            $"fusion={lastGpsFusionAction}; yaw={activeMapToArYawDegrees:F3}; " +
            $"paired={paired}; pairedFrameNs={(paired ? observed.FrameTimestamp : 0)}; pairingSkewMs={skew}; " +
            $"pairedCamera={(paired ? FormattableString.Invariant($"({observed.Pose.PositionX:F3},{observed.Pose.PositionZ:F3})") : "unavailable")}; " +
            $"receiptFrameNs={frame.FrameTimestamp}; receiptCamera=({frame.Pose.PositionX:F3},{frame.Pose.PositionZ:F3}); " +
            $"session={frame.Generation.SessionGeneration}; trackingEpoch={frame.TrackingEpoch}; worldFrame={frame.WorldFrame}.");
    }

    [System.Diagnostics.Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogPositionInnovation(VerifiedPosition? previous, RouteResult route,
        LocationReading reading, RouteBacktrackingPolicy.TrackedMotion? motion)
    {
        if (previous is null || !ReferenceEquals(previous.Route, route) ||
            previous.Motion is not { } from || motion is not { } to || !from.IsContinuousWith(to) ||
            reading.Timestamp - previous.At > TimeSpan.FromSeconds(RouteDisplayProgressPolicy.MaximumPdrGpsAgeSeconds)) return;
        double east = (reading.Coordinate.Longitude - previous.Coordinate.Longitude) *
            Math.PI / 180 * 6371008.8 * Math.Cos(previous.Coordinate.Latitude * Math.PI / 180);
        double north = (reading.Coordinate.Latitude - previous.Coordinate.Latitude) * Math.PI / 180 * 6371008.8;
        var projected = MapToArCoordinates.Rotate(east, north, to.YawDegrees);
        double arX = to.X - from.X, arZ = to.Z - from.Z;
        AndroidLog.Info(RouteLogTag,
            $"NAV_POSITION_INNOVATION sample={reading.Timestamp.ToUnixTimeMilliseconds()}; " +
            $"previousSample={previous.At.ToUnixTimeMilliseconds()}; session={to.Session}; " +
            $"ground={from.GroundReference}/{to.GroundReference}; " +
            $"trackingEpoch={to.TrackingEpoch}; worldFrame={to.WorldFrame}; " +
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
            $"{route.Version}|{frame.Anchor.ReferenceGeneration}|{identitySuspended}|{frame.TrackingEpoch}|{frame.WorldFrame}";
        long now = Environment.TickCount64;
        if (key == lastFieldPlacementKey && lastFieldPlacementLogTimestamp != long.MinValue &&
            now - lastFieldPlacementLogTimestamp < 5_000) return;
        lastFieldPlacementKey = key;
        lastFieldPlacementLogTimestamp = now;
        LocationReading? reading;
        lock (routeProgressFusionSync) reading = latestRouteStartupReading;
        var rendered = ARRouteRenderer.Visibility;
        AndroidLog.Info(RouteLogTag, $"NAV_CUE trip={activeDestinationVersion}; " +
            $"floorArrowEnabled={ARRoadApproachRenderer.IsVisible}; reason='{ARRoadApproachBridge.LastRenderReason}'; " +
            $"cameraFrameNs={frame.FrameTimestamp}; epoch={frame.TrackingEpoch}; world={frame.WorldFrame}.");
        var calibration = _headingAlignmentService.LastResult;
        AndroidLog.Info(RouteLogTag, $"NAV_WORKLOAD trip={activeDestinationVersion}; " +
            $"publishedWindows={Interlocked.Read(ref routeWindowPublishedCount)}; reusedWindows={Interlocked.Read(ref routeWindowReusedCount)}; " +
            $"visibilitySampleMs=250; assessmentIntervalSec={navigationStability.Options.AssessmentInterval.TotalSeconds:F1}; " +
            $"calibrationRevision={_headingAlignmentService.CalibrationRevision}; positioning='{navigationStability.Current.Reason}'.");
        var oldReference = lastFieldReference;
        lastFieldReference = frame;
        var anchor = frame.Anchor;
        AndroidLog.Info(RouteLogTag, $"NAV_REFERENCE session={frame.Generation.SessionGeneration}; frameNs={frame.FrameTimestamp}; " +
            $"tracking={frame.IsTracking}; trackingEpoch={frame.TrackingEpoch}; worldFrame={frame.WorldFrame}; " +
            $"ground={anchor.ReferenceGeneration}; trust={anchor.Trust}; anchorXZ=({anchor.PositionX:F3},{anchor.PositionZ:F3}); " +
            $"anchorHasRotation={anchor.HasRotation}; anchorQ={anchor.Rotation}; " +
            $"previousFrameNs={oldReference.FrameTimestamp}; previousGround={oldReference.Anchor.ReferenceGeneration}; " +
            $"previousAnchorQ={oldReference.Anchor.Rotation}; calibrationSource={calibration?.Source}; " +
            $"requiresMovement={calibration?.RequiresMovementValidation}; calibrationWorld={calibration?.WorldFrame}.");
        if (reading is not null && _headingAlignmentService.TryGetIndependentObservation(reading.Coordinate,
                reading.AltitudeMeters, out var independent))
            AndroidLog.Info(HeadingLogTag, $"NAV_ORIENTATION sample={independent.OrientationObservedAt.ToUnixTimeMilliseconds()}; " +
                $"frameNs={independent.ObservationPoseFrame}; skewMs={independent.ObservationPoseSkewMilliseconds}; " +
                $"yaw={independent.MapToArYawDegrees:F3}; bearing={independent.TrueCameraHeadingDegrees:F3}; " +
                $"arAzimuth={independent.ArCameraAzimuthDegrees:F3}; accuracy=unavailable; action=OBSERVE_ONLY; " +
                $"session={independent.SessionGeneration}; trackingEpoch={independent.TrackingEpoch}; worldFrame={independent.WorldFrame}.");
        AndroidLog.Info(RouteLogTag, $"NAV_PLACEMENT visible={confidence.AllowsRouteGeometry}; " +
            $"reason='{confidence.DisplayMessage}'; session={frame.Generation.SessionGeneration}; " +
            $"route={route.Version}; spatial={continuity.State}; tracking={frame.IsTracking}; " +
            $"visual={route.NavigationState.VisualKind}; mode={arRouteVisualMode}; " +
            $"preview={approachRoutePreviewPublished}; entrySamples={roadFollowingReentryConfirmationCount}/{RoadFollowingEntryRequiredSamples}; " +
            $"rerouting={dynamicRerouteInProgress}; rerouteResult={lastRerouteResult}; " +
            $"observationHeld={positionObservationHeld}; " +
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
            $"viewportMidpoints={rendered.ViewportMidpoints}; arrows={rendered.EnabledArrowWings}; " +
            $"ribbonClippedCenterlines={rendered.RibbonBounds}; arrowClippedCenterlines={rendered.ArrowBounds}; " +
            $"arrowDepthHidden={rendered.ArrowDepthHidden}; arrowPrefixHidden={rendered.ArrowPrefixHidden}.");
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
        if (activeRoute is { } mldRoute && IsMldRoute(mldRoute))
        {
            var motion = GetTrackedRouteMotion(requireTrustedHeading: false);
            AndroidLog.Info(RouteLogTag, $"NAV_ALIGNMENT trip={activeDestinationVersion}; route={route.Version}; " +
                $"registration='{placed.AlignmentSource}'; visual={HasVisualRoadRegistration()}; " +
                $"contradicted={(motion.HasValue && _routeDisplayProgressPolicy.HasContradictedVisualReference(mldRoute, motion.Value))}; " +
                $"style=SOLID_RIBBON; width={ARRouteVisualPolicy.GetRouteWidthMeters(0):F2}m; " +
                $"displaySource=REGISTERED_AR; rawGpsProgress={progress.RawProgressMeters:F2}; " +
                $"acceptedProgress={progress.CommittedProgressMeters:F2}; displayProgress={route.NavigationState.WindowStartProgressMeters:F2}; " +
                $"window={(route.Points.Count > 0 ? route.Points[^1].DistanceFromWindowStartMeters : 0):F2}; " +
                $"session={frame.Generation.SessionGeneration}; epoch={frame.TrackingEpoch}; world={frame.WorldFrame}.");
        }
        if (placed.IsAvailable && route.Points.Count >= 2)
        {
            var first = route.Points[0];
            var final = route.Points[^1];
            AndroidLog.Info(RouteLogTag,
                $"NAV_REGISTRATION route={route.Version}; sample={placed.ObservationTime?.ToUnixTimeMilliseconds()}; " +
                $"source='{placed.Source}'; session={placed.Generation.SessionGeneration}; " +
                $"ground={placed.GroundReference}; spatial={placed.SpatialVersion}; " +
                $"trackingEpoch={placed.TrackingEpoch}; worldFrame={placed.WorldFrame}; " +
                $"placementAgeMs={now - placed.PublishedAt}; " +
                $"poseAtPublicationAgeMs={placed.PublishedAt - placed.PosePublishedAt}; frameNs={placed.FrameTimestamp}; " +
                $"gpsAtPublicationMs={(placed.ObservationTime.HasValue ? placed.PublishedAt - now + (DateTimeOffset.UtcNow - placed.ObservationTime.Value).TotalMilliseconds : double.NaN):F0}; " +
                $"yaw={placed.YawDegrees:F2}; gpsToRoadEN=({placed.GpsToRoadEast:F2},{placed.GpsToRoadNorth:F2})m; " +
                $"cameraPairing={placed.AlignmentSource}; registrationSample={placed.RegistrationSample}; " +
                $"registeredWorldRef=({placed.RegisteredWorldX:F3},{placed.RegisteredWorldZ:F3}); " +
                $"observationPoseFrameNs={placed.ObservationPoseFrame}; " +
                $"observationPoseSkewMs={placed.ObservationPoseSkewMilliseconds}; " +
                $"observationCamera=({placed.ObservationCameraX:F3},{placed.ObservationCameraZ:F3}); anchorQ={placed.AnchorRotation}; " +
                $"gpsToRoadAR=({placed.GpsToRoadX:F2},{placed.GpsToRoadZ:F2})m; " +
                $"origin=({placed.AnchorX:F2},{placed.AnchorZ:F2}); cameraAtPublication=({placed.CameraX:F2},{placed.CameraZ:F2}); " +
                $"first=({first.X:F2},{first.Z:F2}); final=({final.X:F2},{final.Z:F2}); " +
                $"drawRoute={rendered.RouteVersion}; drawGround={rendered.Placement.GroundReference}; " +
                $"drawOrigin=({rendered.RootX:F2},{rendered.RootZ:F2}); " +
                $"drawCamera=({rendered.CameraX:F2},{rendered.CameraZ:F2}).");
        }
    }
}
