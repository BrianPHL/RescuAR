using System;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Projection;
using RescuAR.AR;
using System.Numerics;

namespace RescuAR.Navigation.Progress;

/// <summary>
/// Retains one GPS-to-AR transform and matches tracked movement to its route.
/// PDR trip progress and rendering do not independently move the geographic origin.
/// This affects rendering references, never routing origins or arrival evidence.
/// </summary>
public sealed class RouteDisplayProgressPolicy
{
    public const int MaximumPdrGpsAgeSeconds = 8;
    public const double MaximumPdrAdvanceMeters = 8;
    private readonly TimeSpan frameRequalificationLifetime;
    private readonly bool requireInitialConfirmation;
    private RouteResult? currentRoute;
    private DateTimeOffset? observedAt;
    private DisplayPosition gpsPosition;
    private double committedAtGps;
    private RegistrationObservation? registration;
    private RegistrationObservation? pendingRegistration;
    private DateTimeOffset? registrationSupportedAt;
    private DateTimeOffset? lastRegistrationObservation;
    private int registrationConfirmations;
    private readonly GpsRegistrationCorrectionPolicy registrationCorrections = new();
    private double? trackedDisplayProgress;
    private RouteBacktrackingPolicy.TrackedMotion? lastDisplayMotion;
    private DateTimeOffset? aStarReplacementObservationFloor;
    public RegistrationDecision LastRegistrationDecision { get; private set; }

    private readonly record struct RegistrationObservation(GeoCoordinate Coordinate,
        double Accuracy, DateTimeOffset At, RouteBacktrackingPolicy.TrackedMotion Motion);
    public readonly record struct RegistrationDecision(string State, double InnovationMeters, bool Changed,
        double CorrectionX = 0, double CorrectionZ = 0, int EvidenceCount = 0);
    public readonly record struct RegisteredReference(GeoCoordinate Coordinate, float WorldX, float WorldZ,
        long Session, long TrackingEpoch, long WorldFrame, double YawDegrees, DateTimeOffset RegisteredAt,
        DateTimeOffset SupportedAt, string Source = "QUALIFIED_ACQUISITION");

    public RouteDisplayProgressPolicy(NavigationStabilityOptions? options = null, bool requireInitialConfirmation = false)
    {
        var configured = options ?? new NavigationStabilityOptions();
        configured.Validate();
        frameRequalificationLifetime = configured.RecoveryLifetime;
        this.requireInitialConfirmation = requireInitialConfirmation;
    }

    public DateTimeOffset? RegistrationSupportedAt => registrationSupportedAt;
    public DateTimeOffset? PendingRegistrationAt => pendingRegistration?.At;
    public double? RegistrationAccuracyMeters => registration?.Accuracy;

    public void Reset()
    {
        currentRoute = null;
        observedAt = null;
        gpsPosition = default;
        committedAtGps = 0;
        registration = pendingRegistration = null;
        registrationSupportedAt = lastRegistrationObservation = null;
        registrationConfirmations = 0;
        registrationCorrections.Reset();
        trackedDisplayProgress = null;
        lastDisplayMotion = null;
        aStarReplacementObservationFloor = null;
        LastRegistrationDecision = default;
    }

    /// <summary>A provider change is not a new geographic world. Retain its
    /// transform, support age and correction evidence even when AR trust is held.</summary>
    public bool RebindForAStarReplacement(RouteResult previous, RouteResult replacement,
        AStarReplacementContinuityPolicy? continuity, RouteProgressTracker.RouteProgressUpdate update,
        DateTimeOffset observationTime)
    {
        if (!AStarRouteDisplayPolicy.AppliesTo(replacement)) return false;
        if (!ReferenceEquals(currentRoute, previous))
        {
            Reset();
            currentRoute = replacement;
            aStarReplacementObservationFloor = observationTime;
            return false;
        }
        bool mapped = continuity is not null && ReferenceEquals(continuity.Previous, previous) &&
            ReferenceEquals(continuity.Replacement, replacement);
        if (mapped && continuity!.TryMapPosition(gpsPosition, out var gps) &&
            continuity.TryMapProgress(committedAtGps, out double committed))
        {
            gpsPosition = gps;
            committedAtGps = committed;
        }
        else if (TryResolveGps(replacement, update, out var observed))
        {
            gpsPosition = observed;
            observedAt = observationTime;
            committedAtGps = update.CommittedProgressMeters;
        }
        else observedAt = null;
        if (mapped && trackedDisplayProgress is { } tracked &&
            continuity!.TryMapProgress(tracked, out double display)) trackedDisplayProgress = display;
        else { trackedDisplayProgress = null; lastDisplayMotion = null; }
        currentRoute = replacement;
        if (registration is null) aStarReplacementObservationFloor = observationTime;
        return registration.HasValue;
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

    /// <summary>A new polyline does not create a new AR world. Carry only the
    /// qualified geographic transform, never old progress or pending corrections.
    /// Failure leaves the previous route's registration untouched.</summary>
    public bool TryTransferRegistration(RouteResult previous, RouteResult replacement,
        RouteProgressTracker.RouteProgressUpdate update, DateTimeOffset at, DateTimeOffset now,
        RouteBacktrackingPolicy.TrackedMotion acquisitionMotion)
    {
        if (!ReferenceEquals(currentRoute, previous) || registration is not { } retained ||
            !TryResolveGps(replacement, update, out var position) || now < at ||
            now - at > TimeSpan.FromSeconds(3) || !acquisitionMotion.IsValid ||
            !TryGetRegisteredReference(previous, update.GpsCoordinate, now, acquisitionMotion,
                out _, frameRequalificationLifetime) ||
            RegistrationResidual(retained, new(update.GpsCoordinate, update.AccuracyMeters!.Value, at,
                acquisitionMotion)) > retained.Accuracy + update.AccuracyMeters.Value +
                    RoutePositionObservationPolicy.MotionAllowanceMeters)
            return false;

        var supportedAt = registrationSupportedAt;
        var lastObservation = lastRegistrationObservation;
        Reset();
        ObserveGps(replacement, update, at);
        registration = retained;
        registrationSupportedAt = supportedAt;
        lastRegistrationObservation = lastObservation;
        trackedDisplayProgress = position.ProgressMeters;
        lastDisplayMotion = acquisitionMotion;
        LastRegistrationDecision = new("ROUTE_REFERENCE_RETAINED", 0, false);
        return true;
    }

    public bool CanRetainRoadEntry(RouteResult route, RouteProgressTracker.RouteProgressUpdate update,
        DateTimeOffset now, RouteBacktrackingPolicy.TrackedMotion motion) =>
        RouteCorridorPolicy.CanEnterRoadFollowing(update.IsAccepted, update.IsOffRoute,
            update.CrossTrackErrorMeters, update.CorridorRadiusMeters, update.MatchConfidence, update.AccuracyMeters) &&
        TryGetRegisteredDisplayPosition(route, now, motion, out var tracked) &&
        tracked.RoadCoordinate.DistanceTo(tracked.AlignmentCoordinate) <=
            RouteCorridorPolicy.GetRoadFollowingEntryRadiusMeters(update.CorridorRadiusMeters);

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

    /// <summary>A GPS match whose movement was held cannot move the visible
    /// window independently. Its fresh registration may still support tracked
    /// movement on the same mapped polyline; this never changes GPS or trip progress.</summary>
    public bool TryGetNavigationDisplayPosition(RouteResult route, DateTimeOffset now,
        RouteBacktrackingPolicy.TrackedMotion motion, GpsPdrFusionPolicy.GpsFusionAction action,
        out DisplayPosition position)
    {
        // One retained transform owns MLD geometry. GPS updates its support and
        // bounded origin estimate; raw matched progress cannot independently drag it.
        return TryGetRegisteredDisplayPosition(route, now, motion, out position);
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
            if (aStarReplacementObservationFloor is { } floor && at <= floor)
                return LastRegistrationDecision = new("ASTAR_REPLACEMENT_AWAITING_NEW_OBSERVATION", double.NaN, false);
            if (requireInitialConfirmation || aStarReplacementObservationFloor.HasValue)
            {
                bool consistent = pendingRegistration is { } seed && SameFrame(seed.Motion, motion) &&
                    at - seed.At <= TimeSpan.FromSeconds(MaximumPdrGpsAgeSeconds) &&
                    RegistrationResidual(seed, next) <= RoutePositionObservationPolicy.MotionAllowanceMeters +
                        Math.Min(3, Math.Min(seed.Accuracy, next.Accuracy));
                if (!consistent)
                {
                    pendingRegistration = next;
                    registrationConfirmations = 1;
                    return LastRegistrationDecision = new("ACQUISITION_CONFIRMATION_PENDING", double.NaN, false, EvidenceCount: 1);
                }
                registrationConfirmations++;
                if (at - pendingRegistration!.Value.At < TimeSpan.FromSeconds(1) || registrationConfirmations < 2)
                    return LastRegistrationDecision = new("ACQUISITION_CONFIRMATION_PENDING", double.NaN, false,
                        EvidenceCount: registrationConfirmations);
            }
            registration = next;
            registrationCorrections.Reset();
            trackedDisplayProgress = gpsPosition.ProgressMeters;
            lastDisplayMotion = motion;
            pendingRegistration = null;
            registrationConfirmations = 0;
            registrationSupportedAt = at;
            aStarReplacementObservationFloor = null;
            return LastRegistrationDecision = new("ACQUISITION_SEEDED", 0, true);
        }
        if (!sameFrame)
        {
            registrationCorrections.Reset();
            trackedDisplayProgress = null;
            lastDisplayMotion = null;
            // New frame/calibration ownership needs separate, spaced evidence.
            if (pendingRegistration is not { } seed || !SameFrame(seed.Motion, motion) ||
                // Three fresh fixes at the observed five-second cadence span
                // ten seconds. The eight-second PDR display budget must not
                // repeatedly erase this separate frame-recovery evidence.
                at - seed.At > frameRequalificationLifetime ||
                RegistrationResidual(seed, next) > seed.Accuracy + next.Accuracy + RoutePositionObservationPolicy.MotionAllowanceMeters)
            {
                pendingRegistration = next;
                registrationConfirmations = 1;
                return LastRegistrationDecision = new("FRAME_REQUALIFICATION_PENDING", double.NaN, false,
                    EvidenceCount: registrationConfirmations);
            }
            registrationConfirmations++;
            if (at - seed.At < TimeSpan.FromSeconds(3) || registrationConfirmations < 3)
                return LastRegistrationDecision = new("FRAME_REQUALIFICATION_PENDING", double.NaN, false,
                    EvidenceCount: registrationConfirmations);
            registration = next;
            trackedDisplayProgress = gpsPosition.ProgressMeters;
            lastDisplayMotion = motion;
            pendingRegistration = null;
            registrationConfirmations = 0;
            registrationSupportedAt = at;
            return LastRegistrationDecision = new("FRAME_REQUALIFIED", 0, true, EvidenceCount: 3);
        }

        double innovation = RegistrationResidual(registration!.Value, next);
        if (innovation <= RoutePositionObservationPolicy.MotionAllowanceMeters)
        {
            registrationCorrections.ClearEvidence();
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
            registrationCorrections.Reset();
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

        var anchor = registration.Value;
        var projected = Offset(anchor.Coordinate, next.Coordinate, motion.YawDegrees);
        var residual = new Vector2(motion.X - anchor.Motion.X - (float)projected.X,
            motion.Z - anchor.Motion.Z - (float)projected.Z);
        if (registrationCorrections.Observe(residual, anchor.Accuracy, next.Accuracy, at, out var correction))
        {
            registration = anchor with { Motion = anchor.Motion with {
                X = anchor.Motion.X + correction.X, Z = anchor.Motion.Z + correction.Y } };
            pendingRegistration = null;
            registrationConfirmations = 0;
            registrationSupportedAt = at;
            return LastRegistrationDecision = new("GPS_FILTERED_BOUNDED_CORRECTION", innovation, true,
                correction.X, correction.Y, registrationCorrections.LastCorrectionSampleCount);
        }

        // An uncertainty-overlapping fix supports retaining the old reference,
        // but cannot independently establish a different physical placement.
        if (innovation <= registration.Value.Accuracy + next.Accuracy + RoutePositionObservationPolicy.MotionAllowanceMeters)
            registrationSupportedAt = at;
        return LastRegistrationDecision = new("CORRECTION_AWAITING_EVIDENCE", innovation, false,
            EvidenceCount: registrationConfirmations);
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

    /// <summary>Slides the visible window using tracked displacement inside one
    /// qualified transform. GPS may support registration, but cannot drag its geometry.</summary>
    public bool TryGetRegisteredDisplayPosition(RouteResult route, DateTimeOffset now,
        RouteBacktrackingPolicy.TrackedMotion motion, out DisplayPosition position)
    {
        position = default;
        if (route.Points.Count < 2 || !TryGetRegisteredReference(route, route.Points[0].Coordinate,
            now, motion, out var reference)) return false;
        if (lastDisplayMotion is { } previous &&
            Math.Sqrt(Math.Pow(motion.X - previous.X, 2) + Math.Pow(motion.Z - previous.Z, 2)) > MaximumPdrAdvanceMeters)
            return false;
        if (!TryProjectTrackedDisplayPosition(route, reference.Coordinate, reference.WorldX,
                reference.WorldZ, motion, trackedDisplayProgress ?? gpsPosition.ProgressMeters, out position)) return false;
        trackedDisplayProgress = position.ProgressMeters;
        lastDisplayMotion = motion;
        return true;
    }

    private static bool TryProjectTrackedDisplayPosition(RouteResult route, GeoCoordinate mapPoint,
        float worldX, float worldZ, RouteBacktrackingPolicy.TrackedMotion motion, double previousProgress,
        out DisplayPosition position)
    {
        position = default;
        double dx = motion.X - worldX, dz = motion.Z - worldZ;
        double angle = motion.YawDegrees * Math.PI / 180;
        double east = dx * Math.Cos(angle) - dz * Math.Sin(angle);
        double north = -dx * Math.Sin(angle) - dz * Math.Cos(angle);
        var estimated = new GeoCoordinate(mapPoint.Latitude + north / 6371008.8 * 180 / Math.PI,
            mapPoint.Longitude + east / (6371008.8 * Math.Cos(mapPoint.Latitude * Math.PI / 180)) * 180 / Math.PI);
        double best = double.PositiveInfinity, progress = 0;
        // Only this continuous progress interval is eligible. Locate its first
        // segment without rescanning an entire retained route on every pose.
        double minimumProgress = previousProgress - MaximumPdrAdvanceMeters;
        double maximumProgress = previousProgress + MaximumPdrAdvanceMeters;
        int firstSegment = Math.Max(0, RouteDistanceIndex.AtOrAfter(route.Points, minimumProgress) - 1);
        for (int i = firstSegment; i < route.Points.Count - 1 &&
             route.Points[i].DistanceFromStartMeters <= maximumProgress; i++)
        {
            var a = route.Points[i]; var b = route.Points[i + 1];
            double span = b.DistanceFromStartMeters - a.DistanceFromStartMeters;
            // Match a continuous subsection, rather than another nearby leg of a loop.
            double lower = Math.Max(a.DistanceFromStartMeters, previousProgress - MaximumPdrAdvanceMeters);
            double upper = Math.Min(b.DistanceFromStartMeters, previousProgress + MaximumPdrAdvanceMeters);
            if (span <= .0001 || upper < lower) continue;
            var start = Offset(estimated, a.Coordinate, 0); var end = Offset(estimated, b.Coordinate, 0);
            double x = end.X - start.X, z = end.Z - start.Z, length = x * x + z * z;
            if (length < .0001) continue;
            double t = Math.Clamp(-(start.X * x + start.Z * z) / length,
                (lower - a.DistanceFromStartMeters) / span, (upper - a.DistanceFromStartMeters) / span);
            double error = Math.Pow(start.X + t * x, 2) + Math.Pow(start.Z + t * z, 2);
            if (error >= best) continue;
            best = error; progress = a.DistanceFromStartMeters + t * (b.DistanceFromStartMeters - a.DistanceFromStartMeters);
        }
        if (best > 400 || !TryAtDistance(route, progress, out position)) return false;
        position = position with { AlignmentCoordinate = estimated };
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
        int firstSegment = Math.Max(0, RouteDistanceIndex.AtOrAfter(route.Points, progress) - 1);
        for (int i = firstSegment; i < route.Points.Count - 1; i++)
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
