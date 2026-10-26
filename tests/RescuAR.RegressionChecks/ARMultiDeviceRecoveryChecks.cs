using System.Numerics;
using RescuAR.AR;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;

internal static class ARMultiDeviceRecoveryChecks
{
    private static GeoCoordinate C(double east) => new(0, east / 111195.0);

    public static void Run(Action<bool, string> check)
    {
        foreach (string provider in new[] { "AStar", "MLD (Railway OSRM)" })
        foreach (int interval in new[] { 2, 5, 8 })
        {
            var route = new RouteResult(new[] { new RoutePoint(C(0), 0), new RoutePoint(C(100), 100) }, 100, provider);
            var tracker = new RouteProgressTracker();
            tracker.SetRoute(route);
            var policy = new RouteDisplayProgressPolicy();
            var at = DateTimeOffset.UtcNow;
            RouteBacktrackingPolicy.TrackedMotion Motion(long epoch) => new(0, 0, 0, 1, 1, epoch, 0);
            void Observe(int second, long epoch)
            {
                var update = tracker.Update(C(0), 5);
                policy.ObserveGps(route, update, at.AddSeconds(second));
                policy.ObserveRegistration(route, update, at.AddSeconds(second), at.AddSeconds(second), Motion(epoch));
            }
            Observe(0, 1);
            Observe(10, 2);
            Observe(10 + interval, 2);
            check(!policy.TryGetRegisteredReference(route, C(0), at.AddSeconds(10 + interval), Motion(2), out _),
                $"{provider}/{interval}s: two fixes cannot requalify a changed tracking frame");
            Observe(10 + interval * 2, 2);
            check(policy.LastRegistrationDecision.State == "FRAME_REQUALIFIED" &&
                policy.TryGetRegisteredReference(route, C(0), at.AddSeconds(10 + interval * 2), Motion(2), out _),
                $"{provider}/{interval}s: three fresh coherent fixes recover within the configured lifetime");
            Observe(50, 3);
            Observe(81, 3);
            check(policy.LastRegistrationDecision is { State: "FRAME_REQUALIFICATION_PENDING", EvidenceCount: 1 } &&
                !policy.TryGetRegisteredReference(route, C(0), at.AddSeconds(81), Motion(3), out _),
                $"{provider}/{interval}s: evidence older than the recovery lifetime cannot qualify another frame");
            Observe(81, 3);
            check(policy.LastRegistrationDecision.EvidenceCount == 1,
                $"{provider}/{interval}s: duplicate acquisition times cannot add frame recovery evidence");
        }

        var now = DateTimeOffset.UtcNow;
        var generation = new ARRenderGenerationToken(1, 1, 1);
        var owner = new ARRoadApproachBridge.Ownership(1, "mapped-route", 1, 2, 0, "MAPPED_TARGET");
        var access = new ARRoadApproachBridge.Snapshot(true, Vector2.UnitX, C(0), 5, now,
            generation, 1, ARGroundTrust.Verified, 1000);
        bool Render(ARRoadApproachBridge.Snapshot snapshot) => ARRoadApproachBridge.CanRender(snapshot,
            generation, 1, ARGroundTrust.Verified, true, now, 1000);
        check(Render(access), "an eligible initial-access estimate retains its separate display contract");
        var mapped = access with { Owner = owner };
        check(!Render(mapped), "a mapped arrow cannot silently fall back to an unregistered GPS direction");
        mapped = mapped with { WorldTarget = new Vector2(10, 0) };
        check(Render(mapped) && ARRoadApproachBridge.TryResolveDirection(mapped, 5, 5, out var direction) &&
            Vector2.Distance(direction, Vector2.Normalize(new Vector2(5, -5))) < .0001f,
            "a mapped arrow uses the current camera position and the ribbon's retained world target");
        check(!ARRoadApproachBridge.TryResolveDirection(mapped, 10, 0, out _) &&
            !Render(mapped with { WorldTarget = new Vector2(float.NaN, 0) }),
            "a reached or invalid mapped target cannot produce a guessed forward arrow");
        check(!ARRoadApproachBridge.CanRender(mapped, generation, 2, ARGroundTrust.Verified, true, now, 1000) &&
            !ARRoadApproachBridge.CanRender(mapped, generation, 1, ARGroundTrust.Verified, true, now, 4000),
            "mapped target support still expires and cannot cross a ground-reference handoff");
    }
}
