using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;

namespace RescuAR.Navigation.Hazards;

/// <summary>Retains the accepted escape contract and failed assessments for one route.</summary>
/// <remarks>The owning service serializes access. Hazard refresh timestamps are not revisions.</remarks>
public sealed class RouteHazardMonitor
{
    private RouteResult? currentRoute;
    private GeoCoordinate acceptedOrigin;
    private readonly Dictionary<string, RouteHazard> escapeHazards = new(StringComparer.Ordinal);
    private readonly HashSet<int> unavailableSegments = new();
    private RetryContext? latestContext;
    private string? excludedDestinationSnapshot;

    public readonly record struct RetryContext(RouteResult Route, string Snapshot,
        bool InternetAvailable, int CurrentSegment);
    public readonly record struct Assessment(RouteHazardGeometry.RouteHazardIntersection Unsafe,
        RouteHazard? Escaping, RetryContext Retry, double SafePrefixEndProgressMeters = double.PositiveInfinity);

    public void Reset()
    {
        currentRoute = null;
        escapeHazards.Clear();
        unavailableSegments.Clear();
        latestContext = null;
        excludedDestinationSnapshot = null;
    }

    public void AcceptRoute(RouteResult route, GeoCoordinate origin,
        IReadOnlyList<RouteHazard>? validatedHazards = null)
    {
        Reset();
        currentRoute = route;
        acceptedOrigin = origin;
        // Only a hazard-aware, validated replacement grants an escape exemption.
        // A new GPS fix inside an exclusion zone cannot grant one by itself.
        if (validatedHazards is null || !origin.IsValid || route.Points.Count < 2 ||
            validatedHazards.Any(h => route.Points[^1].Coordinate.DistanceTo(h.Coordinate) <= h.RadiusMeters) ||
            !RouteHazardGeometry.RouteAvoidsHazardsFromOrigin(route, origin, validatedHazards)) return;
        foreach (var hazard in validatedHazards)
            if (origin.DistanceTo(hazard.Coordinate) <= hazard.RadiusMeters)
                escapeHazards[hazard.Id] = hazard;
    }

    public Assessment Assess(RouteResult route, double progress, IReadOnlyList<RouteHazard> hazards,
        bool internetAvailable, string? snapshotFingerprint = null)
    {
        var best = RouteHazardGeometry.RouteHazardIntersection.None;
        RouteHazard? escaping = null;
        double safePrefixEnd = double.PositiveInfinity;
        bool sameRoute = ReferenceEquals(currentRoute, route);
        bool hasPosition = RouteDisplayProgressPolicy.TryAtDistance(route, progress, out var position);
        foreach (var hazard in hazards)
        {
            bool acceptedEscape = sameRoute && escapeHazards.TryGetValue(hazard.Id, out var original) &&
                SameExclusion(original, hazard);
            var one = new[] { hazard };
            var intersection = acceptedEscape
                ? RouteHazardGeometry.FindFirstUnsafeIntersectionFromOrigin(route, acceptedOrigin, one, progress)
                : RouteHazardGeometry.FindFirstIntersection(route, progress, one);
            if (intersection.IsAffected && (!best.IsAffected || intersection.DistanceAheadMeters < best.DistanceAheadMeters))
                best = intersection;
            // An unchanged, validated escape hazard cannot have a later
            // re-entry on this accepted route. All other exclusions clip at entry.
            if (!acceptedEscape && intersection.IsAffected)
                safePrefixEnd = Math.Min(safePrefixEnd, RouteHazardGeometry.FirstEntryProgress(route, progress, hazard));
            if (acceptedEscape && hasPosition && position.RoadCoordinate.DistanceTo(hazard.Coordinate) <= hazard.RadiusMeters)
                escaping ??= hazard;
        }
        var context = new RetryContext(route, snapshotFingerprint ?? Fingerprint(hazards), internetAvailable,
            hasPosition ? position.SegmentIndex : -1);
        if (sameRoute)
        {
            if (latestContext is { } latest && latest.Snapshot != context.Snapshot)
                excludedDestinationSnapshot = null;
            if (!HasCurrentInputs(context)) unavailableSegments.Clear();
            latestContext = context;
        }
        return new(best, escaping, context, safePrefixEnd);
    }

    private bool HasCurrentInputs(RetryContext context) =>
        ReferenceEquals(currentRoute, context.Route) && latestContext is { } latest &&
        latest.Snapshot == context.Snapshot && latest.InternetAvailable == context.InternetAvailable;

    public bool IsCurrentContext(RetryContext context) => HasCurrentInputs(context) &&
        latestContext!.Value.CurrentSegment == context.CurrentSegment;

    public bool CanRetry(RetryContext context) => HasCurrentInputs(context) &&
        !IsDestinationExcluded(context) && !unavailableSegments.Contains(context.CurrentSegment);

    public bool IsDestinationExcluded(RetryContext context) => ReferenceEquals(currentRoute, context.Route) &&
        excludedDestinationSnapshot is not null && excludedDestinationSnapshot == context.Snapshot;

    public bool MarkNoRoute(RetryContext context, RouteReplacementFailure reason = RouteReplacementFailure.NoRoute)
    {
        // A stale async completion cannot inhibit a new route or newer snapshot.
        if (reason == RouteReplacementFailure.DestinationExcluded)
        {
            if (!ReferenceEquals(currentRoute, context.Route) || latestContext is not { } latest ||
                latest.Snapshot != context.Snapshot) return false;
            excludedDestinationSnapshot = context.Snapshot;
        }
        else
        {
            if (!HasCurrentInputs(context)) return false;
            unavailableSegments.Add(context.CurrentSegment);
        }
        return true;
    }

    public void AllowRetryForPositionChange(RouteResult route)
    {
        if (ReferenceEquals(currentRoute, route)) unavailableSegments.Clear();
    }

    private static bool SameExclusion(RouteHazard a, RouteHazard b) => a.Id == b.Id &&
        a.Coordinate == b.Coordinate && a.RadiusMeters == b.RadiusMeters &&
        a.Category == b.Category && a.Severity == b.Severity;

    public static string Fingerprint(IReadOnlyList<RouteHazard> hazards)
    {
        var text = new StringBuilder();
        foreach (var hazard in hazards.OrderBy(h => h.Id, StringComparer.Ordinal))
        {
            text.Append(hazard.Id.Length).Append(':').Append(hazard.Id).Append('|')
                .Append(hazard.Coordinate.Latitude.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(hazard.Coordinate.Longitude.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(hazard.RadiusMeters.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(hazard.Category.Length).Append(':').Append(hazard.Category).Append('|')
                .Append(hazard.Severity.Length).Append(':').Append(hazard.Severity).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }
}
