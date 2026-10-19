using System.Collections.Generic;

namespace RescuAR.Navigation.Models;

/// <summary>Observed provider inputs and provenance; never changes route geometry.</summary>
public sealed record RouteProvenance(
    string Provider,
    string DatasetId = "unreported",
    string DataVersion = "unreported",
    IReadOnlyList<GeoCoordinate>? RequestedCoordinates = null,
    IReadOnlyList<GeoCoordinate>? SnappedWaypoints = null,
    IReadOnlyList<string>? SourceWayIds = null)
{
    public static RouteProvenance Unreported { get; } = new("unreported");
}
