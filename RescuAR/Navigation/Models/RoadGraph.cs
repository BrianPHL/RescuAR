using System;
using System.Collections.Generic;

namespace RescuAR.Navigation.Models;

/// <summary>
/// In-memory pedestrian routing graph.
/// </summary>
public sealed class RoadGraph
{
    private readonly Dictionary<int, RoadNode> nodes;

    private readonly List<RoadEdge> edges;

    private readonly Func<GeoCoordinate, GeoCoordinate, bool>? majorRoadAccessBarrier;

    public IReadOnlyDictionary<int, RoadNode> Nodes =>
        nodes;

    public IReadOnlyList<RoadEdge> Edges =>
        edges;

    public RoadGraph(
        Dictionary<int, RoadNode> nodes,
        List<RoadEdge> edges,
        Func<GeoCoordinate, GeoCoordinate, bool>? majorRoadAccessBarrier = null)
    {
        this.nodes =
            nodes ??
            throw new ArgumentNullException(
                nameof(nodes));

        this.edges =
            edges ??
            throw new ArgumentNullException(
                nameof(edges));

        this.majorRoadAccessBarrier = majorRoadAccessBarrier;
    }

    /// <summary>
    /// The path to a candidate node uses graph edges. A map pin is not a
    /// graph vertex, so its remaining access must not cross a major road.
    /// </summary>
    public bool AccessCrossesMajorRoad(GeoCoordinate from, GeoCoordinate pin) =>
        majorRoadAccessBarrier?.Invoke(from, pin) ?? false;
}
