using System;
using System.Numerics;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Projection;

/// <summary>Camera-relative direction only; never manufactures route geometry.</summary>
public static class RoadApproachCuePolicy
{
    public static bool TryGetAngle(GeoCoordinate origin, GeoCoordinate target,
        double mapToArYawDegrees, Quaternion cameraRotation, out double angle, out double distance)
    {
        angle = 0;
        distance = origin.DistanceTo(target);
        float length = cameraRotation.LengthSquared();
        if (!origin.IsValid || !target.IsValid || !double.IsFinite(mapToArYawDegrees) ||
            !double.IsFinite(distance) || distance < 1 || distance > 50 ||
            !float.IsFinite(length) || length < 0.0001f) return false;
        const double radius = 6371008.8;
        double east = (target.Longitude - origin.Longitude) * Math.PI / 180 * radius *
            Math.Cos(origin.Latitude * Math.PI / 180);
        double north = (target.Latitude - origin.Latitude) * Math.PI / 180 * radius;
        double yaw = mapToArYawDegrees * Math.PI / 180;
        var local = Vector3.Transform(new Vector3(
            (float)(east * Math.Cos(yaw) + north * Math.Sin(yaw)), 0,
            (float)(-east * Math.Sin(yaw) + north * Math.Cos(yaw))),
            Quaternion.Inverse(Quaternion.Normalize(cameraRotation)));
        if (!float.IsFinite(local.X) || !float.IsFinite(local.Z) ||
            local.X * local.X + local.Z * local.Z < 0.0001f) return false;
        angle = Math.Atan2(local.X, -local.Z) * 180 / Math.PI;
        return double.IsFinite(angle);
    }
}
