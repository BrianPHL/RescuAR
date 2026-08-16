using System.Collections.Generic;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Data;

/// <summary>
/// Parsed OSM point feature. The routing graph does not depend on these points,
/// but retaining a typed loader gives RescuAR a clean basis for later POI,
/// crossing, landmark, and evacuation-center matching.
/// </summary>
public sealed class GeoJsonPointFeature
{
    public int SourceId { get; init; }

    public string? OsmId { get; init; }

    public string? Name { get; init; }

    public string? Highway { get; init; }

    public GeoCoordinate Coordinate { get; init; }

    public IReadOnlyDictionary<string, string> Tags { get; init; } =
        new Dictionary<string, string>();
}
