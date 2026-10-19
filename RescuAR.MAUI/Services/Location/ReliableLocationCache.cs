using System.Text.Json;
using Microsoft.Maui.Storage;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Projection;

namespace RescuAR.MAUI.Services.Location;

/// <summary>Display-only cache eligibility; routing and arrival keep their stricter policies.</summary>
public static class LocationReadingQuality
{
    public static readonly TimeSpan MaximumCachedDisplayAge = TimeSpan.FromMinutes(2);

    public static bool CanDisplay(LocationReading? reading, DateTimeOffset now) =>
        reading is not null && reading.Coordinate.IsValid &&
        reading.AccuracyMeters is double accuracy && double.IsFinite(accuracy) &&
        accuracy >= 0 && accuracy <= RouteStartupLocationPolicy.MaximumPlanningAccuracyMeters &&
        now >= reading.Timestamp && now - reading.Timestamp <= MaximumCachedDisplayAge;

    public static bool IsFresh(LocationReading? reading, DateTimeOffset now) =>
        reading is not null && RouteStartupLocationPolicy.CanPlan(reading.Coordinate,
            reading.AccuracyMeters, reading.Timestamp, now, cached: false);
}

/// <summary>
/// Retains actual qualified device readings across page changes and offline
/// restarts. Never changes acquisition time or replaces a fix with an estimate.
/// Read only after location permission has been checked by the service.
/// </summary>
internal sealed class ReliableLocationCache
{
    private const string PreferenceKey = "Navigation.LastReliableLocation.v1";
    private readonly object sync = new();
    private LocationReading? latest;
    private bool loaded;
    private DateTimeOffset lastSavedAt;

    public LocationReading? Get()
    {
        lock (sync)
        {
            if (!loaded)
            {
                loaded = true;
                try
                {
                    var saved = JsonSerializer.Deserialize<SavedReading>(
                        Preferences.Default.Get(PreferenceKey, "null"));
                    if (saved is not null)
                        latest = new(new GeoCoordinate(saved.Latitude, saved.Longitude),
                            saved.Accuracy, null, null, null, saved.Timestamp);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    RescuAR.Diagnostics.AndroidLog.Warn("RescuAR-GPS",
                        "Retained location unavailable; requesting device location: " +
                        RescuAR.Diagnostics.DiagnosticPrivacyPolicy.FormatException(exception));
                }
            }
            if (!LocationReadingQuality.CanDisplay(latest, DateTimeOffset.UtcNow)) latest = null;
            return latest;
        }
    }

    public void Observe(LocationReading reading)
    {
        lock (sync)
        {
            var now = DateTimeOffset.UtcNow;
            Get();
            if (!LocationReadingQuality.CanDisplay(reading, now) ||
                (latest is not null && reading.Timestamp <= latest.Timestamp)) return;
            latest = reading;
            // Keep foreground polling from writing preferences on every fix.
            if (now - lastSavedAt < TimeSpan.FromSeconds(15)) return;
            try
            {
                Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(new SavedReading(
                    reading.Coordinate.Latitude, reading.Coordinate.Longitude,
                    reading.AccuracyMeters!.Value, reading.Timestamp)));
                lastSavedAt = now;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                RescuAR.Diagnostics.AndroidLog.Warn("RescuAR-GPS",
                    "Last reliable location remains in memory; persistence unavailable: " +
                    RescuAR.Diagnostics.DiagnosticPrivacyPolicy.FormatException(exception));
            }
        }
    }

    private sealed record SavedReading(double Latitude, double Longitude,
        double Accuracy, DateTimeOffset Timestamp);
}
