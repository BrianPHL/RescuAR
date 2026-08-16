namespace RescuAR.Navigation.Models;

/// <summary>
/// Canonical point in a route returned by any routing algorithm.
/// </summary>
public sealed record RoutePoint(
    GeoCoordinate Coordinate,
    double DistanceFromStartMeters);
