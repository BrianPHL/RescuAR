using System;
using System.Collections.Generic;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Data;

/// <summary>
/// Builds a pedestrian graph from walkable LineStrings and explicit crossing
/// evidence. Lines connect at shared source coordinates; a visual crossing
/// without a shared source vertex does not create a routing connection.
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
        IReadOnlyList<GeoJsonRoadFeature> features) =>
        Build(features, Array.Empty<GeoJsonPointFeature>());

    public RoadGraph Build(
        IReadOnlyList<GeoJsonRoadFeature> features,
        IReadOnlyList<GeoJsonPointFeature> points)
    {
        ArgumentNullException.ThrowIfNull(
            features);
        ArgumentNullException.ThrowIfNull(points);

        Dictionary<CoordinateKey, RoadNode> byCoordinate =
            new();
        var crossingPolicy = new MajorRoadCrossingPolicy(features, points);
        int rejectedCrossingEdges = 0;
        int retainedJunctionEdges = 0;

        Dictionary<int, RoadNode> nodes =
            new();

        List<RoadEdge> edges =
            new();
        HashSet<SegmentIdentity> seenSourceSegments = new();

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
                if (!string.IsNullOrWhiteSpace(feature.OsmId) &&
                    !seenSourceSegments.Add(SegmentIdentity.From(
                        feature.OsmId, fromCoordinate, toCoordinate)))
                    continue;
                if (crossingPolicy.BlocksSegment(feature,
                        fromCoordinate, toCoordinate))
                {
                    rejectedCrossingEdges++;
                    continue;
                }

                // At the ends of a bridge/tunnel feature the mapped way
                // rejoins the ground network; intermediate crossings remain
                // separated by grade.
                int featureLayer = MajorRoadCrossingPolicy.LayerOf(feature);
                int fromLayer = i == 0 ? 0 : featureLayer;
                int toLayer = i + 1 == feature.Coordinates.Count - 1
                    ? 0 : featureLayer;
                string fromSide = crossingPolicy.EndpointSide(
                    fromCoordinate, toCoordinate, fromLayer);
                string toSide = crossingPolicy.EndpointSide(
                    toCoordinate, fromCoordinate, toLayer);
                if (fromSide.Length > 0 || toSide.Length > 0)
                    retainedJunctionEdges++;
                RoadNode from =
                    GetOrCreateNode(
                        fromCoordinate,
                        fromLayer,
                        fromSide,
                        byCoordinate,
                        nodes,
                        ref nextNodeId);

                RoadNode to =
                    GetOrCreateNode(
                        toCoordinate,
                        toLayer,
                        toSide,
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
            $"Walkable junction segments retained: {retainedJunctionEdges}; " +
            $"interior/overlap major-road crossings excluded: {rejectedCrossingEdges}.");
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
        string side,
        Dictionary<CoordinateKey, RoadNode> byCoordinate,
        Dictionary<int, RoadNode> nodes,
        ref int nextNodeId)
    {
        CoordinateKey key =
            CoordinateKey.From(
                coordinate, layer, side);

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
        int Layer,
        string Side)
    {
        public static CoordinateKey From(
            GeoCoordinate coordinate,
            int layer,
            string side)
        {
            return new CoordinateKey(
                Math.Round(
                    coordinate.Latitude,
                    CoordinatePrecision),
                Math.Round(
                    coordinate.Longitude,
                    CoordinatePrecision),
                layer,
                side);
        }
    }

    private readonly record struct SegmentIdentity(
        string OsmId, double ALat, double ALon, double BLat, double BLon)
    {
        public static SegmentIdentity From(string osmId,
            GeoCoordinate a, GeoCoordinate b)
        {
            double aLat = Math.Round(a.Latitude, CoordinatePrecision);
            double aLon = Math.Round(a.Longitude, CoordinatePrecision);
            double bLat = Math.Round(b.Latitude, CoordinatePrecision);
            double bLon = Math.Round(b.Longitude, CoordinatePrecision);
            if (aLat > bLat || (aLat == bLat && aLon > bLon))
                (aLat, aLon, bLat, bLon) = (bLat, bLon, aLat, aLon);
            return new SegmentIdentity(osmId, aLat, aLon, bLat, bLon);
        }
    }
}
