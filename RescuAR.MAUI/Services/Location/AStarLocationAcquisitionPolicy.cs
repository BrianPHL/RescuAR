using RescuAR.Navigation.Timing;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;

namespace RescuAR.MAUI.Services.Location;

/// <summary>Ranks actual observations for A*. Ranking never changes a fix's
/// coordinates, uncertainty, acquisition time or provider metadata.</summary>
public static class AStarLocationAcquisitionPolicy
{
    public const double DesiredAccuracyMeters = LocationAcquisitionPolicy.DesiredAccuracyMeters;
    public static readonly TimeSpan MaximumFixAge = LocationAcquisitionPolicy.MaximumFixAge;
    public static readonly TimeSpan CollectionWindow = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(15);

    public static bool UseForStartup(RouteResult? retainedRoute, bool hasDestination, bool internetAvailable) =>
        hasDestination && !internetAvailable &&
        (retainedRoute is null || AStarRouteDisplayPolicy.AppliesTo(retainedRoute));

    public static bool IsEligible(LocationReading? reading, DateTimeOffset now, DateTimeOffset? newerThan) =>
        reading is not null && LocationAcquisitionPolicy.IsCurrent(reading, now) &&
        now - reading.ObservationTime <= MaximumFixAge &&
        reading.AccuracyMeters <= RouteStartupLocationPolicy.MaximumPlanningAccuracyMeters &&
        (!newerThan.HasValue || reading.ObservationTime > newerThan.Value);

    public static LocationReading? Select(LocationReading? gps, LocationReading? network,
        LocationReading? retained, DateTimeOffset now, DateTimeOffset? newerThan, LocationReading? fused = null)
    {
        LocationReading? best = null;
        double bestScore = double.PositiveInfinity;
        foreach (var reading in new[] { gps, network, fused, retained })
        {
            if (!IsEligible(reading, now, newerThan)) continue;
            // Age is an additional selection cost (1.5 m/s pedestrian travel),
            // not a claim that the provider's reported accuracy has changed.
            double score = LocationAcquisitionPolicy.Score(reading!, now);
            if (score < bestScore || score == bestScore && reading!.ObservationTime > best!.ObservationTime)
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
        (elapsed >= CollectionWindow || (reading!.Provider is "gps" or "fused") &&
         reading.AccuracyMeters <= DesiredAccuracyMeters);

    public static async Task<LocationReading?> GetAsync(ILocationService source, bool useAStar,
        DateTimeOffset? newerThan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source is INavigationLocationSource navigation)
        {
            var current = await navigation.GetNavigationLocationAsync(newerThan, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var now = current?.AcquisitionMonotonicMilliseconds is not null ? NavigationClock.UtcNow : DateTimeOffset.UtcNow;
            return IsEligible(current, now, newerThan) ? current : null;
        }
        // Legacy providers keep their default path until they implement the
        // shared navigation capability. Native Android uses it for both routers.
        if (!useAStar) return await source.GetCurrentLocationAsync(cancellationToken);
        var reading = source is IAStarLocationSource precise
            ? await precise.GetAStarLocationAsync(newerThan, cancellationToken)
            : await source.GetCurrentLocationAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var evaluatedAt = reading?.AcquisitionMonotonicMilliseconds is not null ? NavigationClock.UtcNow : DateTimeOffset.UtcNow;
        return IsEligible(reading, evaluatedAt, newerThan) ? reading : null;
    }
}
