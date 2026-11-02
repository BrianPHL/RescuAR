using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;

namespace RescuAR.MAUI.Services.Location;

/// <summary>Ranks actual observations for A*. Ranking never changes a fix's
/// coordinates, uncertainty, acquisition time or provider metadata.</summary>
public static class AStarLocationAcquisitionPolicy
{
    public const double DesiredAccuracyMeters = 6;
    public static readonly TimeSpan MaximumFixAge = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan CollectionWindow = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ImmediateFixAge = TimeSpan.FromMilliseconds(500);

    public static bool UseForStartup(RouteResult? retainedRoute, bool hasDestination, bool internetAvailable) =>
        hasDestination && !internetAvailable &&
        (retainedRoute is null || AStarRouteDisplayPolicy.AppliesTo(retainedRoute));

    public static bool IsEligible(LocationReading? reading, DateTimeOffset now, DateTimeOffset? newerThan) =>
        reading is not null && LocationAcquisitionPolicy.IsCurrent(reading, now) &&
        now - reading.Timestamp <= MaximumFixAge &&
        reading.AccuracyMeters <= RouteStartupLocationPolicy.MaximumPlanningAccuracyMeters &&
        (!newerThan.HasValue || reading.Timestamp > newerThan.Value);

    public static LocationReading? Select(LocationReading? gps, LocationReading? network,
        LocationReading? retained, DateTimeOffset now, DateTimeOffset? newerThan)
    {
        LocationReading? best = null;
        double bestScore = double.PositiveInfinity;
        foreach (var reading in new[] { gps, network, retained })
        {
            if (!IsEligible(reading, now, newerThan)) continue;
            // Age is an additional selection cost (1.5 m/s pedestrian travel),
            // not a claim that the provider's reported accuracy has changed.
            double score = reading!.AccuracyMeters!.Value + (now - reading.Timestamp).TotalSeconds * 1.5;
            if (score < bestScore || score == bestScore && reading.Timestamp > best!.Timestamp)
            {
                best = reading;
                bestScore = score;
            }
        }
        return best;
    }

    public static bool CanComplete(LocationReading? reading, DateTimeOffset now,
        DateTimeOffset? newerThan, TimeSpan elapsed) =>
        IsEligible(reading, now, newerThan) && elapsed >= TimeSpan.Zero &&
        (elapsed >= CollectionWindow || reading!.Provider == "gps" &&
         reading.AccuracyMeters <= DesiredAccuracyMeters && now - reading.Timestamp <= ImmediateFixAge);

    public static async Task<LocationReading?> GetAsync(ILocationService source, bool useAStar,
        DateTimeOffset? newerThan, CancellationToken cancellationToken = default)
    {
        // Preserve the existing MLD/default acquisition call and result.
        if (!useAStar) return await source.GetCurrentLocationAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var reading = source is IAStarLocationSource precise
            ? await precise.GetAStarLocationAsync(newerThan, cancellationToken)
            : await source.GetCurrentLocationAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return IsEligible(reading, DateTimeOffset.UtcNow, newerThan) ? reading : null;
    }
}
