using RescuAR.Navigation.Models;
using System;

namespace RescuAR.Navigation.Progress;

/// <summary>
/// Validates a dynamic replacement route before it can atomically replace the
/// active route, progress state, AR geometry, and displayed distance.
/// </summary>
public sealed class RouteReplacementPolicy
{
    private const double MaximumEndpointOffsetMeters =
        150.0;

    private const double MinimumGeometryConsistencyAllowanceMeters =
        75.0;

    private const double MaximumGeometryDifferenceRatio =
        0.35;

    private const double MinimumSuspiciousIncreaseMeters =
        150.0;

    private const double SuspiciousIncreaseRatio =
        0.75;

    private const double MinimumCandidateAgreementMeters =
        50.0;

    private const double CandidateAgreementRatio =
        0.20;

    private static readonly TimeSpan CandidateConfirmationWindow =
        TimeSpan.FromMinutes(
            2.0);

    private PendingCandidate? pendingCandidate;

    public void Reset()
    {
        pendingCandidate =
            null;
    }

    public RouteReplacementDecision Evaluate(
        RouteResult? currentRoute,
        RouteProgressTracker.ProgressSnapshot currentProgress,
        RouteResult replacementRoute,
        GeoCoordinate requestedOrigin,
        GeoCoordinate requestedDestination,
        bool explicitlyJustifiedDetour,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(
            replacementRoute);

        if (replacementRoute.Points.Count <
            2)
        {
            return Reject(
                "replacement route has fewer than two points");
        }

        if (!double.IsFinite(
                replacementRoute.TotalDistanceMeters) ||
            replacementRoute.TotalDistanceMeters <=
                0.0)
        {
            return Reject(
                "replacement route distance is invalid");
        }

        double previousPointDistanceMeters =
            -1.0;

        for (int pointIndex = 0;
             pointIndex < replacementRoute.Points.Count;
             pointIndex++)
        {
            RoutePoint point =
                replacementRoute.Points[pointIndex];

            if (!point.Coordinate.IsValid ||
                !double.IsFinite(
                    point.DistanceFromStartMeters) ||
                point.DistanceFromStartMeters <
                    0.0 ||
                point.DistanceFromStartMeters <
                    previousPointDistanceMeters)
            {
                return Reject(
                    $"replacement route point {pointIndex} has invalid or non-monotonic geometry");
            }

            previousPointDistanceMeters =
                point.DistanceFromStartMeters;
        }

        RoutePoint firstPoint =
            replacementRoute.Points[0];

        RoutePoint lastPoint =
            replacementRoute.Points[^1];

        if (lastPoint.DistanceFromStartMeters <=
            0.0)
        {
            return Reject(
                "replacement route geometry is invalid");
        }

        double startOffsetMeters =
            requestedOrigin.DistanceTo(
                firstPoint.Coordinate);

        double destinationOffsetMeters =
            requestedDestination.DistanceTo(
                lastPoint.Coordinate);

        if (!double.IsFinite(
                startOffsetMeters) ||
            startOffsetMeters >
                MaximumEndpointOffsetMeters)
        {
            return Reject(
                $"replacement route begins {startOffsetMeters:F1} m from the requested origin");
        }

        if (!double.IsFinite(
                destinationOffsetMeters) ||
            destinationOffsetMeters >
                MaximumEndpointOffsetMeters)
        {
            return Reject(
                $"replacement route ends {destinationOffsetMeters:F1} m from the destination");
        }

        double geometryDistanceMeters =
            lastPoint.DistanceFromStartMeters;

        double geometryDifferenceMeters =
            Math.Abs(
                replacementRoute.TotalDistanceMeters -
                geometryDistanceMeters);

        double geometryAllowanceMeters =
            Math.Max(
                MinimumGeometryConsistencyAllowanceMeters,
                geometryDistanceMeters *
                    MaximumGeometryDifferenceRatio);

        if (geometryDifferenceMeters >
            geometryAllowanceMeters)
        {
            return Reject(
                "replacement route distance disagrees with its geometry");
        }

        double previousRemainingMeters =
            currentProgress.HasRoute
                ? Math.Max(
                    0.0,
                    currentProgress.RemainingMeters)
                : currentRoute is null
                    ? 0.0
                    : Math.Max(
                        0.0,
                        currentRoute.TotalDistanceMeters);

        double distanceIncreaseMeters =
            replacementRoute.TotalDistanceMeters -
            previousRemainingMeters;

        double suspiciousIncreaseThresholdMeters =
            Math.Max(
                MinimumSuspiciousIncreaseMeters,
                previousRemainingMeters *
                    SuspiciousIncreaseRatio);

        if (explicitlyJustifiedDetour ||
            previousRemainingMeters <=
                0.0 ||
            distanceIncreaseMeters <=
                suspiciousIncreaseThresholdMeters)
        {
            pendingCandidate =
                null;

            return Accept(
                previousRemainingMeters,
                replacementRoute.TotalDistanceMeters,
                distanceIncreaseMeters,
                explicitlyJustifiedDetour
                    ? "replacement distance increase is explained by an active hazard detour"
                    : "replacement route passed endpoint, geometry, and distance checks");
        }

        if (pendingCandidate.HasValue &&
            timestampUtc >=
                pendingCandidate.Value.TimestampUtc &&
            timestampUtc -
                pendingCandidate.Value.TimestampUtc <=
                    CandidateConfirmationWindow &&
            pendingCandidate.Value.Destination.DistanceTo(
                requestedDestination) <=
                    5.0 &&
            string.Equals(
                pendingCandidate.Value.Algorithm,
                replacementRoute.Algorithm,
                StringComparison.Ordinal) &&
            Math.Abs(
                pendingCandidate.Value.DistanceMeters -
                replacementRoute.TotalDistanceMeters) <=
                    Math.Max(
                        MinimumCandidateAgreementMeters,
                        pendingCandidate.Value.DistanceMeters *
                            CandidateAgreementRatio))
        {
            pendingCandidate =
                null;

            return Accept(
                previousRemainingMeters,
                replacementRoute.TotalDistanceMeters,
                distanceIncreaseMeters,
                "large distance increase was confirmed by a second consistent route result");
        }

        pendingCandidate =
            new PendingCandidate(
                requestedDestination,
                replacementRoute.Algorithm,
                replacementRoute.TotalDistanceMeters,
                timestampUtc);

        return new RouteReplacementDecision(
            RouteReplacementDisposition.RequiresConfirmation,
            previousRemainingMeters,
            replacementRoute.TotalDistanceMeters,
            distanceIncreaseMeters,
            "large unexplained distance increase requires a second consistent reroute result");
    }

    private RouteReplacementDecision Reject(
        string reason)
    {
        pendingCandidate =
            null;

        return new RouteReplacementDecision(
            RouteReplacementDisposition.Rejected,
            0.0,
            0.0,
            0.0,
            reason);
    }

    private static RouteReplacementDecision Accept(
        double previousRemainingMeters,
        double replacementDistanceMeters,
        double distanceIncreaseMeters,
        string reason)
    {
        return new RouteReplacementDecision(
            RouteReplacementDisposition.Accepted,
            previousRemainingMeters,
            replacementDistanceMeters,
            distanceIncreaseMeters,
            reason);
    }

    private readonly record struct PendingCandidate(
        GeoCoordinate Destination,
        string Algorithm,
        double DistanceMeters,
        DateTimeOffset TimestampUtc);
}

public enum RouteReplacementDisposition
{
    Rejected = 0,
    RequiresConfirmation = 1,
    Accepted = 2
}

public readonly record struct RouteReplacementDecision(
    RouteReplacementDisposition Disposition,
    double PreviousRemainingMeters,
    double ReplacementDistanceMeters,
    double DistanceIncreaseMeters,
    string Reason)
{
    public bool IsAccepted =>
        Disposition ==
        RouteReplacementDisposition.Accepted;
}
