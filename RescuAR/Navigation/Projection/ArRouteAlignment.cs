using System;
using System.Collections.Generic;

namespace RescuAR.Navigation.Projection;

/// <summary>
/// Rotates local East/North route coordinates into an AR horizontal X/Z frame.
///
/// Convention:
/// - mapToArYawDegrees = 0:
///     East  -> +X
///     North -> -Z
///
/// The caller determines mapToArYawDegrees from the eventual map/heading to
/// ARCore alignment stage. Keeping this explicit prevents GPS/PDR from
/// directly driving the Evergine camera.
/// </summary>
public static class ArRouteAlignment
{
    public static IReadOnlyList<ArHorizontalRoutePoint> Rotate(
        IReadOnlyList<LocalRoutePoint> points,
        double mapToArYawDegrees)
    {
        ArgumentNullException.ThrowIfNull(
            points);

        if (points.Count ==
            0)
        {
            return [];
        }

        List<ArHorizontalRoutePoint> result =
            new(
                points.Count);

        foreach (LocalRoutePoint point in
                 points)
        {
            var (x, z) = MapToArCoordinates.Rotate(
                point.EastMeters, point.NorthMeters, mapToArYawDegrees);

            result.Add(
                new ArHorizontalRoutePoint(
                    (float)x,
                    (float)z,
                    point.DistanceFromWindowStartMeters));
        }

        return result;
    }
}
