using RescuAR.AR;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Routing;

internal static class ARRegistrationChecks
{
    private static GeoCoordinate C(double east, double north = 0) => new(
        14.64 + north / 6371008.8 * 180 / Math.PI,
        121.095 + east / (6371008.8 * Math.Cos(14.64 * Math.PI / 180)) * 180 / Math.PI);
    private static RouteBacktrackingPolicy.TrackedMotion Motion(float x, float z, double yaw = -77.75) =>
        new(x, z, yaw, 1, 82, 7, 0);
    private static RouteResult Road(string provider, bool field = false)
    {
        var coordinates = field ? new[] { C(-182.659, 22.597), C(-105.393, 15.444), C(-89.019, 13.932), C(-4.071, 6.079) }
            : new[] { C(0), C(120), C(120, 40) };
        double d = 0;
        var points = coordinates.Select((c, i) => { if (i > 0) d += coordinates[i - 1].DistanceTo(c); return new RoutePoint(c, d); }).ToArray();
        return new(points, d, provider);
    }
    private static RouteProgressTracker.RouteProgressUpdate Fix(RouteResult route, GeoCoordinate coordinate, double accuracy = 8)
    {
        var tracker = new RouteProgressTracker(); tracker.SetRoute(route);
        return tracker.Update(coordinate, accuracy);
    }
    private static void Observe(RouteDisplayProgressPolicy policy, RouteResult route,
        RouteProgressTracker.RouteProgressUpdate fix, DateTimeOffset at, RouteBacktrackingPolicy.TrackedMotion motion)
    {
        policy.ObserveGps(route, fix, at);
        policy.ObserveRegistration(route, fix, at, at.AddMilliseconds(600), motion);
    }

    public static void Run(Action<bool, string> check)
    {
        var previous = ARRenderGenerationBridge.Current;
        bool accepting = ARRenderGenerationBridge.IsAcceptingFrames;
        ARRenderGenerationBridge.Activate(new(1, 1, 1));
        try
        {
            foreach (string provider in new[] { "AStar", "MLD (Railway OSRM)" })
            {
                FieldPair(check, provider);
                LatestFieldPlacement(check, provider);
                FrequentGpsCorrection(check, provider);
                October30MapGeometry(check, provider);
                CorrectionAndOwnership(check, provider);
                Arrival(check, provider);
            }
        }
        finally
        {
            ARRouteBridge.Clear();
            if (accepting) ARRenderGenerationBridge.Activate(previous); else ARRenderGenerationBridge.Invalidate();
        }
    }

    private static void October30MapGeometry(Action<bool, string> check, string provider)
    {
        // Local EN observations from the supplied capture, not surveyed road
        // coordinates. Check the real publisher against the measured placement.
        bool mld = provider.StartsWith("MLD");
        var east = mld ? new[] { .108, -89.401, -105.754, -189.130, -230.012, -258.844 }
            : new[] { .190, -78.460, -94.834, -178.200, -219.124, -247.872 };
        var north = mld ? new[] { 1.323, 9.552, 11.108, 18.781, 22.561, 25.230 }
            : new[] { 2.059, 9.329, 10.842, 18.558, 22.339, 24.994 };
        var distance = mld ? new[] { 0.0, 89.886, 106.313, 190.041, 231.097, 260.052 }
            : new[] { 0.0, 78.986, 95.429, 179.151, 220.250, 249.120 };
        var route = new RouteResult(east.Select((e, i) => new RoutePoint(C(e, north[i]), distance[i])).ToArray(),
            distance[^1], provider);
        var originalPoints = route.Points.ToArray();
        var gps = Fix(route, mld ? C(2.085, -3.600) : C(.602, -2.591), mld ? 10.5 : 11.51);
        var pose = mld ? Motion(-3.925f, .534f, -.956) : Motion(.126f, .336f, -56.581);
        var at = DateTimeOffset.UtcNow;
        var policy = new RouteDisplayProgressPolicy();
        Observe(policy, route, gps, at, pose);
        check(policy.TryGetRegisteredReference(route, route.Points[0].Coordinate, at, pose, out var reference) &&
            Math.Abs(reference.WorldX - (mld ? -5.820 : 3.780)) < .015 &&
            Math.Abs(reference.WorldZ - (mld ? -4.421 : -2.570)) < .015,
            $"{provider}: October 30 logged placement is reproduced from the map route and acquisition pose");
        var publisher = new MLDARIntegrationService(new UnusedRouter());
        foreach (var anchor in new[] { (X: -4f, Z: -1f, Ground: 82L), (X: 2f, Z: 3f, Ground: 83L) })
        {
            var placement = new ARRoutePlacement(new(1, 1, 1), anchor.Ground, 1, Environment.TickCount64,
                anchor.X, anchor.Z, pose.X, pose.Z, at, "GPS", pose.YawDegrees,
                TrackingEpoch: 7, WorldFrame: 0);
            check(publisher.PublishProgressWindow(route, 0, route.Points[0].Coordinate, pose.YawDegrees,
                pose.X - anchor.X, pose.Z - anchor.Z, arWindowMeters: 40, userCoordinate: gps.GpsCoordinate,
                placement: placement, registeredReference: reference),
                $"{provider}: the unchanged map route publishes through the shared AR pipeline");
            var drawn = ARRouteBridge.Current;
            var start = drawn.Points[0]; var end = drawn.Points[^1];
            double dx = end.X - start.X, dz = end.Z - start.Z;
            double length = Math.Sqrt(dx * dx + dz * dz);
            // Geographic compass bearings increase clockwise; AR azimuth here
            // uses atan2(+X,+Z). A westbound street is about 275.25 degrees.
            double geographicBearing = Math.Atan2(east[1] - east[0], north[1] - north[0]) * 180 / Math.PI;
            double expectedAzimuth = 180 + pose.YawDegrees - geographicBearing;
            double actualAzimuth = Math.Atan2(dx, dz) * 180 / Math.PI;
            check(Math.Abs(start.X + anchor.X - reference.WorldX) < .01 &&
                Math.Abs(start.Z + anchor.Z - reference.WorldZ) < .01 && Math.Abs(length - 40) < .02 &&
                Math.Abs(Math.IEEERemainder(actualAzimuth - expectedAzimuth, 360)) < .02 &&
                route.Points.SequenceEqual(originalPoints),
                $"{provider}: nonzero ground origins preserve the mapped bearing and metre scale without a double offset");
        }
    }

    private static void FrequentGpsCorrection(Action<bool, string> check, string provider)
    {
        var route = Road(provider);
        var points = route.Points.ToArray();
        var at = DateTimeOffset.UtcNow;
        var policy = new RouteDisplayProgressPolicy();
        var pose = Motion(1, -2, -56.581);
        Observe(policy, route, Fix(route, C(0, -8), 12), at, pose);
        policy.TryGetRegisteredReference(route, C(0), at, pose, out var before);
        for (int seconds = 2; seconds <= 10; seconds += 2)
            Observe(policy, route, Fix(route, C(0, -2), 7), at.AddSeconds(seconds), pose);
        check(policy.LastRegistrationDecision is { State: "GPS_FILTERED_BOUNDED_CORRECTION", Changed: true } &&
            policy.TryGetRegisteredReference(route, C(0), at.AddSeconds(10), pose, out var after) &&
            Math.Abs(Math.Sqrt(Math.Pow(after.WorldX - before.WorldX, 2) +
                Math.Pow(after.WorldZ - before.WorldZ, 2)) - 2) < .01 &&
            policy.RegistrationAccuracyMeters == 12 && after.YawDegrees == before.YawDegrees &&
            route.Points.SequenceEqual(points),
            $"{provider}: frequent improved GPS corrects retained AR translation without changing map geometry, heading or reported precision");
    }

    private static void FieldPair(Action<bool, string> check, string provider)
    {
        var route = Road(provider, true);
        var at = DateTimeOffset.UtcNow.AddSeconds(-4);
        var first = Fix(route, C(-61.995, 3.309), 7.5);
        var next = Fix(route, C(-54.619, 9.306));
        var policy = new RouteDisplayProgressPolicy();
        var acquisition = Motion(-25.443f, -69.910f);
        Observe(policy, route, first, at, acquisition);
        var target = route.Points[2].Coordinate;
        check(policy.TryGetRegisteredReference(route, target, at.AddMilliseconds(600), Motion(-25.482f, -69.303f), out var before),
            $"{provider}: captured acquisition-time reference is available independently of receipt pose");
        var expected = MapToArCoordinates.Rotate((target.Longitude - first.GpsCoordinate.Longitude) * Math.PI / 180 *
            6371008.8 * Math.Cos(first.GpsCoordinate.Latitude * Math.PI / 180),
            (target.Latitude - first.GpsCoordinate.Latitude) * Math.PI / 180 * 6371008.8, -77.75);
        check(Math.Abs(before.WorldX - acquisition.X - expected.X) < .01 &&
            Math.Abs(before.WorldZ - acquisition.Z - expected.Z) < .01,
            $"{provider}: delayed GPS is registered at acquisition rather than half a metre ahead at receipt");
        Observe(policy, route, next, at.AddSeconds(3.741), Motion(-26.063f, -65.893f));
        var current = Motion(-26.065f, -65.263f);
        check(policy.LastRegistrationDecision is { Changed: false, State: "CORRECTION_AWAITING_EVIDENCE" } &&
            policy.TryGetRegisteredReference(route, target, at.AddSeconds(4), current, out var after) &&
            Math.Abs(after.WorldX - before.WorldX) < .01 && Math.Abs(after.WorldZ - before.WorldZ) < .01,
            $"{provider}: the field-test lateral innovation cannot instantly move the registered road");
        check(policy.TryResolvePdr(route, next.CommittedProgressMeters + .7, at.AddSeconds(4.5), out var step),
            $"{provider}: a held registration correction does not block bounded mapped progress");
        var sideways = current with { X = current.X + 2 };
        check(policy.TryGetRegisteredReference(route, target, at.AddSeconds(4.5), sideways, out after) &&
            Math.Abs(after.WorldX - before.WorldX) < .01 && Math.Abs(after.WorldZ - before.WorldZ) < .01,
            $"{provider}: lateral walking changes the camera relationship while the world road stays fixed");

        var publisher = new MLDARIntegrationService(new UnusedRouter());
        bool Publish(float anchorX, float anchorZ, long floor)
        {
            if (!policy.TryGetRegisteredReference(route, step.RoadCoordinate, at.AddSeconds(4.5),
                    sideways with { GroundReference = floor }, out var reference)) return false;
            var placement = new ARRoutePlacement(new(1, 1, 1), floor, 1, Environment.TickCount64,
                anchorX, anchorZ, sideways.X, sideways.Z, at.AddSeconds(3.741), "PDR", -77.75,
                TrackingEpoch: 7, WorldFrame: 0);
            return publisher.PublishProgressWindow(route, step.ProgressMeters, step.RoadCoordinate, -77.75,
                sideways.X - anchorX, sideways.Z - anchorZ, arWindowMeters: 40,
                userCoordinate: step.AlignmentCoordinate, placement: placement, registeredReference: reference);
        }
        check(Publish(-25.04f, -68.12f, 82), $"{provider}: real publisher accepts the retained world registration");
        var old = ARRouteBridge.Current;
        float worldX = old.Points[0].X + old.Placement.AnchorX, worldZ = old.Points[0].Z + old.Placement.AnchorZ;
        check(Publish(-27, -64, 83) &&
            Math.Abs(ARRouteBridge.Current.Points[0].X + ARRouteBridge.Current.Placement.AnchorX - worldX) < .05 &&
            Math.Abs(ARRouteBridge.Current.Points[0].Z + ARRouteBridge.Current.Placement.AnchorZ - worldZ) < .05 &&
            ARRouteBridge.Current.Placement.AlignmentSource == "QUALIFIED_ACQUISITION",
            $"{provider}: floor renewal changes local coordinates without moving the published world geometry");
        check(!policy.TryGetRegisteredReference(route, target, at.AddSeconds(4.5), sideways with { TrackingEpoch = 8 }, out _) &&
            !policy.TryGetRegisteredReference(route, target, at.AddSeconds(4.5), sideways with { WorldFrame = 1 }, out _) &&
            !policy.TryGetRegisteredReference(route, target, at.AddSeconds(4.5), sideways with { Session = 2 }, out _) &&
            !policy.TryGetRegisteredReference(route, target, at.AddSeconds(4.5), sideways with { YawDegrees = -77 }, out _),
            $"{provider}: frame and calibration changes cannot reuse an earlier registration");

        var longGap = new RouteDisplayProgressPolicy();
        var earlier = Fix(route, C(-154.325, 14.945), 6.5);
        Observe(longGap, route, earlier, at, Motion(-22.192f, -132.728f) with { GroundReference = 68 });
        longGap.TryGetRegisteredReference(route, target, at.AddMilliseconds(600), Motion(-22.32f, -132.15f), out var retained);
        var later = Fix(route, C(-146.868, 18.836), 6.5);
        Observe(longGap, route, later, at.AddSeconds(7.841), Motion(-23.365f, -124.415f) with { GroundReference = 68 });
        check(!longGap.LastRegistrationDecision.Changed && longGap.TryGetRegisteredReference(route, target,
                at.AddSeconds(8.441), Motion(-23.37f, -123.86f) with { GroundReference = 68 }, out var supported) &&
            Math.Abs(supported.WorldX - retained.WorldX) < .01 && Math.Abs(supported.WorldZ - retained.WorldZ) < .01,
            $"{provider}: the second captured lateral shift near the eight-second boundary retains its supported reference");
    }

    private static void LatestFieldPlacement(Action<bool, string> check, string provider)
    {
        var coordinates = new[] { C(.592, 5.415), C(-89.240, 13.755), C(-105.592, 15.312),
            C(-188.969, 22.984), C(-229.850, 26.765), C(-258.682, 29.433) };
        var distances = new[] { 0.0, 90.218, 106.644, 190.373, 231.429, 260.384 };
        var route = new RouteResult(coordinates.Select((c, i) => new RoutePoint(c, distances[i])).ToArray(),
            260.384, provider);
        var at = DateTimeOffset.UtcNow;
        var policy = new RouteDisplayProgressPolicy();
        var gps = Fix(route, C(5.276, .743), 7.5);
        var pose = new RouteBacktrackingPolicy.TrackedMotion(.089f, -.152f, -90.669, 1, 1, 4);
        Observe(policy, route, gps, at, pose);
        check(policy.TryGetRegisteredReference(route, route.Points[0].Coordinate, at.AddMilliseconds(300), pose, out var placed) &&
            Math.Abs(placed.WorldX - 4.816) < .015 && Math.Abs(placed.WorldZ + 4.781) < .015,
            $"{provider}: the latest captured GPS/pose pair reproduces the 4.7m rightward placement; it is not a physical accuracy pass");
        check(placed.Source == "QUALIFIED_ACQUISITION" && policy.RegistrationAccuracyMeters == 7.5,
            $"{provider}: a 7.5m GPS accuracy radius is not promoted to an observed physical-road registration");
        policy.Reset();
        check(!policy.TryGetRegisteredReference(route, route.Points[0].Coordinate, at, pose, out _),
            $"{provider}: stop/change-destination clears the captured placement before a new route can bind");
    }

    private static void CorrectionAndOwnership(Action<bool, string> check, string provider)
    {
        var route = Road(provider); var policy = new RouteDisplayProgressPolicy();
        var at = DateTimeOffset.UtcNow;
        Observe(policy, route, Fix(route, C(0)), at, Motion(0, 0, 0));
        Observe(policy, route, Fix(route, C(10, 5)), at.AddSeconds(3), Motion(10, 0, 0));
        check(!policy.LastRegistrationDecision.Changed, $"{provider}: a single plausible GPS bias is a candidate only");
        Observe(policy, route, Fix(route, C(14, 5)), at.AddSeconds(6), Motion(14, 0, 0));
        check(!policy.LastRegistrationDecision.Changed &&
            policy.TryGetRegisteredReference(route, C(0), at.AddSeconds(6), Motion(14, 0, 0), out var corrected) &&
            Math.Abs(corrected.WorldZ) < .02,
            $"{provider}: two correlated fixes cannot translate the mapped world registration");
        var snapshot = policy.LastRegistrationDecision;
        policy.ObserveRegistration(route, Fix(route, C(20, 10)), at.AddSeconds(6), at.AddSeconds(6), Motion(20, 0, 0));
        check(policy.LastRegistrationDecision == snapshot, $"{provider}: duplicate callbacks do not apply another correction");
        check(policy.ObserveRegistration(route, Fix(route, C(20)), at.AddSeconds(7), at.AddSeconds(7), null).State ==
            "UNPAIRED_OR_UNQUALIFIED" && !policy.TryGetRegisteredReference(route, C(0), at.AddSeconds(15), Motion(20, 0, 0), out _),
            $"{provider}: an unavailable acquisition pair cannot extend stale registration support");
        check(!policy.TryGetRegisteredReference(Road(provider), C(0), at.AddSeconds(6), Motion(14, 0, 0), out _),
            $"{provider}: route replacement cannot inherit an unrelated geographic registration");

        Observe(policy, route, Fix(route, C(28, 15)), at.AddSeconds(20), Motion(28, 0, 0));
        check(!policy.LastRegistrationDecision.Changed &&
            policy.TryGetRegisteredReference(route, C(0), at.AddSeconds(20), Motion(28, 0, 0), out var expired) &&
            Math.Abs(expired.WorldZ) < .02,
            $"{provider}: expired support is renewed by overlapping uncertainty without reseeding the world");
        Observe(policy, route, Fix(route, C(55)), at.AddSeconds(24), Motion(32, 0, 0));
        check(!policy.LastRegistrationDecision.Changed,
            $"{provider}: non-overlapping single-fix reacquisition cannot reseed a known frame");

        policy.Reset();
        Observe(policy, route, Fix(route, C(0), 8), at, Motion(0, 0, 0));
        for (int i = 0; i < 3; i++)
            Observe(policy, route, Fix(route, C(10 + i * 4, 14), 2), at.AddSeconds(3 + i * 4), Motion(10 + i * 4, 0, 0));
        check(policy.LastRegistrationDecision is { Changed: true, State: "PRECISION_VALIDATED_BOUNDED_CORRECTION" } &&
            policy.TryGetRegisteredReference(route, C(0), at.AddSeconds(11), Motion(18, 0, 0), out var bounded) &&
            Math.Abs(bounded.WorldZ - 2) < .02,
            $"{provider}: three precise fixes across eight metres correct at most two metres, never the full innovation");

        policy.Reset();
        for (int i = 0; i <= 24; i++)
        {
            Observe(policy, route, Fix(route, C(3 * i)), at.AddSeconds(3 * i), Motion(3 * i, 0, 0));
            check(policy.TryGetRegisteredReference(route, C(0), at.AddSeconds(3 * i + .6), Motion(3 * i + .6f, 1, 0), out var reference) &&
                Math.Abs(reference.WorldX) < .01 && Math.Abs(reference.WorldZ) < .01,
                $"{provider}: fresh evidence sustains a fixed registration at {3 * i} metres without extending the GPS outage bound");
        }
        Observe(policy, route, Fix(route, C(69)), at.AddSeconds(75), Motion(69, 0, 0));
        check(policy.TryGetRegisteredReference(route, C(0), at.AddSeconds(75.6), Motion(68.4f, 1, 0), out var reversed) &&
            Math.Abs(reversed.WorldX) < .01 && Math.Abs(reversed.WorldZ) < .01,
            $"{provider}: reverse walking retains the same world registration while navigation progress changes separately");
        policy.Reset();
        check(!policy.TryGetRegisteredReference(route, C(0), at, Motion(0, 0, 0), out _),
            $"{provider}: a reset trip requires a new qualified reference");
    }

    private static void Arrival(Action<bool, string> check, string provider)
    {
        var at = DateTimeOffset.UtcNow; var service = new SafeZoneConfirmationService();
        SafeZoneConfirmationService.SafeZoneDecision Evaluate(double distance, double accuracy, double seconds, long trip = 1) =>
            service.Evaluate(C(distance), C(0), 30, accuracy, 63, at.AddSeconds(seconds), false, at.AddSeconds(seconds), trip);
        check(Evaluate(17.6, 15.3, 0).ConfirmationCount == 1 && !Evaluate(17.6, 30.3, 2.26).IsConfirmed &&
            !Evaluate(94, 6.5, 8).IsCandidate && Evaluate(24.7, 21.6, 22.5).ConfirmationCount == 1 &&
            !Evaluate(24.7, 38.5, 24.8).IsConfirmed,
            $"{provider}: the MLD field sequence never fabricates automatic arrival from weak or conflicting positions");
        check(Evaluate(27.9, 6.5, 30, 2).ConfirmationCount == 1 && Evaluate(23.5, 6.5, 33.723, 2).IsConfirmed,
            $"{provider}: the captured AStar qualifying pair confirms vicinity under the same arrival rules");
        check(!UserArrivalConfirmationPolicy.CanShowNotice(C(17.6), C(0), 30.3, at, at) &&
            !UserArrivalConfirmationPolicy.CanShowNotice(C(94), C(0), 6.5, at, at) &&
            !UserArrivalConfirmationPolicy.CanOffer(C(10), C(0), 6.5, at, at.AddSeconds(11)) &&
            UserArrivalConfirmationPolicy.CanOffer(C(10), C(0), 6.5, at, at.AddSeconds(2)),
            $"{provider}: manual arrival and its notice require a current precise fix in the final 15 metres");
    }

    private sealed class UnusedRouter : IRoutingService
    {
        public string AlgorithmName => "unused fixture";
        public Task<RouteResult?> FindRouteAsync(GeoCoordinate origin, GeoCoordinate destination, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Registration fixtures never make routing requests.");
    }
}
