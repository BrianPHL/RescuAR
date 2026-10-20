using System;
using System.Numerics;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Projection;

/// <summary>A screen bearing to the destination, separate from mapped walking geometry.</summary>
public static class DestinationBearingCuePolicy
{
    public static bool TryGetAngle(GeoCoordinate origin, GeoCoordinate destination,
        double mapToArYawDegrees, Quaternion cameraRotation, out double angle)
    {
        angle = 0;
        double distance = origin.DistanceTo(destination);
        float length = cameraRotation.LengthSquared();
        if (!origin.IsValid || !destination.IsValid || !double.IsFinite(distance) ||
            distance < 5 || distance > 100_000 || !double.IsFinite(mapToArYawDegrees) ||
            !float.IsFinite(length) || length < 0.0001f) return false;
        const double radius = 6371008.8;
        double east = (destination.Longitude - origin.Longitude) * Math.PI / 180 * radius *
            Math.Cos(origin.Latitude * Math.PI / 180);
        double north = (destination.Latitude - origin.Latitude) * Math.PI / 180 * radius;
        var world = MapToArCoordinates.Rotate(east, north, mapToArYawDegrees);
        var local = Vector3.Transform(new Vector3((float)world.X, 0, (float)world.Z),
            Quaternion.Inverse(Quaternion.Normalize(cameraRotation)));
        if (!float.IsFinite(local.X) || !float.IsFinite(local.Z) ||
            local.X * local.X + local.Z * local.Z < 0.0001f) return false;
        angle = Math.Atan2(local.X, -local.Z) * 180 / Math.PI;
        return double.IsFinite(angle);
    }
}
