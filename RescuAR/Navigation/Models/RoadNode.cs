using System;
using System.Collections.Generic;

namespace RescuAR.Navigation.Models;

/// <summary>
/// Node in the pedestrian road graph.
/// </summary>
public sealed class RoadNode
{
    public int Id { get; }

    public GeoCoordinate Coordinate { get; }

    public List<RoadEdge> Edges { get; } =
        new();

    public RoadNode(
        int id,
        GeoCoordinate coordinate)
    {
        if (!coordinate.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coordinate));
        }

        Id =
            id;

        Coordinate =
            coordinate;
    }

    internal void AddEdge(
        RoadEdge edge)
    {
        ArgumentNullException.ThrowIfNull(
            edge);

        Edges.Add(
            edge);
    }
}
