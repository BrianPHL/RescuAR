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
    private int registrationConfirmations;
    public RegistrationDecision LastRegistrationDecision { get; private set; }

    private readonly record struct RegistrationObservation(GeoCoordinate Coordinate,
        double Accuracy, DateTimeOffset At, RouteBacktrackingPolicy.TrackedMotion Motion);
    public readonly record struct RegistrationDecision(string State, double InnovationMeters, bool Changed,
        double CorrectionX = 0, double CorrectionZ = 0, int EvidenceCount = 0);
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
        registrationConfirmations = 0;
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
        if (lastRegistrationObservation.HasValue && at - lastRegistrationObservation.Value < TimeSpan.FromSeconds(1))
            return LastRegistrationDecision;
        lastRegistrationObservation = at;
        var next = new RegistrationObservation(update.GpsCoordinate, update.AccuracyMeters!.Value, at, motion);
        // Losing GPS support does not create a new AR world. A single later
        // fix must not replace a retained transform in the same known frame.
        bool sameFrame = registration is { } previous && SameFrame(previous.Motion, motion);
        if (registration is null)
        {
            registration = next;
            pendingRegistration = null;
            registrationSupportedAt = at;
            return LastRegistrationDecision = new("ACQUISITION_SEEDED", 0, true);
        }
        if (!sameFrame)
        {
            // New frame/calibration ownership needs separate, spaced evidence.
            if (pendingRegistration is not { } seed || !SameFrame(seed.Motion, motion) ||
                at - seed.At > TimeSpan.FromSeconds(MaximumPdrGpsAgeSeconds) ||
                RegistrationResidual(seed, next) > seed.Accuracy + next.Accuracy + RoutePositionObservationPolicy.MotionAllowanceMeters)
            {
                pendingRegistration = next;
                registrationConfirmations = 1;
                return LastRegistrationDecision = new("FRAME_REQUALIFICATION_PENDING", double.NaN, false);
            }
            registrationConfirmations++;
            if (at - seed.At < TimeSpan.FromSeconds(3) || registrationConfirmations < 3)
                return LastRegistrationDecision = new("FRAME_REQUALIFICATION_PENDING", double.NaN, false);
            registration = next;
            pendingRegistration = null;
            registrationConfirmations = 0;
            registrationSupportedAt = at;
            return LastRegistrationDecision = new("FRAME_REQUALIFIED", 0, true, EvidenceCount: 3);
        }

        double innovation = RegistrationResidual(registration!.Value, next);
        if (innovation <= RoutePositionObservationPolicy.MotionAllowanceMeters)
        {
            // Keep the same transform. A fresh consistent fix renews support,
            // rather than attaching a stationary road to the current camera.
            pendingRegistration = null;
            registrationConfirmations = 0;
            registrationSupportedAt = at;
            return LastRegistrationDecision = new("REFERENCE_SUPPORTED", innovation, false);
        }
        // Similar consecutive GPS fixes are often correlated multipath, not
        // independent proof of a new world origin. Require improved precision,
        // sustained walking and a discrepancy outside the original uncertainty.
        bool coherent = pendingRegistration is { } candidate && SameFrame(candidate.Motion, motion) &&
            at > candidate.At && at - candidate.At <= TimeSpan.FromSeconds(30) &&
            RegistrationResidual(candidate, next) <= RoutePositionObservationPolicy.MotionAllowanceMeters;
        registrationConfirmations = coherent ? registrationConfirmations + 1 : 1;
        if (!coherent) pendingRegistration = next;
        var evidence = pendingRegistration!.Value;
        double walked = Math.Sqrt(Math.Pow(motion.X - evidence.Motion.X, 2) + Math.Pow(motion.Z - evidence.Motion.Z, 2));
        bool improvedPrecision = next.Accuracy <= 5 && next.Accuracy <= registration!.Value.Accuracy / 2;
        bool correctionConfirmed = coherent && registrationConfirmations >= 3 &&
            at - evidence.At >= TimeSpan.FromSeconds(MaximumPdrGpsAgeSeconds) && walked >= MaximumPdrAdvanceMeters &&
            improvedPrecision && innovation > registration.Value.Accuracy + next.Accuracy +
                RoutePositionObservationPolicy.MotionAllowanceMeters;
        if (correctionConfirmed)
        {
            var retained = registration!.Value;
            var offset = Offset(retained.Coordinate, next.Coordinate, motion.YawDegrees);
            double dx = motion.X - retained.Motion.X - offset.X, dz = motion.Z - retained.Motion.Z - offset.Z;
            double weight = Math.Min(1, RoutePositionObservationPolicy.MotionAllowanceMeters / innovation);
            registration = retained with { Accuracy = Math.Max(next.Accuracy, retained.Accuracy -
                RoutePositionObservationPolicy.MotionAllowanceMeters), Motion = retained.Motion with {
                    X = retained.Motion.X + (float)(dx * weight), Z = retained.Motion.Z + (float)(dz * weight) } };
            pendingRegistration = null;
            registrationConfirmations = 0;
            registrationSupportedAt = at;
            return LastRegistrationDecision = new("PRECISION_VALIDATED_BOUNDED_CORRECTION", innovation, true,
                dx * weight, dz * weight, 3);
        }

        // An uncertainty-overlapping fix supports retaining the old reference,
        // but cannot independently establish a different physical placement.
        if (innovation <= registration.Value.Accuracy + next.Accuracy + RoutePositionObservationPolicy.MotionAllowanceMeters)
            registrationSupportedAt = at;
        return LastRegistrationDecision = new("CORRECTION_AWAITING_EVIDENCE", innovation, false);
    }

    public bool TryGetRegisteredReference(RouteResult route, GeoCoordinate coordinate, DateTimeOffset now,
        RouteBacktrackingPolicy.TrackedMotion current, out RegisteredReference reference,
        TimeSpan? maximumSupportAge = null)
    {
        reference = default;
        if (!ReferenceEquals(currentRoute, route) || registration is not { } retained ||
            !coordinate.IsValid || !SameFrame(retained.Motion, current) || !registrationSupportedAt.HasValue ||
            now < registrationSupportedAt.Value ||
            now - registrationSupportedAt.Value > (maximumSupportAge ?? TimeSpan.FromSeconds(MaximumPdrGpsAgeSeconds))) return false;
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
