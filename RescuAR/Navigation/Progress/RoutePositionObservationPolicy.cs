using System;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Projection;

namespace RescuAR.Navigation.Progress;

/// <summary>Checks GPS innovations against a bounded, continuously tracked camera world.</summary>
public sealed class RoutePositionObservationPolicy
{
    private RouteResult? currentRoute;
    private Observation? baseline;
    private Observation? candidate;
    private DateTimeOffset? lastObservation;
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(RouteDisplayProgressPolicy.MaximumPdrGpsAgeSeconds);
    // Small pose/timestamp uncertainty, used only to reject contradictory movement.
    public const double MotionAllowanceMeters = 2;

    private readonly record struct Observation(GeoCoordinate Coordinate, double Accuracy,
        DateTimeOffset At, RouteBacktrackingPolicy.TrackedMotion Motion);
    public readonly record struct Decision(bool IsHeld, bool IsConfirmedCorrection, double ResidualMeters, string Reason);

    public void Reset()
    {
        currentRoute = null;
        baseline = candidate = null;
        lastObservation = null;
    }

    public void Seed(RouteResult route, GeoCoordinate coordinate, double? accuracy,
        DateTimeOffset observedAt, DateTimeOffset now, RouteBacktrackingPolicy.TrackedMotion? motion)
    {
        if (!ReferenceEquals(currentRoute, route)) Evaluate(route, coordinate, accuracy, observedAt, now, motion);
    }

    public Decision Evaluate(RouteResult route, GeoCoordinate coordinate, double? accuracy,
        DateTimeOffset observedAt, DateTimeOffset now, RouteBacktrackingPolicy.TrackedMotion? motion)
    {
        if (!ReferenceEquals(currentRoute, route)) { Reset(); currentRoute = route; }
        if (observedAt > now.AddSeconds(2) || now - observedAt > TimeSpan.FromSeconds(5))
            return new(true, false, double.NaN, "stale or future positioning observation");
        if (lastObservation.HasValue && observedAt <= lastObservation.Value)
            return new(true, false, double.NaN, "duplicate or older positioning observation");
        lastObservation = observedAt;
        if (!coordinate.IsValid || accuracy is not double precision || !double.IsFinite(precision) || precision < 0)
            return new(false, false, double.NaN, "unqualified GPS; ordinary accuracy gates apply");
        if (now - observedAt < TimeSpan.Zero || now - observedAt > TimeSpan.FromSeconds(3) ||
            motion is not { IsValid: true } tracked)
        {
            baseline = candidate = null;
            return new(false, false, double.NaN, "continuous motion comparison unavailable; ordinary GPS gates apply");
        }

        var observation = new Observation(coordinate, precision, observedAt, tracked);
        bool comparable = baseline is { } previous && tracked.IsContinuousWith(previous.Motion) &&
            observedAt - previous.At <= Lifetime;
        if (!comparable)
        {
            baseline = precision <= 20 ? observation : null;
            candidate = null;
            return new(false, false, double.NaN, "new positioning baseline; ordinary GPS gates apply");
        }
        if (precision > 30)
            return new(true, false, double.NaN, "weak GPS held within bounded tracked continuity");

        double residual = Residual(baseline!.Value, observation);
        if (residual <= baseline.Value.Accuracy + precision + MotionAllowanceMeters)
        {
            if (precision <= 20) baseline = observation;
            candidate = null;
            return new(false, false, residual, "GPS movement agrees within combined uncertainty");
        }
        bool confirmed = precision <= 20 && candidate is { } pending &&
            pending.Motion.IsContinuousWith(tracked) && observedAt - pending.At >= TimeSpan.FromSeconds(1) &&
            observedAt - pending.At <= Lifetime &&
            Residual(pending, observation) <= pending.Accuracy + precision + MotionAllowanceMeters;
        if (confirmed)
        {
            baseline = observation;
            candidate = null;
            return new(false, true, residual, "repeated coherent GPS observations confirm a positioning correction");
        }
        if (precision <= 20) candidate = observation;
        return new(true, false, residual, "GPS jump contradicts tracked movement; waiting for independent confirmation");
    }

    private static double Residual(Observation from, Observation to)
    {
        double latitude = from.Coordinate.Latitude * Math.PI / 180;
        double east = (to.Coordinate.Longitude - from.Coordinate.Longitude) * Math.PI / 180 * 6371008.8 * Math.Cos(latitude);
        double north = (to.Coordinate.Latitude - from.Coordinate.Latitude) * Math.PI / 180 * 6371008.8;
        var gps = MapToArCoordinates.Rotate(east, north, to.Motion.YawDegrees);
        return Math.Sqrt(Math.Pow(to.Motion.X - from.Motion.X - gps.X, 2) +
            Math.Pow(to.Motion.Z - from.Motion.Z - gps.Z, 2));
    }
}
