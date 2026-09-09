using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Projection;

/// <summary>
/// One geographic route point expressed in a local tangent plane.
///
/// EastMeters:
///   +X-like navigation direction toward geographic east.
///
/// NorthMeters:
///   +Z-like navigation direction toward geographic north.
///
/// This type deliberately does not depend on Evergine. A separate AR alignment
/// step can rotate East/North into the active ARCore world frame.
/// </summary>
public sealed record LocalRoutePoint(
    GeoCoordinate Coordinate,
    double EastMeters,
    double NorthMeters,
    double DistanceFromWindowStartMeters);
