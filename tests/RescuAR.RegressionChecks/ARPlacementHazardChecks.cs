using RescuAR.AR;
using RescuAR.Navigation.Hazards;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Routing;

internal static class ARPlacementHazardChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(!LocalArNavigationPolicy.TryRenewProvisionalReference(14.99f, 1.35f, 0,
            0, 0, 0, out _) &&
            LocalArNavigationPolicy.TryRenewProvisionalReference(15, 1.35f, 0,
                0, 0, 0, out var renewed) && renewed == (15f, 0f, 0f),
            "estimated ground renews at the existing 15 m boundary, preserving floor height");
        check(!LocalArNavigationPolicy.TryRenewProvisionalReference(15, 1.35f, 0,
            15, 0, 0, out _), "a completed estimate renewal does not repeat while stationary");
        check(!LocalArNavigationPolicy.TryRenewProvisionalReference(30, 3, 0, 0, 0, 0, out _) &&
            !LocalArNavigationPolicy.TryRenewProvisionalReference(float.NaN, 1.35f, 0, 0, 0, 0, out _),
            "estimate renewal cannot manufacture a floor from implausible height or invalid pose");
        float referenceX = 0;
        int renewals = 0;
        for (int x = 0; x <= 100; x++)
            if (LocalArNavigationPolicy.TryRenewProvisionalReference(x, 1.1f, 0,
                referenceX, -0.25f, 0, out var next))
            {
                referenceX = next.X;
                renewals++;
                check(next.Y == -0.25f, "long provisional-floor walks retain one vertical flood baseline");
            }
        check(renewals == 6 && LocalArNavigationPolicy.IsGroundReferenceLocal(100, 0, referenceX, 0),
            "a 100 m unverified-floor walk stays local through six bounded handoffs");
        check(LocalArNavigationPolicy.IsGroundReferenceLocal(20, 0, 0, 0) &&
            !LocalArNavigationPolicy.IsGroundReferenceLocal(20.01f, 0, 0, 0) &&
            !LocalArNavigationPolicy.IsGroundReferenceLocal(85.81f, 0, 0, 0),
            "the field-test 85.81 m reference is ineligible without widening the 20 m publication limit");

        GeoCoordinate C(double x, double y = 0) => new(y / 111_195, x / 111_195);
        RouteResult Route(string algorithm, params GeoCoordinate[] coordinates)
        {
            var points = new List<RoutePoint>();
            double length = 0;
            foreach (var coordinate in coordinates)
            {
                if (points.Count > 0) length += points[^1].Coordinate.DistanceTo(coordinate);
                points.Add(new(coordinate, length));
            }
            return new(points, length, algorithm);
        }
        var now = DateTimeOffset.UtcNow;
        RouteHazard H(string id, double x, double radius) =>
            new(id, C(x), radius, "Flood Warning", "Medium", "Fixture", "fixture", now);
        var near = H("near", 20, 10);
        var far = H("far", 120, 10);
        var originHazard = H("origin", 0, 40);
        foreach (string algorithm in new[] { "AStar", "MLD (Railway OSRM)" })
        {
            var straight = Route(algorithm, C(0), C(200));
            check(RouteHazardGeometry.FindFirstIntersection(straight, 0, new[] { near }).IsAffected &&
                !RouteHazardGeometry.FindFirstIntersection(straight, 50, new[] { near }).IsAffected,
                $"{algorithm}: a passed hazard on a long current segment no longer remains ahead");
            var ahead = RouteHazardGeometry.FindFirstIntersection(straight, 50, new[] { near, far });
            check(Math.Abs(RouteHazardGeometry.FirstEntryProgress(straight, 0, near) - 10) < .1 &&
                Math.Abs(RouteHazardGeometry.FirstEntryProgress(straight, 50, far) - 110) < .1,
                $"{algorithm}: safe-prefix clipping uses circle entry rather than the twenty-metre closest point");
            check(ahead.Hazard == far && Math.Abs(ahead.DistanceAheadMeters - 70) < 0.1,
                $"{algorithm}: clipping removes the passed hazard and retains the next exclusion");
            check(RouteHazardGeometry.FindFirstIntersection(straight, 20, new[] { near }).IsAffected,
                $"{algorithm}: an exclusion at the current position is still detected");

            var monitor = new RouteHazardMonitor();
            monitor.AcceptRoute(straight, C(0));
            check(monitor.Assess(straight, 0, new[] { originHazard }, true).Unsafe.IsAffected,
                $"{algorithm}: normal route matching inside a hazard grants no escape exemption");
            monitor.AcceptRoute(straight, C(0), new[] { originHazard });
            var escape = monitor.Assess(straight, 0, new[] { originHazard }, true);
            check(double.IsPositiveInfinity(escape.SafePrefixEndProgressMeters),
                $"{algorithm}: a validated outward escape is not clipped as an unqualified new hazard");
            check(!escape.Unsafe.IsAffected && escape.Escaping == originHazard &&
                !monitor.Assess(straight, 20, new[] { originHazard }, true).Unsafe.IsAffected,
                $"{algorithm}: a validated outward escape is not repeatedly rejected by monitoring");
            check(monitor.Assess(straight, 60, new[] { originHazard }, true).Escaping is null,
                $"{algorithm}: the escape notice clears once remaining progress leaves the exclusion");
            var tooShort = Route(algorithm, C(0), C(10));
            monitor.AcceptRoute(tooShort, C(0), new[] { originHazard });
            check(monitor.Assess(tooShort, 0, new[] { originHazard }, true).Unsafe.IsAffected,
                $"{algorithm}: an outward path ending inside a hazard is not a safe evacuation escape");
            monitor.AcceptRoute(straight, C(0), new[] { originHazard });
            check(monitor.Assess(straight, 0, new[] { H("origin", 10, 60) }, true).Unsafe.IsAffected,
                $"{algorithm}: changed hazard geometry requires fresh safety validation");
            var reentry = Route(algorithm, C(0), C(80), C(10));
            monitor.AcceptRoute(reentry, C(0), new[] { originHazard });
            check(monitor.Assess(reentry, 80, new[] { originHazard }, true).Unsafe.IsAffected,
                $"{algorithm}: a route that re-enters its starting hazard never acquires a safe escape exemption");
            var blockedDestination = H("destination", 200, 20);
            monitor.AcceptRoute(straight, C(0), new[] { blockedDestination });
            var failed = monitor.Assess(straight, 0, new[] { blockedDestination }, true);
            check(failed.Unsafe.IsAffected && monitor.CanRetry(failed.Retry),
                $"{algorithm}: a hazardous destination remains unsafe and initially permits replacement");
            monitor.MarkNoRoute(failed.Retry);
            check(!monitor.CanRetry(monitor.Assess(straight, 10, new[] { blockedDestination }, true).Retry),
                $"{algorithm}: unchanged no-route assessment is retained while walking the same segment");
            var refreshed = new RouteHazard(blockedDestination.Id, blockedDestination.Coordinate,
                blockedDestination.RadiusMeters, blockedDestination.Category, blockedDestination.Severity,
                blockedDestination.Title, blockedDestination.Source, now.AddSeconds(8));
            check(!monitor.CanRetry(monitor.Assess(straight, 10, new[] { refreshed }, true).Retry),
                $"{algorithm}: a remote refresh timestamp alone cannot restart failed routing");
            monitor.AllowRetryForPositionChange(straight);
            check(monitor.CanRetry(failed.Retry),
                $"{algorithm}: confirmed reverse or off-route walking permits a fresh safety calculation");
            monitor.MarkNoRoute(failed.Retry);
            var offline = monitor.Assess(straight, 10, new[] { blockedDestination }, false);
            check(monitor.CanRetry(offline.Retry), $"{algorithm}: connectivity change permits reconsideration");
            monitor.MarkNoRoute(failed.Retry);
            check(monitor.CanRetry(offline.Retry),
                $"{algorithm}: a stale online failure cannot inhibit the newer offline assessment");
            monitor.MarkNoRoute(offline.Retry);
            var changed = monitor.Assess(straight, 10, new[] { H("destination", 200, 30) }, false);
            check(monitor.CanRetry(changed.Retry), $"{algorithm}: changed exclusions permit reconsideration");
            check(!monitor.IsCurrentContext(offline.Retry),
                $"{algorithm}: an older hazard assessment cannot restore a stale warning");
            monitor.MarkNoRoute(changed.Retry);
            var nextTrip = Route(algorithm, C(0), C(200));
            monitor.AcceptRoute(nextTrip, C(0));
            monitor.MarkNoRoute(changed.Retry);
            check(monitor.CanRetry(monitor.Assess(nextTrip, 0, new[] { blockedDestination }, true).Retry),
                $"{algorithm}: a stale trip completion cannot suppress a new trip to the same destination");
            var withJunction = Route(algorithm, C(0), C(100), C(200));
            monitor.AcceptRoute(withJunction, C(0));
            var beforeJunction = monitor.Assess(withJunction, 10, new[] { blockedDestination }, true);
            monitor.MarkNoRoute(beforeJunction.Retry);
            var afterJunction = monitor.Assess(withJunction, 110, new[] { blockedDestination }, true);
            check(monitor.CanRetry(afterJunction.Retry) && !monitor.IsCurrentContext(beforeJunction.Retry),
                $"{algorithm}: entering another mapped segment permits reconsideration without reviving the earlier notice");
        }

        var previousGeneration = ARRenderGenerationBridge.Current;
        bool accepting = ARRenderGenerationBridge.IsAcceptingFrames;
        var generation = new ARRenderGenerationToken(1200, 1201, 1202);
        ARRenderGenerationBridge.Activate(generation);
        try
        {
            var ground = new ARGroundStateBridge.GroundStateSnapshot(1, generation, 10,
                ARGroundLifecycleState.Provisional, ARGroundTrust.Provisional, Environment.TickCount64, "fixture");
            var frame = new ARCameraPoseBridge.SpatialSnapshot(1,
                new(generation, 1, new(1202, 0, 1080, 1798)), Environment.TickCount64, true, "NONE",
                new(true, 12, 1.35f, 0, 0, 0, 0, 1, 1), default, new(ground, 10, 0, 0));
            var placement = new ARRoutePlacement(generation, 10, 1, Environment.TickCount64,
                10, 0, 12, 0, now, "GPS", 0);
            check(placement.IsFor(frame) &&
                !(placement with { GroundReference = 9 }).IsFor(frame) &&
                !(placement with { Generation = generation with { SessionGeneration = 1199 } }).IsFor(frame),
                "geometry publication is bound to its captured floor reference and AR session");
            foreach (string algorithm in new[] { "AStar", "MLD (Railway OSRM)" })
            {
                var route = Route(algorithm, C(0), C(100));
                var integration = new MLDARIntegrationService(new UnusedRouter());
                check(integration.PublishProgressWindow(route, 0, C(0), 0, 2, 0,
                    userCoordinate: C(0, 3), placement: placement),
                    $"{algorithm}: captured spatial inputs accompany a mapped route publication");
                var published = ARRouteBridge.Current;
                check(published.Placement.IsAvailable && published.Placement.GroundReference == 10 &&
                    published.Placement.Source == "GPS" && Math.Abs(published.Placement.GpsToRoadNorth + 3) < 0.01 &&
                    Math.Abs(published.Placement.GpsToRoadZ - 3) < 0.01 &&
                    Math.Abs(published.Points[0].X + published.Placement.AnchorX - 12) < 0.01,
                    $"{algorithm}: signed GPS offset and publication origin preserve the physical placement equation");
                ARRouteBridge.Publish(published.Points, algorithm, route.TotalDistanceMeters,
                    published.NavigationState.VisualKind, 0, 0, published.Placement with { GroundReference = 11 });
                check(ARRouteBridge.Current.Version > published.Version,
                    $"{algorithm}: equivalent local points cannot resurrect an earlier floor reference");
            }
            check(Route("AStar", C(0), C(100)).GeometryFingerprint ==
                Route("MLD (Railway OSRM)", C(0), C(100)).GeometryFingerprint &&
                Route("AStar", C(0), C(101)).GeometryFingerprint != Route("AStar", C(0), C(100)).GeometryFingerprint,
                "geometry fingerprints compare source geometry independently of routing provider");
        }
        finally
        {
            ARRouteBridge.Clear();
            if (accepting) ARRenderGenerationBridge.Activate(previousGeneration);
            else ARRenderGenerationBridge.Invalidate();
        }
    }

    private sealed class UnusedRouter : IRoutingService
    {
        public string AlgorithmName => "fixture";
        public Task<RouteResult?> FindRouteAsync(GeoCoordinate origin, GeoCoordinate destination,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("No network in this fixture.");
    }
}
