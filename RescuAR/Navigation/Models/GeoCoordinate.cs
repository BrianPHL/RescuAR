using System;

namespace RescuAR.Navigation.Models;

/// <summary>
/// WGS84 geographic coordinate.
/// GeoJSON stores coordinates in [longitude, latitude] order.
/// </summary>
public readonly record struct GeoCoordinate(
    double Latitude,
    double Longitude)
{
    public bool IsValid =>
        !double.IsNaN(Latitude) &&
        !double.IsNaN(Longitude) &&
        Latitude >= -90.0 &&
        Latitude <= 90.0 &&
        Longitude >= -180.0 &&
        Longitude <= 180.0;

    /// <summary>
    /// Great-circle distance in meters using the haversine approximation.
    /// This is sufficient for pedestrian graph edge weights at city scale.
    /// </summary>
    public double DistanceTo(
        GeoCoordinate other)
    {
        const double EarthRadiusMeters =
            6371008.8;

        double latitude1 =
            DegreesToRadians(
                Latitude);

        double latitude2 =
            DegreesToRadians(
                other.Latitude);

        double deltaLatitude =
            latitude2 -
            latitude1;

        double deltaLongitude =
            DegreesToRadians(
                other.Longitude -
                Longitude);

        double sinLatitude =
            Math.Sin(
                deltaLatitude /
                2.0);

        double sinLongitude =
            Math.Sin(
                deltaLongitude /
                2.0);

        double a =
            sinLatitude *
            sinLatitude +
            Math.Cos(
                latitude1) *
            Math.Cos(
                latitude2) *
            sinLongitude *
            sinLongitude;

        double c =
            2.0 *
            Math.Atan2(
                Math.Sqrt(a),
                Math.Sqrt(
                    1.0 - a));

        return EarthRadiusMeters *
            c;
    }

    private static double DegreesToRadians(
        double degrees)
    {
        return degrees *
            (Math.PI / 180.0);
    }
}
