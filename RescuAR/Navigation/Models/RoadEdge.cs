using System.Collections.Generic;

namespace RescuAR.Navigation.Models;

/// <summary>
/// Directed pedestrian graph edge.
///
/// For the initial navigation foundation, cost is physical walking distance.
/// Hazard/accessibility penalties can be layered on later without changing
/// the route representation.
/// </summary>
public sealed class RoadEdge
{
    public int Id { get; }

    public RoadNode From { get; }

    public RoadNode To { get; }

    public double LengthMeters { get; }

    public double Cost { get; }

    public string? OsmId { get; }

    public string? Name { get; }

    public string HighwayType { get; }

    public IReadOnlyDictionary<string, string> Tags { get; }

    public RoadEdge(
        int id,
        RoadNode from,
        RoadNode to,
        double lengthMeters,
        string? osmId,
        string? name,
        string highwayType,
        IReadOnlyDictionary<string, string> tags)
    {
        Id =
            id;

        From =
            from;

        To =
            to;

        LengthMeters =
            lengthMeters;

        Cost =
            lengthMeters;

        OsmId =
            osmId;

        Name =
            name;

        HighwayType =
            highwayType;

        Tags =
            tags;
    }
}
