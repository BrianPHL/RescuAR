namespace RescuAR.MAUI.Services.Location;

/// <summary>Selects actual device fixes without relabeling their accuracy or time.</summary>
public static class LocationAcquisitionPolicy
{
    public static readonly TimeSpan MaximumFixAge = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan CollectionWindow = TimeSpan.FromSeconds(2);
    public const double DesiredAccuracyMeters = 8;

    // Re-reading one native fix is not another observation or a loss of quality.
    // Keep its original timestamp; downstream expiry still applies normally.
    public static bool IsNewObservation(LocationReading reading, DateTimeOffset? consumedAt) =>
        !consumedAt.HasValue || reading.ObservationTime > consumedAt.Value;

    public static bool IsCurrent(LocationReading? reading, DateTimeOffset now) =>
        reading is not null && reading.Coordinate.IsValid &&
        reading.AccuracyMeters is { } accuracy && double.IsFinite(accuracy) && accuracy > 0 &&
        now >= reading.ObservationTime && now - reading.ObservationTime <= MaximumFixAge;

    public static LocationReading? Select(LocationReading? gps, LocationReading? network, DateTimeOffset now,
        LocationReading? fused = null)
    {
        if (!IsCurrent(gps, now)) gps = null;
        if (!IsCurrent(network, now)) network = null;
        LocationReading? best = gps is null ? network : network is null ? gps : Score(gps, now) == Score(network, now)
            ? gps.ObservationTime >= network.ObservationTime ? gps : network
            : Score(gps, now) < Score(network, now) ? gps : network;
        if (IsCurrent(fused, now) && (best is null || Score(fused!, now) < Score(best, now) ||
            Score(fused!, now) == Score(best, now) && fused!.ObservationTime > best.ObservationTime)) best = fused;
        return best;
    }

    public static double Score(LocationReading reading, DateTimeOffset now) =>
        reading.AccuracyMeters!.Value + (now - reading.ObservationTime).TotalSeconds * 1.5;

    public static bool CanComplete(LocationReading? reading, DateTimeOffset now, TimeSpan elapsed) =>
        IsCurrent(reading, now) && elapsed >= TimeSpan.Zero &&
        ((reading!.Provider is "gps" or "fused") && reading.AccuracyMeters <= DesiredAccuracyMeters || elapsed >= CollectionWindow);
}
