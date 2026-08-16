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

    public IReadOnlyDictionary<int, RoadNode> Nodes =>
        nodes;

    public IReadOnlyList<RoadEdge> Edges =>
        edges;

    public RoadGraph(
        Dictionary<int, RoadNode> nodes,
        List<RoadEdge> edges)
    {
        this.nodes =
            nodes ??
            throw new ArgumentNullException(
                nameof(nodes));

        this.edges =
            edges ??
            throw new ArgumentNullException(
                nameof(edges));
    }
}
