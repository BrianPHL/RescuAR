using System;
using System.Collections.Generic;

namespace RescuAR.Navigation.Models;

/// <summary>
/// Common routing result consumed by later map-to-AR conversion.
///
/// Both MLD and A* should eventually return this same model.
/// </summary>
public sealed class RouteResult
{
    public IReadOnlyList<RoutePoint> Points { get; }

    public double TotalDistanceMeters { get; }

    public string Algorithm { get; }

    public RouteResult(
        IReadOnlyList<RoutePoint> points,
        double totalDistanceMeters,
        string algorithm)
    {
        Points =
            points ??
            throw new ArgumentNullException(
                nameof(points));

        TotalDistanceMeters =
            totalDistanceMeters;

        Algorithm =
            algorithm ??
            string.Empty;
    }
}
