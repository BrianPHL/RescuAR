using System;

namespace RescuAR.Navigation.Projection;

/// <summary>
/// Converts geographic East/North metres into ARCore's horizontal frame.
/// At zero yaw East is +X, Up is +Y, and North is -Z. Keeping North as +Z
/// would reflect the map; a yaw rotation cannot correct that reflection.
/// </summary>
public static class MapToArCoordinates
{
    public static (double X, double Z) Rotate(double eastMeters, double northMeters,
        double mapToArYawDegrees)
    {
        double radians = mapToArYawDegrees * Math.PI / 180.0;
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        return (eastMeters * cos - northMeters * sin,
            -eastMeters * sin - northMeters * cos);
    }

    /// <summary>
    /// AR azimuth is atan2(X,Z); geographic bearing is clockwise from North.
    /// Their sum, rather than difference, is invariant as the camera turns.
    /// </summary>
    public static double CalculateYawDegrees(double arAzimuthDegrees,
        double geographicBearingDegrees)
    {
        double yaw = (arAzimuthDegrees + geographicBearingDegrees - 180.0) % 360.0;
        return yaw > 180.0 ? yaw - 360.0 : yaw < -180.0 ? yaw + 360.0 : yaw;
    }
    public static double CalculateArAzimuthDegrees(double geographicBearingDegrees,
        double mapToArYawDegrees)
    {
        double azimuth = (180.0 + mapToArYawDegrees - geographicBearingDegrees) % 360.0;
        return azimuth < 0 ? azimuth + 360.0 : azimuth;
    }

    /// <summary>Positive screen rotation means right; AR azimuth winds the opposite way.</summary>
    public static double CalculateCameraRelativeAngle(double targetArAzimuthDegrees,
        double cameraArAzimuthDegrees)
    {
        double angle = (cameraArAzimuthDegrees - targetArAzimuthDegrees) % 360.0;
        return angle > 180.0 ? angle - 360.0 : angle < -180.0 ? angle + 360.0 : angle;
    }

}
