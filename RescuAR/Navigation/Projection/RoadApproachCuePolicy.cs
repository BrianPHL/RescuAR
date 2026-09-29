using System;
using System.Numerics;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Projection;

/// <summary>Direction cues never manufacture a walking route.</summary>
public static class RoadApproachCuePolicy
{
    public static bool TryGetDirection(GeoCoordinate origin, GeoCoordinate target,
        double mapToArYawDegrees, out Vector2 direction, out double distance)
    {
        direction = default;
        distance = origin.DistanceTo(target);
        if (!origin.IsValid || !target.IsValid || !double.IsFinite(mapToArYawDegrees) ||
            !double.IsFinite(distance) || distance < 1 || distance > 50) return false;
        const double radius = 6371008.8;
        double east = (target.Longitude - origin.Longitude) * Math.PI / 180 * radius *
            Math.Cos(origin.Latitude * Math.PI / 180);
        double north = (target.Latitude - origin.Latitude) * Math.PI / 180 * radius;
        var projected = MapToArCoordinates.Rotate(east, north, mapToArYawDegrees);
        direction = new((float)projected.X, (float)projected.Z);
        float length = direction.LengthSquared();
        if (!float.IsFinite(length) || length < 0.0001f) return false;
        direction = Vector2.Normalize(direction);
        return true;
    }

    public static bool TryGetAngle(GeoCoordinate origin, GeoCoordinate target,
        double mapToArYawDegrees, Quaternion cameraRotation, out double angle, out double distance)
    {
        angle = 0;
        if (!TryGetDirection(origin, target, mapToArYawDegrees, out var direction, out distance)) return false;
        float length = cameraRotation.LengthSquared();
        if (!float.IsFinite(length) || length < 0.0001f) return false;
        var local = Vector3.Transform(new Vector3(direction.X, 0, direction.Y),
            Quaternion.Inverse(Quaternion.Normalize(cameraRotation)));
        if (!float.IsFinite(local.X) || !float.IsFinite(local.Z) ||
            local.X * local.X + local.Z * local.Z < 0.0001f) return false;
        angle = Math.Atan2(local.X, -local.Z) * 180 / Math.PI;
        return double.IsFinite(angle);
    }
}
