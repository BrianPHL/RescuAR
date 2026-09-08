using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Routing;

/// <summary>
/// Offline A* (A-Star) routing service implementation.
/// Calculates shortest and safest pedestrian paths using the in-memory RoadGraph.
/// </summary>
public sealed class AStarRoutingService : IRoutingService
{
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

        return Task.Run(() => ComputeRoute(origin, destination, cancellationToken), cancellationToken);
    }

    private RouteResult? ComputeRoute(
        GeoCoordinate origin,
        GeoCoordinate destination,
        CancellationToken cancellationToken)
    {
        if (graph.Nodes.Count == 0)
        {
            return null;
        }

        // 1. Snap origin and destination coordinates to nearest nodes in the graph
        RoadNode? startNode = FindNearestNode(origin);
        RoadNode? targetNode = FindNearestNode(destination);

        if (startNode == null || targetNode == null)
        {
            return null;
        }

        if (startNode.Id == targetNode.Id)
        {
            var singlePoint = new RoutePoint(startNode.Coordinate, 0.0);
            return new RouteResult(new[] { singlePoint }, 0.0, AlgorithmName);
        }

        // 2. A* Search Data Structures
        var openSet = new PriorityQueue<int, double>();
        var gScore = new Dictionary<int, double>();
        var cameFrom = new Dictionary<int, (int ParentId, RoadEdge UsedEdge)>();
        var closedSet = new HashSet<int>();

        gScore[startNode.Id] = 0.0;
        double initialH = startNode.Coordinate.DistanceTo(targetNode.Coordinate);
        openSet.Enqueue(startNode.Id, initialH);

        while (openSet.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int currentId = openSet.Dequeue();

            if (currentId == targetNode.Id)
            {
                return ReconstructRoute(cameFrom, startNode, targetNode, gScore[targetNode.Id]);
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

        // Path not found
        return null;
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
        double totalDistance)
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

        return new RouteResult(routePoints, totalDistance, AlgorithmName);
    }
}
