using RescuAR.Navigation.Models;
using RescuAR.Navigation.Timing;

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
    DateTimeOffset Timestamp)
{
    public string Provider { get; init; } = "unavailable";
    public double? CourseAccuracyDegrees { get; init; }
    public long? AcquisitionElapsedRealtimeNanoseconds { get; init; }
    public long? AcquisitionMonotonicMilliseconds { get; init; }

    // Timestamp remains exactly the provider's UTC value. Live navigation uses
    // acquisition time in its stable clock; persisted/legacy fixes retain UTC.
    public DateTimeOffset ObservationTime => AcquisitionMonotonicMilliseconds is { } acquired
        ? NavigationClock.AtMonotonicMilliseconds(acquired) : Timestamp;

    // A reported inaccurate course must not select a different road branch.
    public double? QualifiedCourseDegrees => CourseAccuracyDegrees is { } error &&
        (!double.IsFinite(error) || error < 0 || error > 30) ? null : CourseDegrees;
}
