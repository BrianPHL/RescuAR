using RescuAR.AR;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Hazards;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Routing;

internal static class SharedCameraRoutingChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var at = DateTimeOffset.UtcNow;
        check(!RouteStartupLocationPolicy.CanPlan(C(0), null, at, at, false) &&
            !RouteStartupLocationPolicy.CanPlan(C(0), 31, at, at, false) &&
            !RouteStartupLocationPolicy.CanPlan(C(0), 5, at.AddSeconds(-9), at, true),
            "shared camera routing rejects unknown accuracy, weak GPS and stale cached origins");

        var hazard = new RouteHazard("blocked-road", C(40), 8, "Road Hazard", "high", "Blocked", "test", at);
        var unsafeRoute = Route(C(0), C(100));
        var detour = Route(C(20, 20), C(20, 40), C(100, 40), C(100));
        var router = new Router(unsafeRoute, detour);
        var bridge = ARRouteBridge.Current;
        var accepted = await new MLDARIntegrationService(router).RequestRouteWithDeferredRoadApproachAsync(
            C(0), C(100), _ => Task.FromResult<AStarRoutingService.RoadApproachTarget?>(new(C(20, 20), true)),
            hazards: new[] { hazard });
        check(ReferenceEquals(accepted, detour) && router.HazardCalls.Count == 2 && router.PlainCalls == 0 &&
            router.HazardCalls.All(hazards => hazards.Count == 1 && hazards[0] == hazard),
            "direct routing and bounded road-access retry retain the same hazard exclusions");
        check(ReferenceEquals(CameraMapPresentationPolicy.ForTrip(accepted, 41, 41, true), detour) &&
            ARRouteBridge.Current.Version == bridge.Version,
            "2D consumes the accepted route object without publishing AR geometry before alignment");
        check(CameraMapPresentationPolicy.ForTrip(accepted, 41, 42, true) is null &&
            CameraMapPresentationPolicy.ForTrip(accepted, 41, 41, false) is null,
            "a destination change or ended trip cannot display an earlier route");

        router = new Router(unsafeRoute, unsafeRoute);
        accepted = await new MLDARIntegrationService(router).RequestRouteWithDeferredRoadApproachAsync(
            C(0), C(100), _ => Task.FromResult<AStarRoutingService.RoadApproachTarget?>(new(C(20), true)),
            hazards: new[] { hazard });
        check(accepted is null && router.PlainCalls == 0,
            "an unsafe provider result cannot be accepted through an unconstrained fallback");

        var accessHazard = new RouteHazard("blocked-access", C(10, 10), 5, "Road Hazard", "high", "Blocked", "test", at);
        router = new Router(null, detour);
        accepted = await new MLDARIntegrationService(router).RequestRouteWithDeferredRoadApproachAsync(
            C(0), C(100), _ => Task.FromResult<AStarRoutingService.RoadApproachTarget?>(new(C(20, 20), true)),
            hazards: new[] { accessHazard });
        check(accepted is null && router.HazardCalls.Count == 1,
            "a road-access connector crossing a hazard is rejected before the retry");

        router = new Router(detour);
        accepted = await new MLDARIntegrationService(router).RequestRouteWithDeferredRoadApproachAsync(
            C(0), C(100), _ => Task.FromResult<AStarRoutingService.RoadApproachTarget?>(null),
            hazards: new[] { new RouteHazard("blocked-shelter", C(100), 8, "Road Hazard", "high", "Blocked", "test", at) });
        check(accepted is null && router.HazardCalls.Count == 0,
            "a shelter inside a known exclusion cannot start a route request");

        using var cancellation = new CancellationTokenSource();
        router = new Router(detour) { BeforeResult = () => cancellation.Cancel() };
        bool cancelled = false;
        try
        {
            await new MLDARIntegrationService(router).RequestRouteWithDeferredRoadApproachAsync(
                C(0), C(100), _ => Task.FromResult<AStarRoutingService.RoadApproachTarget?>(null),
                cancellation.Token, new[] { hazard });
        }
        catch (OperationCanceledException) { cancelled = true; }
        check(cancelled && ARRouteBridge.Current.Version == bridge.Version,
            "cancellation after a provider completes prevents its late result from reaching either view");

        var arrival = new SafeZoneConfirmationService();
        var first = arrival.Evaluate(C(15), C(0), 30, 5, 0, at, evaluatedAt: at, destinationVersion: 41);
        var repeated = arrival.Evaluate(C(15), C(0), 30, 5, 0, at.AddSeconds(3), evaluatedAt: at.AddSeconds(3), destinationVersion: 41);
        check(!first.IsConfirmed && repeated.IsConfirmed,
            "a 15m sample starts confirmation; repeated accurate fixes establish vicinity in the shared service");
        arrival.Reset();
        check(!arrival.Evaluate(C(10), C(0), 30, 5, 0, at, evaluatedAt: at.AddSeconds(6), destinationVersion: 41).IsCandidate &&
            !arrival.Evaluate(C(10), C(0), 30, 5, 0, at, majorRoadBarrier: true, evaluatedAt: at, destinationVersion: 41).IsCandidate,
            "stale GPS and a mapped road barrier cannot produce an arrival candidate");
    }

    private static GeoCoordinate C(double east, double north = 0) =>
        new(14.7 + north / 111_195, 121.08 + east / (111_195 * Math.Cos(14.7 * Math.PI / 180)));

    private static RouteResult Route(params GeoCoordinate[] coordinates)
    {
        double distance = 0;
        var points = coordinates.Select((coordinate, index) =>
        {
            if (index > 0) distance += coordinates[index - 1].DistanceTo(coordinate);
            return new RoutePoint(coordinate, distance);
        }).ToArray();
        return new(points, distance, "unit-provider");
    }

    private sealed class Router(params RouteResult?[] responses) : IHazardAwareRoutingService
    {
        public string AlgorithmName => "unit-provider";
        public int PlainCalls { get; private set; }
        public Action? BeforeResult { get; init; }
        public List<IReadOnlyList<RouteHazard>> HazardCalls { get; } = new();
        public Task<RouteResult?> FindRouteAsync(GeoCoordinate origin, GeoCoordinate destination, CancellationToken token = default)
        {
            PlainCalls++;
            throw new InvalidOperationException("Hazard exclusions were omitted.");
        }
        public Task<RouteResult?> FindRouteAvoidingHazardsAsync(GeoCoordinate origin, GeoCoordinate destination,
            IReadOnlyList<RouteHazard> hazards, CancellationToken token = default)
        {
            HazardCalls.Add(hazards);
            BeforeResult?.Invoke();
            return Task.FromResult(responses[Math.Min(HazardCalls.Count - 1, responses.Length - 1)]);
        }
    }
}
