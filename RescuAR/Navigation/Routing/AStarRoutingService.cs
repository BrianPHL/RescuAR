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

        // 1. Snap origin and destination coordinates to nearest graph nodes.
        RoadNode? startNode = FindNearestNode(origin);
        RoadNode? targetNode = FindNearestNode(destination);

        if (startNode == null || targetNode == null)
        {
            return null;
        }

        if (startNode.Id == targetNode.Id)
        {
            var singlePoint = new RoutePoint(startNode.Coordinate, 0.0);
            return new RouteResult(new[] { singlePoint }, 0.0, algorithmName);
        }

        // 2. A* Search Data Structures
        var openSet = new PriorityQueue<int, double>();
        var gScore = new Dictionary<int, double>();
        var cameFrom = new Dictionary<int, (int ParentId, RoadEdge UsedEdge)>();
        var closedSet = new HashSet<int>();

        gScore[startNode.Id] = 0.0;
        double initialH = startNode.Coordinate.DistanceTo(targetNode.Coordinate);
        openSet.Enqueue(startNode.Id, initialH);

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
                        startNode,
                        targetNode,
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

    private RouteResult ReconstructRoute(
        Dictionary<int, (int ParentId, RoadEdge UsedEdge)> cameFrom,
        RoadNode startNode,
        RoadNode targetNode,
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

        var routePoints = new List<RoutePoint>();
        double accumulatedDistance = 0.0;

        for (int i = 0; i < reversedNodes.Count; i++)
        {
            if (i > 0 && i - 1 < reversedEdges.Count)
            {
                accumulatedDistance += reversedEdges[i - 1].LengthMeters;
            }

            routePoints.Add(new RoutePoint(reversedNodes[i].Coordinate, accumulatedDistance));
        }

        return new RouteResult(routePoints, totalDistance, algorithmName);
    }
}
