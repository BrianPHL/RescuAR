using System;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Projection;

namespace RescuAR.Navigation.Progress;

/// <summary>
/// Places the mapped road at the observed GPS match independently of smoothed
/// trip progress. PDR advances only from that display anchor between GPS fixes.
/// This affects rendering references, never routing origins or arrival evidence.
/// </summary>
public sealed class RouteDisplayProgressPolicy
{
    public const int MaximumPdrGpsAgeSeconds = 8;
    public const double MaximumPdrAdvanceMeters = 8;
    private RouteResult? currentRoute;
    private DateTimeOffset? observedAt;
    private DisplayPosition gpsPosition;
    private double committedAtGps;
    private RegistrationObservation? registration;
    private RegistrationObservation? pendingRegistration;
    private DateTimeOffset? registrationSupportedAt;
    private DateTimeOffset? lastRegistrationObservation;
    public RegistrationDecision LastRegistrationDecision { get; private set; }

    private readonly record struct RegistrationObservation(GeoCoordinate Coordinate,
        double Accuracy, DateTimeOffset At, RouteBacktrackingPolicy.TrackedMotion Motion);
    public readonly record struct RegistrationDecision(string State, double InnovationMeters, bool Changed);
    public readonly record struct RegisteredReference(GeoCoordinate Coordinate, float WorldX, float WorldZ,
        long Session, long TrackingEpoch, long WorldFrame, double YawDegrees, DateTimeOffset RegisteredAt,
        DateTimeOffset SupportedAt);

    public void Reset()
    {
        currentRoute = null;
        observedAt = null;
        gpsPosition = default;
        committedAtGps = 0;
        registration = pendingRegistration = null;
        registrationSupportedAt = lastRegistrationObservation = null;
        LastRegistrationDecision = default;
    }

    public void ObserveGps(RouteResult route, RouteProgressTracker.RouteProgressUpdate update,
        DateTimeOffset timestamp)
    {
        if (!ReferenceEquals(currentRoute, route)) Reset();
        currentRoute = route;
        if (observedAt.HasValue && timestamp <= observedAt.Value) return;
        if (!TryResolveGps(route, update, out var position)) return;
        observedAt = timestamp;
        gpsPosition = position;
        committedAtGps = update.CommittedProgressMeters;
    }

    public bool TryResolvePdr(RouteResult route, double committedProgress, DateTimeOffset now,
        out DisplayPosition position)
    {
        position = default;
        if (!ReferenceEquals(currentRoute, route) || !observedAt.HasValue ||
            now < observedAt.Value || now - observedAt.Value > TimeSpan.FromSeconds(MaximumPdrGpsAgeSeconds))
            return false;
        double advance = committedProgress - committedAtGps;
        if (!double.IsFinite(advance) || advance < 0 || advance > MaximumPdrAdvanceMeters ||
            !TryAtDistance(route, Math.Min(route.Points[^1].DistanceFromStartMeters,
                gpsPosition.ProgressMeters + advance), out position)) return false;

        // Retain the last observed GPS-to-road offset rather than centering the
        // road on the camera on every step. This bounded reference is used only
        // by the AR projector; it is not published as a device location.
        position = position with { AlignmentCoordinate = new GeoCoordinate(
            gpsPosition.AlignmentCoordinate.Latitude + position.RoadCoordinate.Latitude - gpsPosition.RoadCoordinate.Latitude,
            gpsPosition.AlignmentCoordinate.Longitude + position.RoadCoordinate.Longitude - gpsPosition.RoadCoordinate.Longitude) };
        return position.AlignmentCoordinate.IsValid;
    }

    public bool TryGetObservedPosition(RouteResult route, DateTimeOffset now, out DisplayPosition position)
    {
        position = gpsPosition;
        return ReferenceEquals(currentRoute, route) && observedAt.HasValue && now >= observedAt.Value &&
            now - observedAt.Value <= TimeSpan.FromSeconds(MaximumPdrGpsAgeSeconds);
    }

    /// <summary>GPS eligibility and permission to move the world registration are separate decisions.</summary>
    public RegistrationDecision ObserveRegistration(RouteResult route, RouteProgressTracker.RouteProgressUpdate update,
        DateTimeOffset at, DateTimeOffset now, RouteBacktrackingPolicy.TrackedMotion? acquisitionMotion)
    {
        if (!ReferenceEquals(currentRoute, route) || !TryResolveGps(route, update, out _) ||
            now < at || now - at > TimeSpan.FromSeconds(3) || acquisitionMotion is not { IsValid: true } motion)
            return LastRegistrationDecision = new("UNPAIRED_OR_UNQUALIFIED", double.NaN, false);
        if (lastRegistrationObservation.HasValue && at <= lastRegistrationObservation.Value)
            return LastRegistrationDecision;
        lastRegistrationObservation = at;
        var next = new RegistrationObservation(update.GpsCoordinate, update.AccuracyMeters!.Value, at, motion);
        bool continuous = registration is { } previous && SameFrame(previous.Motion, motion) &&
            registrationSupportedAt.HasValue && at >= registrationSupportedAt.Value &&
            at - registrationSupportedAt.Value <= TimeSpan.FromSeconds(MaximumPdrGpsAgeSeconds);
        if (!continuous)
        {
            registration = next;
            pendingRegistration = null;
            registrationSupportedAt = at;
            return LastRegistrationDecision = new("ACQUISITION_SEEDED", 0, true);
        }

        double innovation = RegistrationResidual(registration!.Value, next);
        if (innovation <= RoutePositionObservationPolicy.MotionAllowanceMeters)
        {
            // Keep the same transform. A fresh consistent fix renews support,
            // rather than attaching a stationary road to the current camera.
            pendingRegistration = null;
            registrationSupportedAt = at;
            return LastRegistrationDecision = new("REFERENCE_SUPPORTED", innovation, false);
        }
        bool correctionConfirmed = pendingRegistration is { } candidate && SameFrame(candidate.Motion, motion) &&
            at - candidate.At >= TimeSpan.FromSeconds(1) &&
            at - candidate.At <= TimeSpan.FromSeconds(MaximumPdrGpsAgeSeconds) &&
            RegistrationResidual(candidate, next) <= RoutePositionObservationPolicy.MotionAllowanceMeters;
        if (correctionConfirmed)
        {
            registration = next;
            pendingRegistration = null;
            registrationSupportedAt = at;
            return LastRegistrationDecision = new("COHERENT_CORRECTION", innovation, true);
        }

        pendingRegistration = next;
        // An uncertainty-overlapping fix supports retaining the old reference,
        // but cannot independently establish a different physical placement.
        if (innovation <= registration.Value.Accuracy + next.Accuracy + RoutePositionObservationPolicy.MotionAllowanceMeters)
            registrationSupportedAt = at;
        return LastRegistrationDecision = new("CORRECTION_AWAITING_EVIDENCE", innovation, false);
    }

    public bool TryGetRegisteredReference(RouteResult route, GeoCoordinate coordinate, DateTimeOffset now,
        RouteBacktrackingPolicy.TrackedMotion current, out RegisteredReference reference)
    {
        reference = default;
        if (!ReferenceEquals(currentRoute, route) || registration is not { } retained ||
            !coordinate.IsValid || !SameFrame(retained.Motion, current) || !registrationSupportedAt.HasValue ||
            now < registrationSupportedAt.Value ||
            now - registrationSupportedAt.Value > TimeSpan.FromSeconds(MaximumPdrGpsAgeSeconds)) return false;
        var offset = Offset(retained.Coordinate, coordinate, retained.Motion.YawDegrees);
        float x = retained.Motion.X + (float)offset.X, z = retained.Motion.Z + (float)offset.Z;
        if (!float.IsFinite(x) || !float.IsFinite(z)) return false;
        reference = new(coordinate, x, z, current.Session, current.TrackingEpoch, current.WorldFrame,
            retained.Motion.YawDegrees, retained.At, registrationSupportedAt.Value);
        return true;
    }

    private static bool SameFrame(RouteBacktrackingPolicy.TrackedMotion a, RouteBacktrackingPolicy.TrackedMotion b) =>
        a.IsContinuousWith(b) && Math.Abs(Math.IEEERemainder(a.YawDegrees - b.YawDegrees, 360)) < 0.001;

    private static double RegistrationResidual(RegistrationObservation from, RegistrationObservation to)
    {
        var offset = Offset(from.Coordinate, to.Coordinate, to.Motion.YawDegrees);
        return Math.Sqrt(Math.Pow(to.Motion.X - from.Motion.X - offset.X, 2) +
            Math.Pow(to.Motion.Z - from.Motion.Z - offset.Z, 2));
    }

    private static (double X, double Z) Offset(GeoCoordinate from, GeoCoordinate to, double yaw) =>
        MapToArCoordinates.Rotate((to.Longitude - from.Longitude) * Math.PI / 180 * 6371008.8 *
            Math.Cos(from.Latitude * Math.PI / 180), (to.Latitude - from.Latitude) * Math.PI / 180 * 6371008.8, yaw);

    public static bool TryResolveGps(RouteResult route, RouteProgressTracker.RouteProgressUpdate update,
        out DisplayPosition position)
    {
        position = default;
        if (!update.IsAccepted || update.IsOffRoute || update.MatchConfidence < RouteMatchConfidence.Medium ||
            update.AccuracyMeters is not double accuracy || !double.IsFinite(accuracy) || accuracy < 0 || accuracy > 20 ||
            !update.GpsCoordinate.IsValid || !TryAtDistance(route, update.RawProgressMeters, out position) ||
            update.GpsCoordinate.DistanceTo(position.RoadCoordinate) > 20) return false;
        position = position with { AlignmentCoordinate = update.GpsCoordinate };
        return true;
    }

    public static bool TryAtDistance(RouteResult route, double progress, out DisplayPosition position)
    {
        position = default;
        if (route.Points.Count < 2 || !double.IsFinite(progress) ||
            progress < route.Points[0].DistanceFromStartMeters - 0.01 ||
            progress > route.Points[^1].DistanceFromStartMeters + 0.01) return false;
        progress = Math.Clamp(progress, route.Points[0].DistanceFromStartMeters,
            route.Points[^1].DistanceFromStartMeters);
        for (int i = 0; i < route.Points.Count - 1; i++)
        {
            var start = route.Points[i];
            var end = route.Points[i + 1];
            double span = end.DistanceFromStartMeters - start.DistanceFromStartMeters;
            if (end.DistanceFromStartMeters < progress || span <= 0.0001) continue;
            double t = Math.Clamp((progress - start.DistanceFromStartMeters) / span, 0, 1);
            var coordinate = new GeoCoordinate(
                start.Coordinate.Latitude + (end.Coordinate.Latitude - start.Coordinate.Latitude) * t,
                start.Coordinate.Longitude + (end.Coordinate.Longitude - start.Coordinate.Longitude) * t);
            if (!coordinate.IsValid) return false;
            position = new(progress, coordinate, i, coordinate);
            return true;
        }
        return false;
    }

    public readonly record struct DisplayPosition(double ProgressMeters, GeoCoordinate RoadCoordinate,
        int SegmentIndex, GeoCoordinate AlignmentCoordinate);
}
