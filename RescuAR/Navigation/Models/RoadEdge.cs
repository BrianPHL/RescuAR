using System.Collections.Generic;
using RescuAR.Navigation.Data;

namespace RescuAR.Navigation.Models;

/// <summary>
/// Directed pedestrian graph edge.
///
/// Cost includes shared evacuation preferences; LengthMeters remains physical
/// distance for route progress and AR placement.
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
            lengthMeters * EvacuationRoutingPolicy.CostFactor(highwayType, tags);

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
