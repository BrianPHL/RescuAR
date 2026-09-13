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

    private readonly RoadGraph graph;

    public string AlgorithmName => "AStar";

    public AStarRoutingService(RoadGraph graph)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
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

        // Keep destination node snapping until destination-edge arrival is
        // implemented as a separate, bounded change.
        RoadNode? targetNode =
            FindNearestNode(
                destination);

        if (targetNode is null)
        {
            return null;
        }

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
                    origin);

            if (startNode is null)
            {
                return null;
            }

            snappedStartCoordinate =
                startNode.Coordinate;

            startSeedCosts[startNode.Id] =
                0.0;
        }
        else
        {
            EdgeProjection startProjection =
                FindNearestEdgeProjection(
                    origin);

            if (!startProjection.IsAvailable ||
                startProjection.Edge is null)
            {
                return null;
            }

            RoadEdge startEdge =
                startProjection.Edge;

            snappedStartCoordinate =
                startProjection.SnappedCoordinate;

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

        // 2. A* Search Data Structures
        var openSet = new PriorityQueue<int, double>();
        var gScore = new Dictionary<int, double>();
        var cameFrom = new Dictionary<int, (int ParentId, RoadEdge UsedEdge)>();
        var closedSet = new HashSet<int>();

        foreach (KeyValuePair<int, double> seed in
                 startSeedCosts)
        {
            if (!graph.Nodes.TryGetValue(
                    seed.Key,
                    out RoadNode? seedNode))
            {
                continue;
            }

            gScore[seed.Key] =
                seed.Value;

            double initialH =
                seedNode.Coordinate.DistanceTo(
                    targetNode.Coordinate);

            openSet.Enqueue(
                seed.Key,
                seed.Value +
                    initialH);
        }

        if (openSet.Count ==
            0)
        {
            return null;
        }

        int blockedEdgeCount =
            0;

        while (openSet.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int currentId = openSet.Dequeue();

            if (currentId == targetNode.Id)
            {
                RouteResult result =
                    ReconstructRoute(
                        cameFrom,
                        targetNode,
                        snappedStartCoordinate,
                        startSeedCosts,
                        gScore[targetNode.Id],
                        algorithmName);

                if (hazardAware)
                {
                    AndroidLog.Warn(
                        LogTag,
                        "Hazard-aware A* route calculated successfully: " +
                        $"hazards={hazards!.Count}, " +
                        $"blockedEdgeChecks={blockedEdgeCount}, " +
                        $"points={result.Points.Count}, " +
                        $"distance={result.TotalDistanceMeters:F1} m.");
                }

                return result;
            }

            if (!closedSet.Add(currentId))
            {
                continue;
            }

            if (!graph.Nodes.TryGetValue(currentId, out var currentNode))
            {
                continue;
            }

            double currentG = gScore[currentId];

            foreach (var edge in currentNode.Edges)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (hazardAware &&
                    IsEdgeBlockedByHazards(
                        edge,
                        origin,
                        hazards!))
                {
                    blockedEdgeCount++;
                    continue;
                }

                RoadNode neighbor = edge.To;
                if (closedSet.Contains(neighbor.Id))
                {
                    continue;
                }

                double tentativeGScore = currentG + edge.Cost;

                if (!gScore.TryGetValue(neighbor.Id, out double neighborG) || tentativeGScore < neighborG)
                {
                    cameFrom[neighbor.Id] = (currentId, edge);
                    gScore[neighbor.Id] = tentativeGScore;

                    double hScore = neighbor.Coordinate.DistanceTo(targetNode.Coordinate);
                    double fScore = tentativeGScore + hScore;

                    openSet.Enqueue(neighbor.Id, fScore);
                }
            }
        }

        if (hazardAware)
        {
            AndroidLog.Warn(
                LogTag,
                "Hazard-aware A* could not find a safe replacement route: " +
                $"hazards={hazards!.Count}, blockedEdgeChecks={blockedEdgeCount}.");
        }

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

    private RoadNode? FindNearestNode(GeoCoordinate point)
    {
        RoadNode? nearest = null;
        double minDistance = double.MaxValue;

        foreach (var node in graph.Nodes.Values)
        {
            double dist = node.Coordinate.DistanceTo(point);
            if (dist < minDistance)
            {
                minDistance = dist;
                nearest = node;
            }
        }

        return nearest;
    }

    private EdgeProjection FindNearestEdgeProjection(
        GeoCoordinate point)
    {
        EdgeProjection nearest =
            EdgeProjection.Unavailable;

        foreach (RoadEdge edge in
                 graph.Edges)
        {
            EdgeProjection candidate =
                ProjectOntoEdge(
                    point,
                    edge);

            if (!candidate.IsAvailable ||
                (nearest.IsAvailable &&
                 candidate.CrossTrackMeters >=
                    nearest.CrossTrackMeters))
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
