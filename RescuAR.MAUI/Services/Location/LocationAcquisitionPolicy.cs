namespace RescuAR.MAUI.Services.Location;

/// <summary>Selects actual device fixes without relabeling their accuracy or time.</summary>
public static class LocationAcquisitionPolicy
{
    public static readonly TimeSpan MaximumFixAge = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan CollectionWindow = TimeSpan.FromSeconds(2);
    public const double DesiredAccuracyMeters = 8;

    public static bool IsCurrent(LocationReading? reading, DateTimeOffset now) =>
        reading is not null && reading.Coordinate.IsValid &&
        reading.AccuracyMeters is { } accuracy && double.IsFinite(accuracy) && accuracy > 0 &&
        now >= reading.Timestamp && now - reading.Timestamp <= MaximumFixAge;

    public static LocationReading? Select(LocationReading? gps, LocationReading? network, DateTimeOffset now)
    {
        if (!IsCurrent(gps, now)) gps = null;
        if (!IsCurrent(network, now)) network = null;
        if (gps is null) return network;
        if (network is null) return gps;
        if (gps.AccuracyMeters == network.AccuracyMeters)
            return gps.Timestamp >= network.Timestamp ? gps : network;
        return gps.AccuracyMeters < network.AccuracyMeters ? gps : network;
    }

    public static bool CanComplete(LocationReading? reading, DateTimeOffset now, TimeSpan elapsed) =>
        IsCurrent(reading, now) && elapsed >= TimeSpan.Zero &&
        (reading!.Provider == "gps" && reading.AccuracyMeters <= DesiredAccuracyMeters || elapsed >= CollectionWindow);
}
