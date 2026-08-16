using RescuAR.Navigation.Models;

namespace RescuAR.MAUI.Services.Location;

/// <summary>
/// Canonical device-location sample for navigation.
///
/// The geographic coordinate is expressed in WGS84 and can be consumed by the
/// road graph, map matching, PDR fusion, and routing layers without depending
/// on Microsoft.Maui.Devices.Sensors.Location.
/// </summary>
public sealed record LocationReading(
    GeoCoordinate Coordinate,
    double? AccuracyMeters,
    double? AltitudeMeters,
    double? SpeedMetersPerSecond,
    double? CourseDegrees,
    DateTimeOffset Timestamp);
