using System.Collections.Generic;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Data;

/// <summary>
/// Parsed road feature before graph construction.
/// </summary>
public sealed class GeoJsonRoadFeature
{
    public int SourceId { get; init; }

    public string? OsmId { get; init; }

    public string? Name { get; init; }

    public string? Highway { get; init; }

    public IReadOnlyDictionary<string, string> Tags { get; init; } =
        new Dictionary<string, string>();

    public IReadOnlyList<GeoCoordinate> Coordinates { get; init; } =
        [];
}
