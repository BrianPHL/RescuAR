using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;

internal static class FieldCadenceRecoveryChecks
{
    private static GeoCoordinate C(double east) => new(0, east / 111_195);

    public static void Run(Action<bool, string> check)
    {
        var at = DateTimeOffset.Parse("2026-11-02T09:13:13.625Z");
        var fix = new LocationReading(C(0), 8, null, 1.2, 90, at) { Provider = "gps" };
        check(AStarLocationAcquisitionPolicy.CanComplete(fix, at.AddSeconds(2.4), null, TimeSpan.Zero),
            "field cadence: the captured fresh 8m GNSS fix need not age for another two seconds before delivery");
        check(!AStarLocationAcquisitionPolicy.CanComplete(fix, at.AddSeconds(3.1), null, TimeSpan.Zero) &&
            !AStarLocationAcquisitionPolicy.IsEligible(fix, at, at),
            "field cadence: immediate delivery still rejects stale and consumed acquisitions");
        var fused = fix with { Provider = "fused", AccuracyMeters = 5 };
        check(ReferenceEquals(LocationAcquisitionPolicy.Select(fix, null, at, fused), fused) &&
            ReferenceEquals(AStarLocationAcquisitionPolicy.Select(fix, null, null, at, null, fused), fused) &&
            AStarLocationAcquisitionPolicy.CanComplete(fused, at, null, TimeSpan.Zero),
            "field cadence: available native fused fixes use the same quality, freshness and prompt-delivery rules");
        check(ReferenceEquals(AStarLocationAcquisitionPolicy.Select(fix, null, null, at, null,
                fused with { Timestamp = at.AddSeconds(-4) }), fix),
            "field cadence: stale fused observations cannot outrank fresh GNSS");

        foreach (string algorithm in new[] { "AStar", "MLD (Railway OSRM)" })
        {
            var route = new RouteResult([new(C(0), 0), new(C(300), 300)], 300, algorithm);
            var geographic = new NavigationStabilityPolicy();
            var tracker = new RouteProgressTracker(); tracker.SetRoute(route);
            var fusion = new GpsPdrFusionPolicy();
            double committed = 0;
            // Replay the observed GNSS spacing and delivery ages, using a
            // straight walk so physical coordinates need not leave the device.
            double[] times = [0, 18.019, 34.915, 52.927, 69.541, 86.619];
            double[] ages = [.924, 2.417, 2.591, .671, .567, .688];
            for (int i = 0; i < times.Length; i++)
            {
                var acquired = at.AddSeconds(times[i]);
                var now = acquired.AddSeconds(ages[i]);
                check(geographic.ObserveGeographic(route, C(times[i]), 8, acquired, now).Reliable,
                    $"{algorithm}: fresh GNSS sample {i + 1} remains geographic evidence at the captured Samsung cadence");
                var match = tracker.Update(C(times[i]), 8, 90, 1);
                var decision = fusion.EvaluateGps(committed, i > 0, match, acquired);
                check(decision.Confidence >= GpsPdrFusionPolicy.GpsConfidence.Medium &&
                        Math.Abs(decision.TargetProgressMeters - times[i]) < .1,
                    $"{algorithm}: accepted sparse GNSS advances guidance by the walked distance rather than a fixed per-poll cap");
                committed = decision.TargetProgressMeters;
                tracker.ApplyFusionCorrection(committed, match, "FIELD_CADENCE");
            }
            var motion = new RouteBacktrackingPolicy.TrackedMotion(0, 0, 0, 1, 1);
            var registration = new RouteDisplayProgressPolicy(geographic.Options);
            var initial = new RouteProgressTracker(); initial.SetRoute(route);
            var seed = initial.Update(C(0), 8);
            registration.ObserveGps(route, seed, at);
            check(registration.ObserveRegistration(route, seed, at, at, motion).State == "ACQUISITION_SEEDED" &&
                    registration.TryGetRegisteredReference(route, C(0), at, motion, out _),
                $"{algorithm}: b239fa7 registration seeds from one qualified, paired acquisition");

            geographic.Reset();
            geographic.ObserveGeographic(route, C(0), 8, at, at);
            check(geographic.TryEstimate(route, C(0), 0, at, at.AddSeconds(18), motion,
                    motion with { X = 20 }, out var estimate) && Math.Abs(estimate.ProgressMeters - 20) < .1,
                $"{algorithm}: live tracked straight-road geometry can bridge one observed GNSS interval without promoting GPS freshness");
            check(!geographic.TryEstimate(route, C(0), 0, at, at.AddSeconds(31), motion,
                    motion with { X = 20 }, out _) &&
                !geographic.TryEstimate(route, C(0), 0, at, at.AddSeconds(18), motion,
                    motion with { X = 20, TrackingEpoch = 1 }, out _),
                $"{algorithm}: bounded visual continuity cannot cross expiry or a tracking-frame change");

            var arObservation = new RoutePositionObservationPolicy();
            arObservation.Evaluate(route, C(0), 8, at, at, motion);
            var disagreement = arObservation.Evaluate(route, C(5), 8, at.AddSeconds(1), at.AddSeconds(1), motion with { X = 50 });
            check(disagreement.IsHeld && geographic.ObserveGeographic(route, C(5), 8,
                    at.AddSeconds(1), at.AddSeconds(1)).Reliable,
                $"{algorithm}: inconsistent AR displacement suppresses placement without vetoing plausible measured GPS");

            var arrival = new SafeZoneConfirmationService();
            check(!arrival.Evaluate(C(10), C(0), 30, 8, 100, at, evaluatedAt: at).IsConfirmed &&
                arrival.Evaluate(C(8), C(0), 30, 8, 100, at.AddSeconds(17), evaluatedAt: at.AddSeconds(17)).IsConfirmed,
                $"{algorithm}: two distinct fresh vicinity observations confirm arrival at the captured GNSS cadence even if route progress lagged");
            arrival.Reset();
            arrival.Evaluate(C(10), C(0), 30, 8, 0, at, evaluatedAt: at);
            check(!arrival.Evaluate(C(8), C(0), 30, 8, 0, at.AddSeconds(31), evaluatedAt: at.AddSeconds(31)).IsConfirmed,
                $"{algorithm}: an expired arrival candidate cannot confirm from one later fix");

            fusion.Reset();
            fusion.EvaluateGps(0, false, seed with { IsOffRoute = true }, at);
            var first = fusion.EvaluateGps(0, false, seed, at.AddSeconds(1));
            var second = fusion.EvaluateGps(0, false, seed, at.AddSeconds(18));
            check(first.Action == GpsPdrFusionPolicy.GpsFusionAction.AwaitRouteIdentity && !fusion.IsRouteIdentitySuspended &&
                    second.Action != GpsPdrFusionPolicy.GpsFusionAction.AwaitRouteIdentity,
                $"{algorithm}: distinct strong corridor matches can recover identity across a 17-second GNSS gap");
        }
    }
}
