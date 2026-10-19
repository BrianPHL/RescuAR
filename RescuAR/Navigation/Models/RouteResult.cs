using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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
    public string GeometryFingerprint { get; }

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
        var geometry = new StringBuilder();
        foreach (var point in Points)
            geometry.Append(point.Coordinate.Latitude.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(point.Coordinate.Longitude.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                .Append(point.DistanceFromStartMeters.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        GeometryFingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(geometry.ToString()))).ToLowerInvariant();
    }
}
