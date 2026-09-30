using System.Collections.Generic;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Data;

/// <summary>
/// Parsed OSM point feature. Explicit marked crossing points can authorize
/// connections between otherwise separated sides of a major-road junction.
/// </summary>
public sealed class GeoJsonPointFeature
{
    public int SourceId { get; init; }

    public string? OsmId { get; init; }

    public string? Name { get; init; }

    public string? Highway { get; init; }

    public string? Barrier { get; init; }

    public GeoCoordinate Coordinate { get; init; }

    public IReadOnlyDictionary<string, string> Tags { get; init; } =
        new Dictionary<string, string>();
}
