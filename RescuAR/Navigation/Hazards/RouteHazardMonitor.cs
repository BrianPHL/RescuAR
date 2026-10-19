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

    public readonly record struct RetryContext(RouteResult Route, string Snapshot,
        bool InternetAvailable, int CurrentSegment);
    public readonly record struct Assessment(RouteHazardGeometry.RouteHazardIntersection Unsafe,
        RouteHazard? Escaping, RetryContext Retry);

    public void Reset()
    {
        currentRoute = null;
        escapeHazards.Clear();
        unavailableSegments.Clear();
        latestContext = null;
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
        bool internetAvailable)
    {
        var best = RouteHazardGeometry.RouteHazardIntersection.None;
        RouteHazard? escaping = null;
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
            if (acceptedEscape && hasPosition && position.RoadCoordinate.DistanceTo(hazard.Coordinate) <= hazard.RadiusMeters)
                escaping ??= hazard;
        }
        var context = new RetryContext(route, Fingerprint(hazards), internetAvailable,
            hasPosition ? position.SegmentIndex : -1);
        if (sameRoute)
        {
            if (!HasCurrentInputs(context)) unavailableSegments.Clear();
            latestContext = context;
        }
        return new(best, escaping, context);
    }

    private bool HasCurrentInputs(RetryContext context) =>
        ReferenceEquals(currentRoute, context.Route) && latestContext is { } latest &&
        latest.Snapshot == context.Snapshot && latest.InternetAvailable == context.InternetAvailable;

    public bool IsCurrentContext(RetryContext context) => HasCurrentInputs(context) &&
        latestContext!.Value.CurrentSegment == context.CurrentSegment;

    public bool CanRetry(RetryContext context) => HasCurrentInputs(context) &&
        !unavailableSegments.Contains(context.CurrentSegment);

    public void MarkNoRoute(RetryContext context)
    {
        // A stale async completion cannot inhibit a new route or newer snapshot.
        if (HasCurrentInputs(context)) unavailableSegments.Add(context.CurrentSegment);
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
