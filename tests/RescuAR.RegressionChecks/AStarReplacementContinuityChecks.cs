using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;

internal static class AStarReplacementContinuityChecks
{
    // Session-relative metre fixtures from the November 5 capture, with a
    // synthetic geographic origin. No private field origin is reproduced.
    private static GeoCoordinate C(double east, double north = 0) => new(
        north / 6371008.8 * 180 / Math.PI, east / 6371008.8 * 180 / Math.PI);
    private static RouteResult Road(string algorithm, params (double East, double North)[] vertices)
    {
        double distance = 0;
        var points = vertices.Select((p, i) =>
        {
            var coordinate = C(p.East, p.North);
            if (i > 0) distance += C(vertices[i - 1].East, vertices[i - 1].North).DistanceTo(coordinate);
            return new RoutePoint(coordinate, distance);
        }).ToArray();
        return new(points, distance, algorithm);
    }
    private static RouteProgressTracker.RouteProgressUpdate Fix(RouteResult route, GeoCoordinate coordinate, double accuracy)
    {
        var tracker = new RouteProgressTracker();
        tracker.SetRoute(route);
        return tracker.Update(coordinate, accuracy);
    }

    public static void Run(Action<bool, string> check)
    {
        var old = Road("MLD (Railway OSRM, Hazard-Aware)", (.257, 2.682), (-97.320, 11.689),
            (-113.673, 13.245), (-197.049, 20.918), (-237.931, 24.698), (-266.763, 27.367));
        var next = Road("AStar (Hazard-Aware)", (-36.483, 6.079), (-97.288, 11.700),
            (-113.662, 13.212), (-197.028, 20.929), (-237.952, 24.710), (-266.700, 27.364));
        string originalGeometry = old.GeometryFingerprint;
        check(AStarReplacementContinuityPolicy.TryCreate(old, next, out var continuity) && continuity is not null,
            "A* expiry: centimetre-different route geometry is recognized as the same directed corridor");
        // Reproject the common physical point; subtracting the two total route
        // lengths also includes their slightly different snapped endpoints.
        check(continuity!.TryMapProgress(68.825, out double progress) && Math.Abs(progress - 31.9283) < .01 &&
            continuity.TryMapProgress(36.897, out double raw) && raw < .1,
            "A* expiry: accepted 68.8 m rebases near 31.9 m while disputed 36.9 m GPS remains near the new origin");
        check(!continuity.TryMapProgress(10, out _),
            "A* expiry: progress outside the shared subsection cannot be invented on a replacement");
        Registration(check, old, next, continuity);
        Fusion(check, old, next, continuity);
        Preview(check, old, next);

        var parallel = Road("AStar", (0, 11), (100, 11));
        var straight = Road("MLD (Railway OSRM)", (0, 0), (100, 0));
        var reverse = Road("AStar", (100, 0), (0, 0));
        var detour = Road("AStar (Hazard-Aware)", (0, 0), (50, 15), (100, 0));
        check(!AStarReplacementContinuityPolicy.TryCreate(straight, parallel, out _) &&
            !AStarReplacementContinuityPolicy.TryCreate(straight, reverse, out _) &&
            !AStarReplacementContinuityPolicy.TryCreate(straight, detour, out _),
            "A* continuity rejects another street, reversed travel and a changed hazard corridor without rejecting their geographic routes");
        var loop = Road("MLD", (0, 0), (100, 0), (0, 0));
        check(!AStarReplacementContinuityPolicy.TryCreate(loop, Road("AStar", (20, 0), (80, 0)), out _),
            "A* continuity refuses an ambiguous repeated leg rather than rebasing onto another traversal");
        check(!AStarReplacementContinuityPolicy.TryCreate(old, Road("MLD (Railway OSRM)",
            (-36.483, 6.079), (-266.700, 27.364)), out _) && old.GeometryFingerprint == originalGeometry,
            "A* replacement policies do not accept MLD outputs or mutate retained MLD geometry");
    }

    private static void Registration(Action<bool, string> check, RouteResult old, RouteResult next,
        AStarReplacementContinuityPolicy continuity)
    {
        var at = DateTimeOffset.UtcNow;
        var policy = new RouteDisplayProgressPolicy();
        var seed = Fix(old, C(-39.430, -8.905), 13.19);
        var seedMotion = new RouteBacktrackingPolicy.TrackedMotion(27.586f, 15.731f, 110.219, 1, 35, 9, 0);
        policy.ObserveGps(old, seed, at);
        policy.ObserveRegistration(old, seed, at, at, seedMotion);
        var support = Fix(old, C(-63.690, 6.529), 14.19);
        var supportAt = at.AddSeconds(18.013);
        policy.ObserveGps(old, support, supportAt);
        policy.ObserveRegistration(old, support, supportAt, supportAt,
            seedMotion with { X = 31.462f, Z = 32.303f, GroundReference = 40 });
        var disputedAt = at.AddSeconds(26.809);
        var now = at.AddSeconds(29.5);
        var motion = seedMotion with { X = 33.041f, Z = 36.267f, GroundReference = 40 };
        var disputed = Fix(old, C(-36.870, 1.892), 8.58) with { CommittedProgressMeters = 68.825 };
        policy.ObserveGps(old, disputed, disputedAt);
        var decision = policy.ObserveRegistration(old, disputed, disputedAt, now, motion);
        var replacementFix = Fix(next, disputed.GpsCoordinate, 8.58);
        check(Math.Abs(decision.InnovationMeters - 25.302) < .01 && !decision.Changed &&
            !policy.TryTransferRegistration(old, next, replacementFix, disputedAt, now, motion),
            "A* expiry: the captured 25.3 m disagreement fails the original safe-transfer threshold");
        var target = C(-100, 12);
        check(policy.TryGetRegisteredReference(old, target, now, motion, out var before, TimeSpan.FromSeconds(30)),
            "A* expiry fixture retains the historical transform for comparison without renewing support");
        var supportedAt = policy.RegistrationSupportedAt;
        var pendingAt = policy.PendingRegistrationAt;
        check(policy.RebindForAStarReplacement(old, next, continuity, replacementFix, disputedAt) &&
            policy.RegistrationSupportedAt == supportedAt && policy.PendingRegistrationAt == pendingAt &&
            policy.RegistrationAccuracyMeters == 13.19 && policy.LastRegistrationDecision == decision,
            "A* expiry preserves original accuracy, stale support and pending correction evidence across geographic acceptance");
        policy.ObserveGps(next, replacementFix, disputedAt);
        var repeated = policy.ObserveRegistration(next, replacementFix, disputedAt, now, motion);
        check(repeated == decision && policy.TryGetRegisteredReference(next, target, now, motion,
            out var after, TimeSpan.FromSeconds(30)) && before.RegisteredAt == after.RegisteredAt &&
            Math.Abs(before.WorldX - after.WorldX) < .001 && Math.Abs(before.WorldZ - after.WorldZ) < .001,
            "A* expiry cannot clear and reseed the same disputed fix or move a common mapped point by 25 metres");
        check(!policy.TryGetRegisteredReference(next, target, now, motion, out _) &&
            !policy.TryGetRegisteredReference(next, target, now, motion with { WorldFrame = 1 }, out _, TimeSpan.FromSeconds(30)),
            "A* geographic acceptance never refreshes stale AR support or bypasses a changed world frame");

        var unregistered = new RouteDisplayProgressPolicy();
        check(!unregistered.RebindForAStarReplacement(old, next, continuity, replacementFix, disputedAt),
            "A* replacement without registration does not manufacture a retained reference");
        unregistered.ObserveGps(next, replacementFix, disputedAt);
        var sameFix = unregistered.ObserveRegistration(next, replacementFix, disputedAt, disputedAt, motion);
        unregistered.ObserveGps(next, replacementFix, disputedAt.AddSeconds(2));
        var oneFresh = unregistered.ObserveRegistration(next, replacementFix, disputedAt.AddSeconds(2), disputedAt.AddSeconds(2), motion);
        check(!sameFix.Changed && !oneFresh.Changed &&
            !unregistered.TryGetRegisteredReference(next, target, disputedAt.AddSeconds(2), motion, out _),
            "A* replacement cannot treat the reused sample or one later fix as independent reacquisition");
        unregistered.ObserveGps(next, replacementFix, disputedAt.AddSeconds(4));
        check(unregistered.ObserveRegistration(next, replacementFix, disputedAt.AddSeconds(4),
            disputedAt.AddSeconds(4), motion).Changed,
            "A* replacement can recover with two distinct consistent fresh observations");
    }

    private static void Fusion(Action<bool, string> check, RouteResult old, RouteResult next,
        AStarReplacementContinuityPolicy continuity)
    {
        var at = DateTimeOffset.UtcNow;
        var fix = Fix(old, C(-36.870, 1.892), 5) with
        { CrossTrackErrorMeters = 1, CourseAlignmentErrorDegrees = 0, MatchConfidence = RouteMatchConfidence.High };
        var fusion = new GpsPdrFusionPolicy();
        fusion.EvaluateGps(68.825, true, fix, at);
        var second = fusion.EvaluateGps(68.825, true, fix, at.AddSeconds(1));
        continuity.TryMapProgress(68.825, out double progress);
        fusion.RebindForAStarReplacement(continuity);
        var nextFix = Fix(next, fix.GpsCoordinate, 5) with
        { CrossTrackErrorMeters = 1, CourseAlignmentErrorDegrees = 0, MatchConfidence = RouteMatchConfidence.High };
        var third = fusion.EvaluateGps(progress, true, nextFix, at.AddSeconds(2));
        check(second.BackwardConfirmationCount == 2 && third.BackwardConfirmationCount == 3 &&
            third.Action == GpsPdrFusionPolicy.GpsFusionAction.CorrectBackward &&
            Math.Abs(third.TargetProgressMeters - (progress - 3)) < .01,
            "A* same-corridor handoff preserves disagreement evidence and its existing three-metre correction bound");

        var identity = new GpsPdrFusionPolicy();
        identity.EvaluateGps(68.825, true, fix with { MatchConfidence = RouteMatchConfidence.Low }, at);
        identity.EvaluateGps(68.825, true, fix, at.AddSeconds(1));
        identity.RebindForAStarReplacement(continuity);
        identity.EvaluateGps(progress, true, nextFix, at.AddSeconds(1));
        check(identity.IsRouteIdentitySuspended,
            "A* corridor rebase preserves the identity observation watermark so duplicate samples cannot vote twice");
        identity.EvaluateGps(progress, true, nextFix, at.AddSeconds(2));
        check(!identity.IsRouteIdentitySuspended,
            "A* corridor rebase retains pending identity evidence for the next independent matching fix");
        identity.RebindForAStarReplacement(null);
        check(identity.IsRouteIdentitySuspended,
            "A* changed corridors require identity requalification instead of clearing uncertainty");

        var motion = new RouteProgressMotionPolicy();
        var tracked = new RouteBacktrackingPolicy.TrackedMotion(0, 0, 0, 1, 1);
        motion.Filter(old, fix.GpsCoordinate, 5, at, 68.825, 69, tracked, out _);
        motion.RebindForAStarReplacement(old, next);
        double heldProgress = motion.Filter(next, fix.GpsCoordinate, 5, at.AddSeconds(1), progress,
            progress + 3, tracked, out bool held);
        check(held && heldProgress == progress,
            "A* replacement retains the stationary motion anchor instead of counting the same GPS noise as travel");
    }

    private static void Preview(Action<bool, string> check, RouteResult mld, RouteResult astar)
    {
        var at = DateTimeOffset.UtcNow;
        var owner = new AStarMappedPreviewPolicy.Owner(1, astar.GeometryFingerprint, 44, 1, 40, 9, 0, 19);
        bool Retain(AStarMappedPreviewPolicy.Owner current, double age, bool registration = true,
            bool held = false, bool identity = false, bool hazard = false) =>
            AStarMappedPreviewPolicy.CanRetain(astar, owner, current, at, at.AddSeconds(age), registration, held, identity, hazard);
        check(Retain(owner, 5.317) && Retain(owner, 8) && !Retain(owner, 8.001),
            "A* preview bridges the captured 5.317-second fix gap within the existing eight-second maximum");
        check(!Retain(owner, 6, registration: false) && !Retain(owner, 6, held: true) &&
            !Retain(owner, 6, identity: true) && !Retain(owner, 6, hazard: true),
            "A* preview retention cannot bypass registration, position, identity or hazard holds");
        check(!Retain(owner with { Trip = 2 }, 6) && !Retain(owner with { Geometry = "changed" }, 6) &&
            !Retain(owner with { Window = 45 }, 6) && !Retain(owner with { Session = 2 }, 6) &&
            !Retain(owner with { Ground = 41 }, 6) && !Retain(owner with { TrackingEpoch = 10 }, 6) &&
            !Retain(owner with { WorldFrame = 1 }, 6) && !Retain(owner with { Calibration = 20 }, 6),
            "A* preview rejects every changed trip, geometry, window, session, ground, epoch, world and calibration owner");
        check(!AStarMappedPreviewPolicy.CanRetain(mld, owner, owner, at, at.AddSeconds(6), true, false, false, false),
            "A* preview ownership does not participate in the locked MLD guidance path");
    }
}
