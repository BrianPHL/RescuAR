using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;

internal static class MldWindowContinuityChecks
{
    private static GeoCoordinate C(double east, double north = 0) => new(
        14.64 + north / 6371008.8 * 180 / Math.PI,
        121.095 + east / (6371008.8 * Math.Cos(14.64 * Math.PI / 180)) * 180 / Math.PI);

    public static void Run(Action<bool, string> check)
    {
        // Actual normalized route/GPS/acquisition-pose values from 18:12:50/53.
        var coords = new[] { C(-.043, .423), C(-93.855, 9.096), C(-110.208, 10.652),
            C(-193.584, 18.325), C(-234.465, 22.106), C(-263.298, 24.774) };
        var metres = new[] { 0.0, 94.212, 110.639, 194.367, 235.423, 264.378 };
        var route = new RouteResult(coords.Select((p, i) => new RoutePoint(p, metres[i])).ToArray(),
            metres[^1], "MLD (Railway OSRM)");
        var tracker = new RouteProgressTracker(); tracker.SetRoute(route);
        var policy = new RouteDisplayProgressPolicy();
        var at = DateTimeOffset.UtcNow.AddMinutes(-1);
        var seedMotion = new RouteBacktrackingPolicy.TrackedMotion(.102f, -.171f, -67.42, 1, 1, 3);
        var first = tracker.Update(C(8.281, -6.153), 9);
        policy.ObserveGps(route, first, at);
        policy.ObserveRegistration(route, first, at, at, seedMotion);
        check(policy.TryGetRegisteredReference(route, coords[0], at, seedMotion, out var seed) &&
            Math.Abs(Math.Sqrt(Math.Pow(seed.WorldX - seedMotion.X, 2) + Math.Pow(seed.WorldZ - seedMotion.Z, 2)) - 10.69) < .15,
            "1811 capture: the initial 10.7m gap is reproduced from the real GPS-to-road offset, not hidden by moving the road to the camera");

        var to = seedMotion with { X = -.411f, Z = -1.601f };
        var raw = tracker.Update(C(-11.038, -1.501), 18.78);
        check(raw.SegmentIndex == 0 && Math.Abs(raw.RawProgressMeters - 10.771) < .03 &&
            raw.RawProgressMeters < metres[1],
            "1811 capture: map matching interpolates the road segment; it does not jump to the next node 94m away");
        var held = tracker.ApplyFusionCorrection(.595, raw, "GPS_PDR_HoldStationary");
        var nextAt = at.AddMilliseconds(3233);
        policy.ObserveGps(route, held, nextAt);
        policy.ObserveRegistration(route, held, nextAt, nextAt, to);
        check(policy.TryGetObservedPosition(route, nextAt, out var oldPosition) && oldPosition.ProgressMeters > 10.7,
            "1811 capture: the preceding raw-GPS display behavior reproduces a window ahead of accepted progress");
        check(policy.TryGetNavigationDisplayPosition(route, nextAt, to,
            GpsPdrFusionPolicy.GpsFusionAction.HoldStationary, out var corrected) && corrected.ProgressMeters < .01,
            "1811 capture: a held 10.77m GPS advance cannot move the visible window ahead of the registered camera");
        policy.TryGetRegisteredReference(route, oldPosition.RoadCoordinate, nextAt, to, out var oldStart);
        policy.TryGetRegisteredReference(route, corrected.RoadCoordinate, nextAt, to, out var newStart);
        double Gap(RouteDisplayProgressPolicy.RegisteredReference reference) => Math.Sqrt(
            Math.Pow(reference.WorldX - to.X, 2) + Math.Pow(reference.WorldZ - to.Z, 2));
        check(Gap(oldStart) - Gap(newStart) > 8 && newStart.RegisteredAt == seed.RegisteredAt,
            "1811 capture: the extra distant-start jump is removed while retaining the original world registration");
        foreach (var action in new[] { GpsPdrFusionPolicy.GpsFusionAction.KeepPdrLead,
            GpsPdrFusionPolicy.GpsFusionAction.WaitForBackwardConfirmation })
            check(policy.TryGetNavigationDisplayPosition(route, nextAt, to, action, out var position) &&
                Math.Abs(position.ProgressMeters - corrected.ProgressMeters) < .01,
                $"held display: {action} does not let uncommitted GPS jitter move the window independently");
        check(policy.TryGetNavigationDisplayPosition(route, nextAt, to,
            GpsPdrFusionPolicy.GpsFusionAction.AcceptForward, out var accepted) && accepted.ProgressMeters < .01,
            "accepted display: GPS progress and tracked geometry share the retained transform instead of separate moving windows");
        check(Math.Abs(tracker.Current.CommittedProgressMeters - .595) < .001,
            "display correction does not create trip progress, a location observation or arrival evidence");
        check(!policy.TryGetNavigationDisplayPosition(route, nextAt, to with { TrackingEpoch = 4 },
            GpsPdrFusionPolicy.GpsFusionAction.HoldStationary, out _) &&
            !policy.TryGetNavigationDisplayPosition(route, at.AddSeconds(12), to,
            GpsPdrFusionPolicy.GpsFusionAction.HoldStationary, out _),
            "held display: a changed epoch or expired registration cannot fall through to the raw GPS jump");

        var point = RouteDisplayProgressPolicy.TryAtDistance(route, raw.RawProgressMeters, out var matched) ? matched.RoadCoordinate : default;
        check(!MappedRoutePreviewPolicy.CanPublish(true, false, RouteMatchConfidence.High, 9,
            point, point, at, at.AddSeconds(5.1), true) &&
            MappedRoutePreviewPolicy.CanRetain(at, at.AddSeconds(5.1), true),
            "1811 capture: a 5.1-second fix cannot republish, but an existing qualified preview survives the polling gap");
        check(!MappedRoutePreviewPolicy.CanRetain(at, at.AddSeconds(5), true) &&
            MappedRoutePreviewPolicy.CanRetain(at, at.AddSeconds(8), true) &&
            !MappedRoutePreviewPolicy.CanRetain(at, at.AddSeconds(8.001), true),
            "preview bridge is limited to the existing five-to-eight-second gap, not a new GPS lifetime");
        check(!MappedRoutePreviewPolicy.CanRetain(at, at.AddSeconds(5.1), false) &&
            !MappedRoutePreviewPolicy.CanRetain(at, at.AddSeconds(-1), true),
            "preview bridge cannot rescue invalid pose/heading/registration or future observations");
    }
}
