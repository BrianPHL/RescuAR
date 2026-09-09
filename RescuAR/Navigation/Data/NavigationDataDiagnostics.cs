using System;
using System.Collections.Generic;
using System.Linq;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Data;

/// <summary>
/// Small diagnostics helper for validating the first graph build without
/// coupling it to UI or Android logging.
/// </summary>
public static class NavigationDataDiagnostics
{
    public static NavigationDataSummary Summarize(
        IReadOnlyList<GeoJsonRoadFeature> roads,
        RoadGraph graph)
    {
        ArgumentNullException.ThrowIfNull(
            roads);

        ArgumentNullException.ThrowIfNull(
            graph);

        int sourceLineStrings =
            roads.Count;

        int sourceCoordinateCount =
            roads.Sum(
                road =>
                    road.Coordinates.Count);

        int isolatedNodes =
            graph.Nodes.Values.Count(
                node =>
                    node.Edges.Count ==
                    0);

        return new NavigationDataSummary(
            sourceLineStrings,
            sourceCoordinateCount,
            graph.Nodes.Count,
            graph.Edges.Count,
            isolatedNodes);
    }
}

public sealed record NavigationDataSummary(
    int SourceLineStrings,
    int SourceCoordinateCount,
    int GraphNodeCount,
    int DirectedEdgeCount,
    int IsolatedNodeCount);
