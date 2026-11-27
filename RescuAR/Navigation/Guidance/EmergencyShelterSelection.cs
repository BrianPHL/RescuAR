using System;
using System.Collections.Generic;
using System.Linq;
using RescuAR.Navigation.Data;
using RescuAR.Navigation.Hazards;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Guidance;

public static class EmergencyShelterSelection
{
    private static readonly TimeSpan MaximumDirectoryAge = TimeSpan.FromMinutes(2);

    public sealed record Shelter(string Id, string Name, GeoCoordinate Coordinate,
        string Status, int Capacity, int? Occupancy);

    public static bool HasCurrentDirectory(bool isLive, DateTimeOffset? updatedAt, DateTimeOffset now) =>
        isLive && updatedAt is { } at && now >= at && now - at <= MaximumDirectoryAge;

    public static bool IsEligible(Shelter shelter) =>
        !string.IsNullOrWhiteSpace(shelter.Id) && !string.IsNullOrWhiteSpace(shelter.Name) &&
        shelter.Coordinate.IsValid && shelter.Coordinate != default &&
        shelter.Capacity > 0 && shelter.Occupancy is int occupancy && occupancy >= 0 && occupancy < shelter.Capacity &&
        (string.Equals(shelter.Status?.Trim(), "Open", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(shelter.Status?.Trim(), "Operational", StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<Shelter> NearbyEligible(IEnumerable<Shelter> shelters, GeoCoordinate origin) =>
        shelters.Where(IsEligible).OrderBy(shelter => origin.DistanceTo(shelter.Coordinate))
            .ThenBy(shelter => shelter.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(shelter => shelter.Id, StringComparer.Ordinal);

    public static bool IsRouteUsable(RouteResult? route, GeoCoordinate origin, GeoCoordinate shelter,
        IReadOnlyList<RouteHazard> hazards)
    {
        if (route is not { Points.Count: >= 2 } ||
            !double.IsFinite(route.TotalDistanceMeters) || route.TotalDistanceMeters <= 0 ||
            route.Points.Any(point => !point.Coordinate.IsValid))
            return false;

        GeoCoordinate start = route.Points[0].Coordinate;
        GeoCoordinate end = route.Points[^1].Coordinate;
        return origin.DistanceTo(start) <= 50 &&
            shelter.DistanceTo(end) <= EvacuationRoutingPolicy.MaximumDestinationSnapMeters &&
            !RouteHazardGeometry.IsDestinationExcluded(shelter, hazards) &&
            RouteHazardGeometry.IsSegmentSafeFromOrigin(origin, start, origin, hazards) &&
            RouteHazardGeometry.RouteAvoidsHazardsFromOrigin(route, origin, hazards) &&
            RouteHazardGeometry.IsSegmentSafeFromOrigin(end, shelter, origin, hazards);
    }
}
