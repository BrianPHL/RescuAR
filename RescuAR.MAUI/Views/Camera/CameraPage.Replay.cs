using RescuAR.AR;
using RescuAR.Diagnostics;
using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.State;
using RescuAR.Navigation.Timing;
using System.Diagnostics;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    // Records are diagnostic output only. These helpers must not acquire a new
    // location, update a policy, or create substitute positioning evidence.
    private static object ReplayReading(LocationReading reading) => new
    {
        latitude = reading.Coordinate.Latitude,
        longitude = reading.Coordinate.Longitude,
        reading.AccuracyMeters,
        reading.AltitudeMeters,
        reading.SpeedMetersPerSecond,
        reading.CourseDegrees,
        reading.CourseAccuracyDegrees,
        reading.Provider,
        providerTimestampUtc = reading.Timestamp,
        observationTimeUtc = reading.ObservationTime,
        reading.AcquisitionElapsedRealtimeNanoseconds,
        reading.AcquisitionMonotonicMilliseconds
    };

    [Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogReplayPolicyReset(string reason, bool registration = false, bool position = false,
        bool stability = false, bool progressMotion = false, bool fusion = false, bool arrival = false) =>
        NavigationReplayCapture.Record("policy.reset", new
        {
            trip = activeDestinationVersion > 0 ? activeDestinationVersion : NavigationDestinationBridge.Current.Version,
            reason, registration, position, stability, progressMotion, fusion, arrival,
            evaluatedAtUtc = NavigationClock.UtcNow
        });

    [Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogReplayStartup(RouteResult route, LocationReading reading, GeoCoordinate origin,
        RouteProgressTracker.RouteProgressUpdate matched, GpsPdrFusionPolicy.GpsFusionDecision fusion,
        RouteProgressTracker.RouteProgressUpdate fused, DateTimeOffset evaluatedAt,
        double? motionFilterProgress, bool? motionFilterHeld) => NavigationReplayCapture.Record("navigation.startup", new
        {
            trip = activeDestinationVersion, geometry = route.GeometryFingerprint, reading = ReplayReading(reading),
            origin, matched, fusion, fused, evaluatedAtUtc = evaluatedAt, motionFilterProgress, motionFilterHeld
        });

    [Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogReplayRegistrationTransfer(RouteResult previous, RouteResult replacement,
        RouteProgressTracker.RouteProgressUpdate update, LocationReading reading,
        RouteBacktrackingPolicy.TrackedMotion motion, DateTimeOffset evaluatedAt, bool retained) =>
        NavigationReplayCapture.Record("registration.transfer", new
        {
            trip = activeDestinationVersion, previousGeometry = previous.GeometryFingerprint,
            geometry = replacement.GeometryFingerprint, update, reading = ReplayReading(reading), motion,
            evaluatedAtUtc = evaluatedAt, retained
        });

    [Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogReplayReplacementMatch(RouteResult route, LocationReading reading,
        RouteProgressTracker.RouteProgressUpdate update) => NavigationReplayCapture.Record("route.match", new
        {
            trip = activeDestinationVersion, geometry = route.GeometryFingerprint,
            reading = ReplayReading(reading), update, evaluatedAtUtc = NavigationClock.UtcNow
        });

    [Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogReplayAcceptedRoute(RouteResult route, long trip, string reason)
    {
        if (!NavigationReplayCapture.IsEnabled) return;
        const int batchSize = 512;
        for (int start = 0; start < route.Points.Count; start += batchSize)
            NavigationReplayCapture.Record("route.geometry", new
            {
                trip, geometry = route.GeometryFingerprint, startIndex = start,
                totalPoints = route.Points.Count,
                points = route.Points.Skip(start).Take(batchSize).ToArray()
            });
        var destination = NavigationDestinationBridge.Current;
        NavigationReplayCapture.Record("route.accepted", new
        {
            trip, reason, geometry = route.GeometryFingerprint, route.Algorithm,
            route.TotalDistanceMeters, route.Provenance,
            destination = new { destination.Version, destination.IsAvailable,
                destination.Coordinate, destination.SafeZoneRadiusMeters },
            progress = _routeProgressTracker.Current,
            evaluatedAtUtc = NavigationClock.UtcNow
        });
    }

    [Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogReplayObservation(RouteResult route, LocationReading reading,
        RouteProgressTracker.ProgressSnapshot before,
        RouteProgressTracker.RouteProgressUpdate matched,
        RouteProgressTracker.RouteProgressUpdate fused,
        GpsPdrFusionPolicy.GpsFusionDecision fusion,
        RoutePositionObservationPolicy.Decision observation,
        NavigationStabilityPolicy.Decision stability,
        RouteBacktrackingPolicy.TrackedMotion? motion, DateTimeOffset evaluatedAt)
    {
        if (!NavigationReplayCapture.IsEnabled) return;
        var frame = ARCameraPoseBridge.CurrentFrame;
        NavigationReplayCapture.Record("navigation.observation", new
        {
            trip = activeDestinationVersion, geometry = route.GeometryFingerprint,
            reading = ReplayReading(reading), evaluatedAtUtc = evaluatedAt,
            before, matched, fused, fusion, observation, stability, motion,
            geographicHeld = positionObservationHeld, arHeld = arPositionObservationHeld,
            yawDegrees = activeMapToArYawDegrees,
            headingTrusted = HasCurrentRouteHeading(),
            frame = new { frame.Version, frame.FrameTimestamp, frame.TrackingEpoch,
                frame.WorldFrame, frame.IsTracking, session = frame.Generation.SessionGeneration,
                ground = frame.Anchor.ReferenceGeneration, frame.Pose },
            registration = _routeDisplayProgressPolicy.LastRegistrationDecision
        });
    }

    [Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogReplayRegistration(RouteResult route,
        RouteProgressTracker.RouteProgressUpdate update, LocationReading reading,
        RouteBacktrackingPolicy.TrackedMotion motion,
        RouteDisplayProgressPolicy.RegistrationDecision decision, DateTimeOffset evaluatedAt) =>
        NavigationReplayCapture.Record("registration.observation", new
        {
            trip = activeDestinationVersion, geometry = route.GeometryFingerprint,
            reading = ReplayReading(reading), update, motion, decision,
            supportedAtUtc = _routeDisplayProgressPolicy.RegistrationSupportedAt,
            evaluatedAtUtc = evaluatedAt
        });

    [Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogReplayPdr(RouteResult route, double advanceMeters,
        RouteProgressTracker.RouteProgressUpdate update) =>
        NavigationReplayCapture.Record("navigation.pdr", new
        {
            trip = activeDestinationVersion, geometry = route.GeometryFingerprint,
            advanceMeters, update, evaluatedAtUtc = NavigationClock.UtcNow
        });

    [Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogReplayArrivalObservation(LocationReading reading, GeoCoordinate destination,
        double radiusMeters, double remainingMeters, bool majorRoadBarrier, long trip,
        bool geographicHeld, SafeZoneConfirmationService.SafeZoneDecision decision, DateTimeOffset evaluatedAt) =>
        NavigationReplayCapture.Record("arrival.observation", new
        {
            trip, reading = ReplayReading(reading), destination, radiusMeters, remainingMeters,
            majorRoadBarrier, geographicHeld, decision, evaluatedAtUtc = evaluatedAt
        });

    [Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogReplayUserArrival(SafeZoneConfirmationService.SafeZoneDecision decision,
        bool eligible) => NavigationReplayCapture.Record("arrival.user", new
        {
            trip = activeDestinationVersion, eligible, decision,
            evaluatedAtUtc = NavigationClock.UtcNow
        });
}
