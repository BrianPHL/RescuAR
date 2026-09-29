using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RescuAR.Diagnostics;
using RescuAR.Navigation.Hazards;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Routing;

/// <summary>
/// Offline A* (A-Star) routing service implementation.
/// Calculates shortest pedestrian paths using the in-memory RoadGraph.
///
/// Stage 10 adds an explicit hazard-aware route entry point while preserving
/// the original IRoutingService behavior unchanged for ordinary navigation.
/// </summary>
public sealed class AStarRoutingService : IHazardAwareRoutingService
{
    private const string LogTag =
        "RescuAR-AStar";

    private const string HazardAwareAlgorithmName =
        "AStar (Hazard-Aware)";

    private const double OriginHazardEscapeAllowanceMeters =
        12.0;

    private const double EscapeProgressEpsilonMeters =
        0.25;

    // AR refuses to align GPS to route geometry beyond this distance.
    private const double MaximumOriginSnapMeters = 20.0;
    private const double MaximumDestinationSnapMeters = 50.0;
    private const double DestinationCandidateBandMeters = 12.0;
    private const int MaximumDestinationCandidates = 12;

    private readonly RoadGraph graph;
    private readonly Dictionary<int, int> componentByNode;

    public string AlgorithmName => "AStar";

    public AStarRoutingService(RoadGraph graph)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
        componentByNode = BuildComponents(graph);
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
        CancellationToken cancellationToken)
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

        // A nearby dead-end fragment must not win the origin snap when a
        // second, comparably nearby pedestrian edge reaches the facility.
        // Compute viable destination components before selecting that edge.
        var facilityAccess = graph.Nodes.Values
            .Select(node => (Node: node,
                Distance: node.Coordinate.DistanceTo(destination)))
            .Where(item => item.Distance <= MaximumDestinationSnapMeters &&
                !graph.AccessCrossesMajorRoad(item.Node.Coordinate, destination))
            .ToArray();
        if (facilityAccess.Length == 0)
        {
            AndroidLog.Warn(LogTag,
                "No pedestrian graph endpoint within 50 m of the facility without a major-road barrier.");
            return null;
        }

        HashSet<int> destinationComponents = facilityAccess
            .Select(item => componentByNode[item.Node.Id])
            .ToHashSet();

        Dictionary<int, double> startSeedCosts =
            new();

        GeoCoordinate snappedStartCoordinate;

        if (hazardAware)
        {
            /*
             * Preserve the previously validated hazard escape behavior. A
             * virtual partial-edge start needs separate segment-level hazard
             * clipping before it can safely be enabled for exclusion routes.
             */
            RoadNode? startNode =
                FindNearestNode(
                    origin, destinationComponents);

            double originSnapMeters =
                startNode?.Coordinate.DistanceTo(origin) ??
                double.PositiveInfinity;

            if (startNode is null ||
                originSnapMeters > MaximumOriginSnapMeters)
            {
                AndroidLog.Warn(
                    LogTag,
                    $"Origin node snap rejected: distance={originSnapMeters:F1} m, " +
                    $"limit={MaximumOriginSnapMeters:F1} m.");
                return null;
            }

            snappedStartCoordinate =
                startNode.Coordinate;

            if (graph.AccessCrossesMajorRoad(origin, snappedStartCoordinate))
            {
                AndroidLog.Warn(LogTag, "Origin node snap crosses a major road.");
                return null;
            }

            startSeedCosts[startNode.Id] =
                0.0;
        }
        else
        {
            EdgeProjection startProjection =
                FindNearestEdgeProjection(
                    origin, destinationComponents);

            if (!startProjection.IsAvailable ||
                startProjection.Edge is null ||
                startProjection.CrossTrackMeters > MaximumOriginSnapMeters)
            {
                AndroidLog.Warn(
                    LogTag,
                    $"Origin edge snap rejected: distance={startProjection.CrossTrackMeters:F1} m, " +
                    $"limit={MaximumOriginSnapMeters:F1} m.");
                return null;
            }

            RoadEdge startEdge =
                startProjection.Edge;

            snappedStartCoordinate =
                startProjection.SnappedCoordinate;

            if (graph.AccessCrossesMajorRoad(origin, snappedStartCoordinate))
            {
                AndroidLog.Warn(LogTag, "Origin edge snap crosses a major road.");
                return null;
            }

            double costToFrom =
                startEdge.LengthMeters *
                startProjection.FractionFromStart;

            double costToTo =
                startEdge.LengthMeters *
                (1.0 -
                 startProjection.FractionFromStart);

            startSeedCosts[startEdge.From.Id] =
                costToFrom;

            startSeedCosts[startEdge.To.Id] =
                costToTo;

            AndroidLog.Debug(
                LogTag,
                "A* origin snapped to nearest traversable edge: " +
                $"edge={startEdge.Id}, " +
                $"crossTrack={startProjection.CrossTrackMeters:F1} m, " +
                $"fraction={startProjection.FractionFromStart:F3}, " +
                $"seedFrom={costToFrom:F1} m, " +
                $"seedTo={costToTo:F1} m.");
        }

        // The closest node to a map pin may be isolated or across a road.
        // Search only access points on the origin's connected network, and
        // reject an unmapped major-road crossing in the gap to the pin.
        int originComponent = componentByNode[startSeedCosts.Keys.First()];
        var accessibleTargets = facilityAccess
            .Where(item => componentByNode[item.Node.Id] == originComponent)
            .OrderBy(item => item.Distance)
            .ToArray();
        if (accessibleTargets.Length == 0)
        {
            AndroidLog.Warn(LogTag,
                "No connected facility access within 50 m without an unverified major-road crossing.");
            return null;
        }

        double candidateRadius = Math.Min(MaximumDestinationSnapMeters,
            accessibleTargets[0].Distance + DestinationCandidateBandMeters);
        Dictionary<int, double> targets = accessibleTargets
            .Where(item => item.Distance <= candidateRadius)
            .Take(MaximumDestinationCandidates)
            .ToDictionary(item => item.Node.Id, item => item.Distance);

        // One bounded multi-target A* search avoids repeating A* for
        // nearby facility access nodes. Off-graph distance is penalized but
        // never rendered as a walking segment.
        var openSet = new PriorityQueue<int, double>();
        var gScore = new Dictionary<int, double>();
        var cameFrom = new Dictionary<int, (int ParentId, RoadEdge UsedEdge)>();
        var closedSet = new HashSet<int>();
        foreach (KeyValuePair<int, double> seed in startSeedCosts)
        {
            if (!graph.Nodes.ContainsKey(seed.Key)) continue;
            gScore[seed.Key] = seed.Value;
            double remaining = Math.Max(0.0,
                graph.Nodes[seed.Key].Coordinate.DistanceTo(destination) -
                candidateRadius);
            openSet.Enqueue(seed.Key, seed.Value + remaining);
        }
        if (openSet.Count == 0) return null;

        int blockedEdgeCount = 0;
        RoadNode? bestTarget = null;
        double bestTargetScore = double.PositiveInfinity;
        double bestGraphDistance = double.PositiveInfinity;
        while (openSet.TryDequeue(out int currentId, out double lowerBound))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lowerBound >= bestTargetScore) break;
            if (!closedSet.Add(currentId) ||
                !graph.Nodes.TryGetValue(currentId, out RoadNode? currentNode))
                continue;
            double currentG = gScore[currentId];
            // A seed can itself lie within the facility's candidate band.
            // Selecting it creates a one-point route, which the AR publisher
            // rejects, even when another connected access node is available.
            if (currentG > 0.05 &&
                targets.TryGetValue(currentId, out double snap))
            {
                double score = currentG + snap * 3.0;
                if (score < bestTargetScore)
                {
                    bestTarget = currentNode;
                    bestTargetScore = score;
                    bestGraphDistance = currentG;
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
                    double remaining = Math.Max(0.0,
                        neighbor.Coordinate.DistanceTo(destination) -
                        candidateRadius);
                    openSet.Enqueue(neighbor.Id, tentative + remaining);
                }
            }
        }

        double direct = origin.DistanceTo(destination);
        double maximumReasonableDetour = Math.Max(150.0, direct * 3.0);
        if (bestTarget is not null && bestGraphDistance <= maximumReasonableDetour)
        {
            RouteResult result = ReconstructRoute(cameFrom, bestTarget,
                snappedStartCoordinate, startSeedCosts, bestGraphDistance,
                algorithmName);
            AndroidLog.Debug(LogTag,
                $"Selected connected facility access: snap={targets[bestTarget.Id]:F1} m, " +
                $"graph={bestGraphDistance:F1} m, candidates={targets.Count}.");
            return result;
        }
        if (bestTarget is not null)
            AndroidLog.Warn(LogTag,
                $"Implausible destination detour rejected: graph={bestGraphDistance:F1} m, " +
                $"direct={direct:F1} m, limit={maximumReasonableDetour:F1} m.");

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
            $"startSeeds={startSeedCosts.Count}, " +
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
    private static bool IsEdgeBlockedByHazards(
        RoadEdge edge,
        GeoCoordinate origin,
        IReadOnlyList<RouteHazard> hazards)
    {
        for (int i = 0;
             i < hazards.Count;
             i++)
        {
            RouteHazard hazard =
                hazards[i];

            double originDistance =
                origin.DistanceTo(
                    hazard.Coordinate);

            double fromDistance =
                edge.From.Coordinate.DistanceTo(
                    hazard.Coordinate);

            double toDistance =
                edge.To.Coordinate.DistanceTo(
                    hazard.Coordinate);

            bool originInsideHazard =
                originDistance <=
                    hazard.RadiusMeters;

            if (originInsideHazard &&
                fromDistance <=
                    hazard.RadiusMeters +
                    OriginHazardEscapeAllowanceMeters &&
                toDistance >
                    fromDistance +
                    EscapeProgressEpsilonMeters)
            {
                // Permit only movement that clearly increases separation from
                // the hazard while escaping its immediate start-area buffer.
                continue;
            }

            if (RouteHazardGeometry.EdgeIntersectsHazard(
                    edge,
                    hazard))
            {
                return true;
            }
        }

        return false;
    }

    private RoadNode? FindNearestNode(GeoCoordinate point,
        IReadOnlySet<int> destinationComponents)
    {
        RoadNode? nearest = null;
        double minDistance = double.MaxValue;

        foreach (var node in graph.Nodes.Values)
        {
            double dist = node.Coordinate.DistanceTo(point);
            if (dist < minDistance && dist <= MaximumOriginSnapMeters &&
                destinationComponents.Contains(componentByNode[node.Id]) &&
                !graph.AccessCrossesMajorRoad(point, node.Coordinate))
            {
                minDistance = dist;
                nearest = node;
            }
        }

        return nearest;
    }

    private static Dictionary<int, int> BuildComponents(RoadGraph graph)
    {
        var components = new Dictionary<int, int>(graph.Nodes.Count);
        var pending = new Queue<RoadNode>();
        int nextComponent = 0;
        foreach (RoadNode node in graph.Nodes.Values)
        {
            if (components.ContainsKey(node.Id)) continue;
            components[node.Id] = ++nextComponent;
            pending.Enqueue(node);
            while (pending.TryDequeue(out RoadNode? current))
            {
                if (current is null) continue;
                foreach (RoadEdge edge in current.Edges)
                {
                    if (!components.TryAdd(edge.To.Id, nextComponent)) continue;
                    pending.Enqueue(edge.To);
                }
            }
        }
        return components;
    }

    /// <summary>
    /// Finds a nearby road connected to the facility for an arrow-only approach
    /// cue. Does not relax origin snapping or add any walking-route geometry.
    /// </summary>
    public GeoCoordinate? FindApproachCoordinate(GeoCoordinate origin, GeoCoordinate destination)
    {
        if (!origin.IsValid || !destination.IsValid) return null;
        var components = graph.Nodes.Values
            .Where(node => node.Coordinate.DistanceTo(destination) <= MaximumDestinationSnapMeters &&
                !graph.AccessCrossesMajorRoad(node.Coordinate, destination))
            .Select(node => componentByNode[node.Id]).ToHashSet();
        var projection = FindNearestEdgeProjection(origin, components, 50.0);
        return projection.IsAvailable ? projection.SnappedCoordinate : null;
    }

    public readonly record struct RoadApproachTarget(GeoCoordinate Coordinate, bool ConnectsToDestination);

    /// <summary>Road access can be shown before facility connectivity is known.</summary>
    public RoadApproachTarget? FindRoadApproachTarget(GeoCoordinate origin, GeoCoordinate destination)
    {
        GeoCoordinate? connected = FindApproachCoordinate(origin, destination);
        if (connected.HasValue) return new(connected.Value, true);
        if (!origin.IsValid || !destination.IsValid) return null;
        var nearest = FindNearestEdgeProjection(origin, null, 50.0);
        return nearest.IsAvailable ? new(nearest.SnappedCoordinate, false) : null;
    }

    private EdgeProjection FindNearestEdgeProjection(
        GeoCoordinate point, IReadOnlySet<int>? destinationComponents,
        double maximumSnapMeters = MaximumOriginSnapMeters)
    {
        EdgeProjection nearest =
            EdgeProjection.Unavailable;

        foreach (RoadEdge edge in
                 graph.Edges)
        {
            if (destinationComponents is not null &&
                !destinationComponents.Contains(componentByNode[edge.From.Id]))
                continue;
            EdgeProjection candidate =
                ProjectOntoEdge(
                    point,
                    edge);

            if (!candidate.IsAvailable ||
                candidate.CrossTrackMeters > maximumSnapMeters ||
                (nearest.IsAvailable &&
                 candidate.CrossTrackMeters >=
                    nearest.CrossTrackMeters) ||
                graph.AccessCrossesMajorRoad(point, candidate.SnappedCoordinate))
            {
                continue;
            }

            nearest =
                candidate;
        }

        return nearest;
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
        RoadNode targetNode,
        GeoCoordinate snappedStartCoordinate,
        IReadOnlyDictionary<int, double> startSeedCosts,
        double totalDistance,
        string algorithmName)
    {
        var reversedNodes = new List<RoadNode>();
        var reversedEdges = new List<RoadEdge>();

        int currentId = targetNode.Id;
        reversedNodes.Add(targetNode);

        while (cameFrom.TryGetValue(currentId, out var tuple))
        {
            reversedEdges.Add(tuple.UsedEdge);
            currentId = tuple.ParentId;
            if (graph.Nodes.TryGetValue(currentId, out var parentNode))
            {
                reversedNodes.Add(parentNode);
            }
        }

        reversedNodes.Reverse();
        reversedEdges.Reverse();

        var routePoints =
            new List<RoutePoint>
            {
                new(
                    snappedStartCoordinate,
                    0.0)
            };

        if (reversedNodes.Count ==
            0)
        {
            return new RouteResult(
                routePoints,
                0.0,
                algorithmName);
        }

        RoadNode firstGraphNode =
            reversedNodes[0];

        double accumulatedDistance =
            startSeedCosts.TryGetValue(
                firstGraphNode.Id,
                out double seedCost)
                ? seedCost
                : snappedStartCoordinate.DistanceTo(
                    firstGraphNode.Coordinate);

        if (snappedStartCoordinate.DistanceTo(
                firstGraphNode.Coordinate) >
            0.05)
        {
            routePoints.Add(
                new RoutePoint(
                    firstGraphNode.Coordinate,
                    accumulatedDistance));
        }

        for (int i = 1; i < reversedNodes.Count; i++)
        {
            if (i - 1 <
                reversedEdges.Count)
            {
                accumulatedDistance +=
                    reversedEdges[i - 1]
                        .LengthMeters;
            }

            routePoints.Add(
                new RoutePoint(
                    reversedNodes[i]
                        .Coordinate,
                    accumulatedDistance));
        }

        return new RouteResult(routePoints, totalDistance, algorithmName);
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
