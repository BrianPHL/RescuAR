using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Models;

internal static class AStarLocationAcquisitionChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var at = DateTimeOffset.UtcNow;
        LocationReading Fix(string provider, double? accuracy, double age = 0) =>
            new(new GeoCoordinate(14.6448, 121.095), accuracy, 12, 1.2, 275, at.AddSeconds(-age)) {
                Provider = provider, CourseAccuracyDegrees = 7, AcquisitionElapsedRealtimeNanoseconds = 123456789 };
        var oldPrecise = Fix("gps", 4, 2);
        var currentNetwork = Fix("network", 12);
        var points = new[] { new RoutePoint(new(14.6448, 121.095), 0), new RoutePoint(new(14.6449, 121.095), 11) };
        var astarTrip = new RouteResult(points, 11, "AStar", new RouteProvenance("ASTAR"));
        var mldTrip = new RouteResult(points, 11, "MLD (Railway OSRM)", new RouteProvenance("MLD"));
        check(AStarLocationAcquisitionPolicy.UseForStartup(null, true, false) &&
            AStarLocationAcquisitionPolicy.UseForStartup(astarTrip, true, false),
            "A* GPS: a new offline trip and a retained offline A* trip request fresh startup evidence");
        check(!AStarLocationAcquisitionPolicy.UseForStartup(null, true, true) &&
            !AStarLocationAcquisitionPolicy.UseForStartup(null, false, false),
            "MLD lock: an online start and ordinary map browsing keep their existing acquisition path");
        check(!AStarLocationAcquisitionPolicy.UseForStartup(mldTrip, true, false) &&
            !AStarLocationAcquisitionPolicy.UseForStartup(mldTrip, true, true),
            "MLD lock: a retained MLD trip cannot switch GPS policy when Internet connectivity changes");
        check(ReferenceEquals(LocationAcquisitionPolicy.Select(oldPrecise, currentNetwork, at), oldPrecise) &&
            ReferenceEquals(AStarLocationAcquisitionPolicy.Select(oldPrecise, currentNetwork, null, at, null), oldPrecise),
            "navigation GPS: both providers use the same bounded age/precision ranking for a two-second-old fix");
        var fresh = Fix("gps", 6);
        var retained = Fix("gps", 5, 1);
        check(ReferenceEquals(AStarLocationAcquisitionPolicy.Select(fresh, null, retained, at, null), fresh),
            "A* GPS: a fresh six-metre fix outranks an older five-metre fix after accounting for pedestrian travel");
        var better = Fix("gps", 4, .2);
        check(ReferenceEquals(AStarLocationAcquisitionPolicy.Select(Fix("gps", 10), null, better, at, null), better),
            "A* GPS: retains a better actual observation within the bounded collection window");
        check(AStarLocationAcquisitionPolicy.Select(null, null, better, at.Add(AStarLocationAcquisitionPolicy.MaximumFixAge), null) is null,
            "A* GPS: a retained candidate expires rather than being refreshed at timeout");
        check(AStarLocationAcquisitionPolicy.Select(fresh, currentNetwork, better, at, at) is null &&
            AStarLocationAcquisitionPolicy.Select(better, null, null, at, fresh.Timestamp) is null,
            "A* GPS: duplicate and older acquisition times cannot be returned as new positioning evidence");
        var stationary = fresh with { Timestamp = at.AddMilliseconds(1), AcquisitionElapsedRealtimeNanoseconds = 123456790 };
        check(ReferenceEquals(AStarLocationAcquisitionPolicy.Select(stationary, null, null,
            at.AddMilliseconds(2), fresh.Timestamp), stationary),
            "A* GPS: independent stationary fixes remain usable without inventing movement");
        foreach (double? accuracy in new double?[] { null, 0, -1, double.NaN, double.PositiveInfinity, 31 })
            check(!AStarLocationAcquisitionPolicy.IsEligible(Fix("gps", accuracy), at, null),
                $"A* GPS: invalid or unusable reported precision is rejected ({accuracy})");
        check(!AStarLocationAcquisitionPolicy.IsEligible(fresh with { Coordinate = new(double.NaN, 121) }, at, null) &&
            !AStarLocationAcquisitionPolicy.IsEligible(Fix("gps", 4, -.1), at, null),
            "A* GPS: invalid coordinates and future acquisitions cannot become current fixes");
        check(AStarLocationAcquisitionPolicy.CanComplete(fresh, at, null, TimeSpan.FromMilliseconds(100)) &&
            !AStarLocationAcquisitionPolicy.CanComplete(Fix("gps", 6, 1), at, null, TimeSpan.FromMilliseconds(100)) &&
            !AStarLocationAcquisitionPolicy.CanComplete(Fix("gps", 12), at, null, TimeSpan.FromMilliseconds(100)),
            "A* GPS: early completion requires a newly measured precise GPS fix, not simply a GPS provider label");
        check(!AStarLocationAcquisitionPolicy.CanComplete(currentNetwork, at, null, TimeSpan.FromSeconds(1)) &&
            AStarLocationAcquisitionPolicy.CanComplete(currentNetwork, at, null, TimeSpan.FromSeconds(2)) &&
            !AStarLocationAcquisitionPolicy.CanComplete(null, at, null, TimeSpan.FromSeconds(2)),
            "A* GPS: waits briefly for improved evidence then allows an honest fresh fallback or no fix");
        check(!AStarLocationAcquisitionPolicy.CanComplete(fresh, at, null, TimeSpan.FromMilliseconds(-1)),
            "A* GPS: negative collection time cannot authorize an early result");
        var selected = AStarLocationAcquisitionPolicy.Select(better, currentNetwork, null, at, null);
        check(ReferenceEquals(selected, better) && selected!.Coordinate == better.Coordinate &&
            selected.AccuracyMeters == 4 && selected.Timestamp == better.Timestamp &&
            selected.Provider == "gps" && selected.CourseAccuracyDegrees == 7 &&
            selected.AcquisitionElapsedRealtimeNanoseconds == 123456789 && selected.AltitudeMeters == 12,
            "A* GPS: selection preserves measured coordinates, precision, timestamp, course and native acquisition metadata");

        // Exercise the production dispatcher with a source exposing both paths.
        // MLD must keep the original call and result, without A* filtering.
        var source = new Source { Reading = Fix("network", 25, 2) };
        var mld = await AStarLocationAcquisitionPolicy.GetAsync(source, false, at);
        check(ReferenceEquals(mld, source.Reading) && source.DefaultCalls == 1 && source.AStarCalls == 0,
            "MLD lock: the default request and its original result bypass every A*-specific filter");
        var now = DateTimeOffset.UtcNow;
        var measured = Fix("gps", 5) with { Timestamp = now };
        source = new Source { Reading = measured };
        using var request = new CancellationTokenSource();
        var watermark = now.AddSeconds(-1);
        var actual = await AStarLocationAcquisitionPolicy.GetAsync(source, true, watermark, request.Token);
        check(ReferenceEquals(actual, measured) && source.DefaultCalls == 0 && source.AStarCalls == 1 &&
            source.NewerThan == watermark && source.Token == request.Token,
            "A* GPS: dispatches to the precise source with the exact consumed-fix watermark and cancellation token");
        check(await AStarLocationAcquisitionPolicy.GetAsync(source, true, measured.Timestamp) is null,
            "A* GPS: an adapter returning the consumed fix cannot bypass duplicate rejection");

        var legacy = new LegacySource { Reading = measured };
        check(ReferenceEquals(await AStarLocationAcquisitionPolicy.GetAsync(legacy, true, watermark), measured) &&
            legacy.Calls == 1,
            "A* GPS: providers without the optional native capability retain a validated single-request fallback");
        legacy.Reading = measured with { Timestamp = DateTimeOffset.UtcNow.AddSeconds(-3) };
        check(await AStarLocationAcquisitionPolicy.GetAsync(legacy, true, watermark) is null,
            "A* GPS: the legacy fallback cannot revive stale cached coordinates");

        using var before = new CancellationTokenSource();
        before.Cancel();
        source = new Source { Reading = measured };
        bool cancelled = false;
        try { await AStarLocationAcquisitionPolicy.GetAsync(source, true, null, before.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        check(cancelled && source.AStarCalls == 0 && source.DefaultCalls == 0,
            "A* GPS: a cancelled trip starts no acquisition work");
        using var after = new CancellationTokenSource();
        source = new Source { Reading = measured, BeforeResult = after.Cancel };
        cancelled = false;
        try { await AStarLocationAcquisitionPolicy.GetAsync(source, true, null, after.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        check(cancelled && source.AStarCalls == 1,
            "A* GPS: cancellation discards a late provider result before it can reach progress or arrival");
    }

    private sealed class Source : ILocationService, IAStarLocationSource
    {
        public LocationReading? Reading { get; init; }
        public Action? BeforeResult { get; init; }
        public int DefaultCalls { get; private set; }
        public int AStarCalls { get; private set; }
        public DateTimeOffset? NewerThan { get; private set; }
        public CancellationToken Token { get; private set; }
        public bool IsLocationServiceEnabled => true;
        public Task<bool> EnsurePermissionAsync(CancellationToken token = default) => Task.FromResult(true);
        public Task<LocationReading?> GetLastKnownLocationAsync(CancellationToken token = default) => Task.FromResult(Reading);
        public Task<LocationReading?> GetCurrentLocationAsync(CancellationToken token = default)
        { DefaultCalls++; return Task.FromResult(Reading); }
        public Task<LocationReading?> GetAStarLocationAsync(DateTimeOffset? newerThan, CancellationToken token = default)
        { AStarCalls++; NewerThan = newerThan; Token = token; BeforeResult?.Invoke(); return Task.FromResult(Reading); }
    }

    private sealed class LegacySource : ILocationService
    {
        public LocationReading? Reading { get; set; }
        public int Calls { get; private set; }
        public bool IsLocationServiceEnabled => true;
        public Task<bool> EnsurePermissionAsync(CancellationToken token = default) => Task.FromResult(true);
        public Task<LocationReading?> GetLastKnownLocationAsync(CancellationToken token = default) => Task.FromResult(Reading);
        public Task<LocationReading?> GetCurrentLocationAsync(CancellationToken token = default)
        { Calls++; return Task.FromResult(Reading); }
    }
}
