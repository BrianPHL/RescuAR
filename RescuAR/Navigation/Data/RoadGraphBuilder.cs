using System;
using System.Collections.Generic;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Data;

/// <summary>
/// Builds an initial pedestrian graph from walkable LineStrings.
///
/// IMPORTANT:
/// This first foundation nodes the graph at coordinates already present in the
/// source LineStrings. It does not yet calculate arbitrary geometric
/// intersections between two lines that cross without sharing a source
/// vertex. That topology validation belongs before routing-algorithm
/// integration, not before establishing the common data model.
/// </summary>
public sealed class RoadGraphBuilder
{
    /*
     * Seven decimal degrees is roughly centimeter-scale around Marikina.
     * This normalizes harmless floating representation differences while
     * preserving the OSM vertex topology.
     */
    private const int CoordinatePrecision =
        7;

    private readonly PedestrianRoadFilter filter;

    public RoadGraphBuilder(
        PedestrianRoadFilter? filter = null)
    {
        this.filter =
            filter ??
            new PedestrianRoadFilter();
    }

    public RoadGraph Build(
        IReadOnlyList<GeoJsonRoadFeature> features)
    {
        ArgumentNullException.ThrowIfNull(
            features);

        Dictionary<CoordinateKey, RoadNode> byCoordinate =
            new();
        var crossingPolicy = new MajorRoadCrossingPolicy(features);
        int rejectedCrossingEdges = 0;

        Dictionary<int, RoadNode> nodes =
            new();

        List<RoadEdge> edges =
            new();

        int nextNodeId =
            1;

        int nextEdgeId =
            1;

        foreach (GeoJsonRoadFeature feature in
                 features)
        {
            if (!filter.IsWalkable(
                    feature))
            {
                continue;
            }

            for (int i = 0;
                 i < feature.Coordinates.Count - 1;
                 i++)
            {
                GeoCoordinate fromCoordinate =
                    feature.Coordinates[i];

                GeoCoordinate toCoordinate =
                    feature.Coordinates[i + 1];

                if (fromCoordinate == toCoordinate)
                    continue;
                if (crossingPolicy.IsUnverifiedCrossing(feature,
                        fromCoordinate, toCoordinate))
                {
                    rejectedCrossingEdges++;
                    continue;
                }

                // At the ends of a bridge/tunnel feature the mapped way
                // rejoins the ground network; intermediate crossings remain
                // separated by grade.
                int featureLayer = MajorRoadCrossingPolicy.LayerOf(feature);
                RoadNode from =
                    GetOrCreateNode(
                        fromCoordinate,
                        i == 0 ? 0 : featureLayer,
                        byCoordinate,
                        nodes,
                        ref nextNodeId);

                RoadNode to =
                    GetOrCreateNode(
                        toCoordinate,
                        i + 1 == feature.Coordinates.Count - 1
                            ? 0 : featureLayer,
                        byCoordinate,
                        nodes,
                        ref nextNodeId);

                double length =
                    fromCoordinate.DistanceTo(
                        toCoordinate);

                if (length <=
                    0.01)
                {
                    continue;
                }

                /*
                 * Pedestrian routing is bidirectional by default even when
                 * the road's OSM "oneway" tag applies to motor vehicles.
                 * Explicit pedestrian direction restrictions can be added
                 * later if present in the source data.
                 */
                RoadEdge forward =
                    CreateEdge(
                        nextEdgeId++,
                        from,
                        to,
                        length,
                        feature);

                RoadEdge reverse =
                    CreateEdge(
                        nextEdgeId++,
                        to,
                        from,
                        length,
                        feature);

                edges.Add(
                    forward);

                edges.Add(
                    reverse);

                from.AddEdge(
                    forward);

                to.AddEdge(
                    reverse);
            }
        }

        RescuAR.Diagnostics.AndroidLog.Warn("RescuAR-RoadGraph",
            $"Unverified major-road crossing segments excluded: {rejectedCrossingEdges}.");
        return new RoadGraph(
            nodes,
            edges,
            crossingPolicy.CrossesMajorRoadBetween);
    }

    private static RoadEdge CreateEdge(
        int id,
        RoadNode from,
        RoadNode to,
        double length,
        GeoJsonRoadFeature feature)
    {
        return new RoadEdge(
            id,
            from,
            to,
            length,
            feature.OsmId,
            feature.Name,
            feature.Highway ??
                string.Empty,
            feature.Tags);
    }

    private static RoadNode GetOrCreateNode(
        GeoCoordinate coordinate,
        int layer,
        Dictionary<CoordinateKey, RoadNode> byCoordinate,
        Dictionary<int, RoadNode> nodes,
        ref int nextNodeId)
    {
        CoordinateKey key =
            CoordinateKey.From(
                coordinate, layer);

        if (byCoordinate.TryGetValue(
                key,
                out RoadNode? existing))
        {
            return existing;
        }

        RoadNode node =
            new(
                nextNodeId++,
                coordinate);

        byCoordinate.Add(
            key,
            node);

        nodes.Add(
            node.Id,
            node);

        return node;
    }

    private readonly record struct CoordinateKey(
        double Latitude,
        double Longitude,
        int Layer)
    {
        public static CoordinateKey From(
            GeoCoordinate coordinate,
            int layer)
        {
            return new CoordinateKey(
                Math.Round(
                    coordinate.Latitude,
                    CoordinatePrecision),
                Math.Round(
                    coordinate.Longitude,
                    CoordinatePrecision),
                layer);
        }
    }
}
