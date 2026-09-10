using System;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Hazards;

/// <summary>
/// Geographic hazard that must be avoided by pedestrian routing.
///
/// RadiusMeters represents the horizontal exclusion radius around the
/// reported hazard coordinate. The source remains provider-neutral so future
/// LGU/advisory feeds can publish the same model without changing routing.
/// </summary>
public sealed record RouteHazard
{
    public string Id { get; }

    public GeoCoordinate Coordinate { get; }

    public double RadiusMeters { get; }

    public string Category { get; }

    public string Severity { get; }

    public string Title { get; }

    public string Source { get; }

    public DateTimeOffset ObservedAtUtc { get; }

    public RouteHazard(
        string id,
        GeoCoordinate coordinate,
        double radiusMeters,
        string category,
        string severity,
        string title,
        string source,
        DateTimeOffset observedAtUtc)
    {
        if (!coordinate.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coordinate));
        }

        if (!double.IsFinite(radiusMeters) ||
            radiusMeters <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radiusMeters));
        }

        Id =
            string.IsNullOrWhiteSpace(id)
                ? Guid.NewGuid().ToString("N")
                : id.Trim();

        Coordinate =
            coordinate;

        RadiusMeters =
            radiusMeters;

        Category =
            category?.Trim() ?? string.Empty;

        Severity =
            severity?.Trim() ?? string.Empty;

        Title =
            title?.Trim() ?? string.Empty;

        Source =
            source?.Trim() ?? string.Empty;

        ObservedAtUtc =
            observedAtUtc;
    }
}
