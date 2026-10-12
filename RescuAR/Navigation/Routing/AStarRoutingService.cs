using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RescuAR.Diagnostics;
using RescuAR.Navigation.Data;
using RescuAR.Navigation.Hazards;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Routing;

/// <summary>
/// Offline A* (A-Star) routing service implementation.
/// Calculates evacuation paths using shared access rules and weighted road
/// preferences. Route distances remain physical meters for AR guidance.
/// </summary>
public sealed class AStarRoutingService : IHazardAwareRoutingService
{
    private const string LogTag =
        "RescuAR-AStar";

    private const string HazardAwareAlgorithmName =
        "AStar (Hazard-Aware)";

    // Routing endpoints share these limits with MLD. AR placement remains stricter.
    private static double MaximumOriginSnapMeters => EvacuationRoutingPolicy.MaximumOriginSnapMeters;
    private static double MaximumDestinationSnapMeters => EvacuationRoutingPolicy.MaximumDestinationSnapMeters;
    private const double DestinationCandidateBandMeters = 12.0;
    private const int MaximumDestinationCandidates = 12;
    private const int MaximumOriginCandidates = 12;
    private const double OriginCandidateBandMeters = 12.0;

    private readonly RoadGraph graph;
    private readonly Dictionary<int, int> componentByNode;
    private readonly Dictionary<int, int> componentSizes;
    private const double IndexCellDegrees = 0.002;
    private readonly Dictionary<(int X, int Y), List<RoadEdge>> edgeIndex = new();
    private readonly List<RoadEdge> longEdges = new();

    public string AlgorithmName => "AStar";

    public AStarRoutingService(RoadGraph graph)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
        componentByNode = BuildComponents(graph);
        componentSizes = componentByNode.Values.GroupBy(id => id)
            .ToDictionary(group => group.Key, group => group.Count());
        BuildEdgeIndex();
    }

    public Task<RouteResult?> FindRouteAsync(
        GeoCoordinate origin,
        GeoCoordinate destination,
        CancellationToken cancellationToken = default)
    {
        if (!origin.IsValid || !destination.IsValid)
        {
            return Task.FromResult<RouteResult?>(null);
        }

        return Task.Run(
            () => ComputeRoute(
                origin,
                destination,
                hazards: null,
                AlgorithmName,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Computes an offline route that treats every supplied RouteHazard as an
    /// exclusion zone. This reuses the existing A* implementation and graph;
    /// only unsafe edges are filtered from expansion.
    /// </summary>
    public Task<RouteResult?> FindRouteAvoidingHazardsAsync(
        GeoCoordinate origin,
        GeoCoordinate destination,
        IReadOnlyList<RouteHazard> hazards,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            hazards);

        if (!origin.IsValid ||
            !destination.IsValid)
        {
            return Task.FromResult<RouteResult?>(null);
        }

        RouteHazard[] validHazards =
            hazards
                .Where(
                    hazard =>
                        hazard is not null &&
                        hazard.Coordinate.IsValid &&
                        double.IsFinite(hazard.RadiusMeters) &&
                        hazard.RadiusMeters > 0.0)
                .ToArray();

        if (validHazards.Length == 0)
        {
            return FindRouteAsync(
                origin,
                destination,
                cancellationToken);
        }

        return Task.Run(
            () => ComputeRoute(
                origin,
                destination,
                validHazards,
                HazardAwareAlgorithmName,
                cancellationToken),
            cancellationToken);
    }

    private RouteResult? ComputeRoute(
        GeoCoordinate origin,
        GeoCoordinate destination,
        IReadOnlyList<RouteHazard>? hazards,
        string algorithmName,
        CancellationToken cancellationToken,
        double? maximumOriginSnapMeters = null)
    {
        if (graph.Nodes.Count == 0)
        {
            return null;
        }

        bool hazardAware =
            hazards is { Count: > 0 };

        if (hazardAware &&
            IsDestinationInsideHazard(
                destination,
                hazards!))
        {
            AndroidLog.Warn(
                LogTag,
                "Hazard-aware A* rejected the destination because it lies " +
                "inside an active hazard exclusion zone.");

            return null;
        }

        // Match both endpoints to segments, not just stored vertices. Temporary
        // endpoints remain on mapped geometry and respect pedestrian direction.
        EdgeProjection[] destinations = FindEdgeProjections(destination, null,
            MaximumDestinationSnapMeters, MaximumDestinationCandidates,
            DestinationCandidateBandMeters, cancellationToken)
            .Where(p => !hazardAware || !IsDestinationInsideHazard(p.SnappedCoordinate, hazards!))
            .ToArray();
        if (destinations.Length == 0)
        {
            AndroidLog.Warn(LogTag, "A* endpoint unmatched: no eligible destination segment within 50 m.");
            return null;
        }
        HashSet<int> destinationComponents = destinations
            .Select(p => componentByNode[p.Edge!.From.Id]).ToHashSet();
        EdgeProjection[] origins = FindEdgeProjections(origin, destinationComponents,
            maximumOriginSnapMeters ?? MaximumOriginSnapMeters, MaximumOriginCandidates,
            OriginCandidateBandMeters, cancellationToken);
        if (origins.Length == 0)
        {
            AndroidLog.Warn(LogTag, "A* endpoint unmatched: no nearby origin segment on a destination network.");
            return null;
        }

        var targets = new Dictionary<int, List<EndpointTarget>>();
        foreach (EdgeProjection projection in destinations)
        {
            RoadEdge edge = projection.Edge!;
            AddTarget(edge.From, projection, edge.Cost * projection.FractionFromStart);
            RoadEdge? reverse = ReverseOf(edge);
            if (projection.FractionFromStart >= 1.0 - 1e-9 || reverse is not null)
                AddTarget(edge.To, projection, (reverse ?? edge).Cost * (1.0 - projection.FractionFromStart));
        }
        void AddTarget(RoadNode node, EdgeProjection projection, double partialCost)
        {
            if (!PartialSegmentSafe(node.Coordinate, projection.SnappedCoordinate, origin, hazards)) return;
            if (!targets.TryGetValue(node.Id, out var list)) targets[node.Id] = list = new();
            list.Add(new(projection, partialCost + projection.CrossTrackMeters * 3.0));
        }

        // Multiple nearby starts share one search. Weak components prune
        // candidates; only directed traversal establishes actual reachability.
        var openSet = new PriorityQueue<int, double>();
        var gScore = new Dictionary<int, double>();
        var cameFrom = new Dictionary<int, (int ParentId, RoadEdge UsedEdge)>();
        var startByNode = new Dictionary<int, EdgeProjection>();
        var closedSet = new HashSet<int>();
        foreach (EdgeProjection projection in origins)
        {
            RoadEdge edge = projection.Edge!;
            AddSeed(edge.To, projection, edge.Cost * (1.0 - projection.FractionFromStart));
            RoadEdge? reverse = ReverseOf(edge);
            if (projection.FractionFromStart <= 1e-9 || reverse is not null)
                AddSeed(edge.From, projection, (reverse ?? edge).Cost * projection.FractionFromStart);
        }
        void AddSeed(RoadNode node, EdgeProjection projection, double partialCost)
        {
            if (!PartialSegmentSafe(projection.SnappedCoordinate, node.Coordinate, origin, hazards)) return;
            double cost = partialCost + projection.CrossTrackMeters * 3.0;
            if (gScore.TryGetValue(node.Id, out double previous) && previous <= cost) return;
            gScore[node.Id] = cost;
            startByNode[node.Id] = projection;
            openSet.Enqueue(node.Id, cost + Heuristic(node));
        }
        double Heuristic(RoadNode node) => Math.Max(0.0,
            node.Coordinate.DistanceTo(destination) - MaximumDestinationSnapMeters);

        int blockedEdgeCount = 0;
        RoadNode? bestTarget = null;
        EdgeProjection bestDestination = default;
        double bestTargetScore = double.PositiveInfinity;
        RouteResult? bestDirect = null;
        foreach (EdgeProjection start in origins)
        foreach (EdgeProjection end in destinations)
        {
            if (!SameSegment(start.Edge!, end.Edge!)) continue;
            double endFraction = start.Edge!.From.Id == end.Edge!.From.Id
                ? end.FractionFromStart : 1.0 - end.FractionFromStart;
            double fraction = endFraction - start.FractionFromStart;
            RoadEdge? travel = fraction >= 0 ? start.Edge : ReverseOf(start.Edge);
            if (travel is null || !PartialSegmentSafe(start.SnappedCoordinate, end.SnappedCoordinate, origin, hazards)) continue;
            double score = travel.Cost * Math.Abs(fraction) +
                3.0 * (start.CrossTrackMeters + end.CrossTrackMeters);
            if (score >= bestTargetScore) continue;
            bestTargetScore = score;
            bestDirect = CreateRoute(new[] { start.SnappedCoordinate, end.SnappedCoordinate }, algorithmName);
        }
        while (openSet.TryDequeue(out int currentId, out double lowerBound))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lowerBound >= bestTargetScore) break;
            if (!closedSet.Add(currentId) ||
                !graph.Nodes.TryGetValue(currentId, out RoadNode? currentNode))
                continue;
            double currentG = gScore[currentId];
            if (targets.TryGetValue(currentId, out var endpoints))
            {
                foreach (EndpointTarget endpoint in endpoints)
                {
                    double score = currentG + endpoint.Cost;
                    if (score >= bestTargetScore) continue;
                    bestTarget = currentNode;
                    bestDestination = endpoint.Projection;
                    bestTargetScore = score;
                    bestDirect = null;
                }
            }
            foreach (RoadEdge edge in currentNode.Edges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (hazardAware && IsEdgeBlockedByHazards(edge, origin, hazards!))
                {
                    blockedEdgeCount++;
                    continue;
                }
                RoadNode neighbor = edge.To;
                if (closedSet.Contains(neighbor.Id)) continue;
                double tentative = currentG + edge.Cost;
                if (!gScore.TryGetValue(neighbor.Id, out double old) ||
                    tentative < old)
                {
                    cameFrom[neighbor.Id] = (currentId, edge);
                    gScore[neighbor.Id] = tentative;
                    openSet.Enqueue(neighbor.Id, tentative + Heuristic(neighbor));
                }
            }
        }

        RouteResult? result = bestDirect ?? (bestTarget is null ? null :
            ReconstructRoute(cameFrom, startByNode, bestTarget, bestDestination.SnappedCoordinate, algorithmName));
        if (result is not null)
        {
            if (hazardAware && !RouteHazardGeometry.RouteAvoidsHazardsFromOrigin(result, origin, hazards!))
                return null;
            AndroidLog.Debug(LogTag,
                $"Selected directed segment route: origins={origins.Length}, destinations={destinations.Length}, " +
                $"distance={result.TotalDistanceMeters:F1} m, weightedCost={bestTargetScore:F1}.");
            return result;
        }

        if (hazardAware)
        {
            AndroidLog.Warn(
                LogTag,
                "Hazard-aware A* could not find a safe replacement route: " +
                $"hazards={hazards!.Count}, blockedEdgeChecks={blockedEdgeCount}.");
        }

        AndroidLog.Warn(
            LogTag,
            "No connected pedestrian graph path: " +
            $"visitedNodes={closedSet.Count}, " +
            $"startSeeds={startByNode.Count}, " +
            $"targetCandidates={targets.Count}.");

        return null;
    }

    private static bool IsDestinationInsideHazard(
        GeoCoordinate destination,
        IReadOnlyList<RouteHazard> hazards)
    {
        for (int i = 0;
             i < hazards.Count;
             i++)
        {
            RouteHazard hazard =
                hazards[i];

            if (destination.DistanceTo(hazard.Coordinate) <=
                hazard.RadiusMeters)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Blocks edges intersecting an active hazard. If the user is already
    /// inside a newly reported exclusion zone, outward-moving edges near the
    /// current position are temporarily allowed so A* can lead the user out
    /// instead of trapping the start node.
    /// </summary>
    private static bool IsEdgeBlockedByHazards(RoadEdge edge, GeoCoordinate origin,
        IReadOnlyList<RouteHazard> hazards) =>
        !RouteHazardGeometry.IsSegmentSafeFromOrigin(edge.From.Coordinate,
            edge.To.Coordinate, origin, hazards);

    private static bool PartialSegmentSafe(GeoCoordinate from, GeoCoordinate to,
        GeoCoordinate origin, IReadOnlyList<RouteHazard>? hazards) =>
        from.DistanceTo(to) <= 0.05 || hazards is null ||
        RouteHazardGeometry.IsSegmentSafeFromOrigin(from, to, origin, hazards);

    private static RoadEdge? ReverseOf(RoadEdge edge) =>
        edge.To.Edges.Where(reverse => reverse.To.Id == edge.From.Id).MinBy(reverse => reverse.Cost);

    private static bool SameSegment(RoadEdge a, RoadEdge b) =>
        (a.From.Id == b.From.Id && a.To.Id == b.To.Id) ||
        (a.From.Id == b.To.Id && a.To.Id == b.From.Id);

    private readonly record struct EndpointTarget(EdgeProjection Projection, double Cost);

    private static Dictionary<int, int> BuildComponents(RoadGraph graph)
    {
        // Weak connectivity only selects candidates. Directed A* still decides
        // reachability; reverse-only ways must not be split by iteration order.
        var neighbors = graph.Nodes.Keys.ToDictionary(id => id, _ => new List<int>());
        foreach (RoadEdge edge in graph.Edges)
        {
            neighbors[edge.From.Id].Add(edge.To.Id);
            neighbors[edge.To.Id].Add(edge.From.Id);
        }
        var components = new Dictionary<int, int>(graph.Nodes.Count);
        var pending = new Queue<int>();
        int next = 0;
        foreach (int id in graph.Nodes.Keys)
        {
            if (components.ContainsKey(id)) continue;
            components.Add(id, ++next);
            pending.Enqueue(id);
            while (pending.TryDequeue(out int current))
                foreach (int neighbor in neighbors[current])
                    if (components.TryAdd(neighbor, next)) pending.Enqueue(neighbor);
        }
        return components;
    }

    /// <summary>
    /// Finds a nearby road connected to the facility for an arrow-only approach
    /// cue. Does not relax origin snapping or add any walking-route geometry.
    /// </summary>
    public GeoCoordinate? FindApproachCoordinate(GeoCoordinate origin, GeoCoordinate destination)
    {
        var target = FindRoadApproachTarget(origin, destination);
        return target is { ConnectsToDestination: true } ? target.Value.Coordinate : null;
    }

    public readonly record struct RoadApproachTarget(GeoCoordinate Coordinate, bool ConnectsToDestination)
    {
        public int RoadComponentNodeCount { get; init; }
        public int DestinationComponentCount { get; init; }
    }

    /// <summary>Road access can be shown before facility connectivity is known.</summary>
    public RoadApproachTarget? FindRoadApproachTarget(GeoCoordinate origin, GeoCoordinate destination,
        CancellationToken cancellationToken = default)
    {
        if (!origin.IsValid || !destination.IsValid) return null;
        var destinations = FindEdgeProjections(destination, null, MaximumDestinationSnapMeters,
            MaximumDestinationCandidates, DestinationCandidateBandMeters, cancellationToken)
            .Select(p => componentByNode[p.Edge!.From.Id]).ToHashSet();
        RouteResult? reachable = ComputeRoute(origin, destination, null, AlgorithmName, cancellationToken, 50.0);
        GeoCoordinate? access = reachable?.Points.FirstOrDefault()?.Coordinate;
        var projection = FindEdgeProjections(access ?? origin, null,
            access.HasValue ? 0.1 : 50.0, 1, 0, cancellationToken).FirstOrDefault();
        if (!projection.IsAvailable || projection.Edge is null) return null;
        return new(access ?? projection.SnappedCoordinate, reachable is not null)
        {
            RoadComponentNodeCount = componentSizes[componentByNode[projection.Edge.From.Id]],
            DestinationComponentCount = destinations.Count
        };
    }

    private EdgeProjection[] FindEdgeProjections(GeoCoordinate point,
        IReadOnlySet<int>? destinationComponents, double maximumSnapMeters,
        int maximumCandidates, double candidateBandMeters, CancellationToken token)
    {
        var candidates = new List<EdgeProjection>();
        foreach (RoadEdge edge in NearbyEdges(point, maximumSnapMeters))
        {
            token.ThrowIfCancellationRequested();
            if (destinationComponents is not null &&
                !destinationComponents.Contains(componentByNode[edge.From.Id]))
                continue;
            EdgeProjection candidate =
                ProjectOntoEdge(
                    point,
                    edge);

            if (!candidate.IsAvailable ||
                candidate.CrossTrackMeters > maximumSnapMeters ||
                graph.AccessCrossesMajorRoad(point, candidate.SnappedCoordinate))
            {
                continue;
            }

            candidates.Add(candidate);
        }
        if (candidates.Count == 0) return Array.Empty<EdgeProjection>();
        double band = candidates.Min(p => p.CrossTrackMeters) + candidateBandMeters;
        return candidates.Where(p => p.CrossTrackMeters <= band)
            .OrderBy(p => p.CrossTrackMeters).ThenBy(p => p.Edge!.Id)
            .Take(maximumCandidates).ToArray();
    }

    private static int Cell(double degrees) => (int)Math.Floor(degrees / IndexCellDegrees);

    private void BuildEdgeIndex()
    {
        var seen = new HashSet<(int, int)>();
        foreach (RoadEdge edge in graph.Edges)
        {
            if (!seen.Add((Math.Min(edge.From.Id, edge.To.Id), Math.Max(edge.From.Id, edge.To.Id)))) continue;
            int left = Cell(Math.Min(edge.From.Coordinate.Longitude, edge.To.Coordinate.Longitude));
            int right = Cell(Math.Max(edge.From.Coordinate.Longitude, edge.To.Coordinate.Longitude));
            int bottom = Cell(Math.Min(edge.From.Coordinate.Latitude, edge.To.Coordinate.Latitude));
            int top = Cell(Math.Max(edge.From.Coordinate.Latitude, edge.To.Coordinate.Latitude));
            if ((long)(right - left + 1) * (top - bottom + 1) > 2048)
            {
                longEdges.Add(edge);
                continue;
            }
            for (int x = left; x <= right; x++)
            for (int y = bottom; y <= top; y++)
            {
                if (!edgeIndex.TryGetValue((x, y), out var list)) edgeIndex[(x, y)] = list = new();
                list.Add(edge);
            }
        }
    }

    private IEnumerable<RoadEdge> NearbyEdges(GeoCoordinate point, double radiusMeters)
    {
        double latitudeDelta = radiusMeters / 111000.0;
        double cosine = Math.Abs(Math.Cos(point.Latitude * Math.PI / 180.0));
        if (cosine < 0.01) return graph.Edges;
        double longitudeDelta = latitudeDelta / cosine;
        var candidates = new HashSet<RoadEdge>(longEdges);
        for (int x = Cell(point.Longitude - longitudeDelta); x <= Cell(point.Longitude + longitudeDelta); x++)
        for (int y = Cell(point.Latitude - latitudeDelta); y <= Cell(point.Latitude + latitudeDelta); y++)
            if (edgeIndex.TryGetValue((x, y), out var list)) candidates.UnionWith(list);
        return candidates;
    }

    private static EdgeProjection ProjectOntoEdge(
        GeoCoordinate point,
        RoadEdge edge)
    {
        const double earthRadiusMeters =
            6371008.8;

        double referenceLatitudeRadians =
            point.Latitude *
            Math.PI /
            180.0;

        double cosReferenceLatitude =
            Math.Cos(
                referenceLatitudeRadians);

        double fromNorth =
            (edge.From.Coordinate.Latitude -
             point.Latitude) *
            Math.PI /
            180.0 *
            earthRadiusMeters;

        double fromEast =
            (edge.From.Coordinate.Longitude -
             point.Longitude) *
            Math.PI /
            180.0 *
            earthRadiusMeters *
            cosReferenceLatitude;

        double toNorth =
            (edge.To.Coordinate.Latitude -
             point.Latitude) *
            Math.PI /
            180.0 *
            earthRadiusMeters;

        double toEast =
            (edge.To.Coordinate.Longitude -
             point.Longitude) *
            Math.PI /
            180.0 *
            earthRadiusMeters *
            cosReferenceLatitude;

        double deltaEast =
            toEast -
            fromEast;

        double deltaNorth =
            toNorth -
            fromNorth;

        double lengthSquared =
            deltaEast *
                deltaEast +
            deltaNorth *
                deltaNorth;

        if (!double.IsFinite(
                lengthSquared) ||
            lengthSquared <=
                0.0001)
        {
            return EdgeProjection.Unavailable;
        }

        double fraction =
            -(
                fromEast *
                    deltaEast +
                fromNorth *
                    deltaNorth) /
            lengthSquared;

        fraction =
            Math.Clamp(
                fraction,
                0.0,
                1.0);

        double snappedEast =
            fromEast +
            deltaEast *
                fraction;

        double snappedNorth =
            fromNorth +
            deltaNorth *
                fraction;

        double crossTrack =
            Math.Sqrt(
                snappedEast *
                    snappedEast +
                snappedNorth *
                    snappedNorth);

        GeoCoordinate snappedCoordinate =
            new(
                edge.From.Coordinate.Latitude +
                    (edge.To.Coordinate.Latitude -
                     edge.From.Coordinate.Latitude) *
                    fraction,
                edge.From.Coordinate.Longitude +
                    (edge.To.Coordinate.Longitude -
                     edge.From.Coordinate.Longitude) *
                    fraction);

        return new EdgeProjection(
            true,
            edge,
            snappedCoordinate,
            fraction,
            crossTrack);
    }

    private RouteResult ReconstructRoute(
        Dictionary<int, (int ParentId, RoadEdge UsedEdge)> cameFrom,
        Dictionary<int, EdgeProjection> startByNode,
        RoadNode targetNode,
        GeoCoordinate snappedDestination,
        string algorithmName)
    {
        var reversedNodes = new List<RoadNode>();

        int currentId = targetNode.Id;
        reversedNodes.Add(targetNode);

        while (cameFrom.TryGetValue(currentId, out var tuple))
        {
            currentId = tuple.ParentId;
            if (graph.Nodes.TryGetValue(currentId, out var parentNode))
            {
                reversedNodes.Add(parentNode);
            }
        }

        reversedNodes.Reverse();
        return CreateRoute(new[] { startByNode[currentId].SnappedCoordinate }
            .Concat(reversedNodes.Select(node => node.Coordinate))
            .Append(snappedDestination), algorithmName);
    }

    private static RouteResult CreateRoute(IEnumerable<GeoCoordinate> coordinates, string algorithmName)
    {
        var points = new List<RoutePoint>();
        double distance = 0.0;
        foreach (GeoCoordinate coordinate in coordinates)
        {
            if (points.Count > 0)
            {
                double segment = points[^1].Coordinate.DistanceTo(coordinate);
                if (segment <= 0.05) continue;
                distance += segment;
            }
            points.Add(new RoutePoint(coordinate, distance));
        }
        return new RouteResult(points, distance, algorithmName);
    }

    private readonly record struct EdgeProjection(
        bool IsAvailable,
        RoadEdge? Edge,
        GeoCoordinate SnappedCoordinate,
        double FractionFromStart,
        double CrossTrackMeters)
    {
        public static EdgeProjection Unavailable =>
            new(
                false,
                null,
                default,
                0.0,
                double.PositiveInfinity);
    }
}
