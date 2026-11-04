using System.Numerics;
using RescuAR.AR;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;

internal static class ARWorldCorrectionChecks
{
    private static Quaternion Yaw(float degrees) => Quaternion.CreateFromAxisAngle(
        Vector3.UnitY, degrees * MathF.PI / 180);

    public static void Run(Action<bool, string> check)
    {
        CheckAnchorYaw(check);
        CheckPendingRecovery(check);
    }

    private static void CheckAnchorYaw(Action<bool, string> check)
    {
        var policy = new ARAnchorYawCorrectionPolicy();
        bool Sample(long frame, long at, Quaternion? rotation, long session = 1, long ground = 1) =>
            policy.Observe(session, ground, frame, at, rotation, out _);
        check(!Sample(1, 0, Quaternion.Identity) && !Sample(2, 100, Yaw(3)) &&
            !Sample(3, 800, Yaw(3)), "anchor yaw: ordinary small refinements retain registration");
        check(!Sample(4, 900, Yaw(6)) && !Sample(5, 1150, Yaw(6)) &&
            Sample(6, 1400, Yaw(6)),
            "anchor yaw: a sustained rotation invalidates placement without any anchor translation");
        check(!Sample(7, 2000, Yaw(6)),
            "anchor yaw: the confirmed orientation becomes the baseline rather than retriggering recovery");

        policy.Reset();
        Sample(1, 0, Quaternion.Identity);
        Sample(2, 100, Yaw(6));
        check(!Sample(3, 1800, Yaw(6)) && !Sample(4, 2050, Yaw(6)) && Sample(5, 2300, Yaw(6)),
            "anchor yaw: tracking gaps preserve the baseline but require new continuous confirmation");

        policy.Reset();
        Sample(1, 0, Quaternion.Identity);
        Sample(2, 100, Yaw(6));
        check(!Sample(2, 700, Yaw(6)) && !Sample(3, 710, Yaw(6)) && Sample(4, 720, Yaw(6)),
            "anchor yaw: a repeated camera frame cannot satisfy the distinct-frame evidence count");

        policy.Reset();
        Sample(1, 0, Quaternion.Identity);
        Sample(2, 100, Yaw(6));
        check(!Sample(3, 700, Yaw(-6)) && !Sample(4, 1300, Yaw(6)) &&
            !Sample(5, 1900, Yaw(-6)),
            "anchor yaw: alternating corrections do not count as sustained directional evidence");

        policy.Reset();
        Sample(1, 0, Quaternion.Identity);
        var pitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, .5f);
        var roll = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .5f);
        check(!Sample(2, 100, pitch) && !Sample(3, 800, pitch) &&
            !Sample(4, 1000, roll) && !Sample(5, 1600, pitch * roll),
            "anchor yaw: surface tilt is not treated as geographic yaw evidence");
        check(!Sample(6, 1700, new Quaternion(0, float.NaN, 0, 1)) &&
            !Sample(7, 2300, default(Quaternion)) && !Sample(8, 2900, null),
            "anchor yaw: unavailable, nonfinite and zero rotations cannot invalidate a world");

        policy.Reset();
        Sample(1, 0, Yaw(170));
        Sample(2, 100, Yaw(-175));
        Sample(3, 350, Yaw(-175));
        check(Sample(4, 600, Yaw(-175)), "anchor yaw: angle wrap preserves the actual 15 degree correction");
        Quaternion signFlipped = -Yaw(-175);
        check(!Sample(5, 1200, signFlipped),
            "anchor yaw: equivalent quaternion signs do not create another correction");
        check(!Sample(6, 1800, Yaw(40), ground: 2) &&
            !Sample(7, 2400, Yaw(40), ground: 2) &&
            !Sample(1, 3000, Yaw(-40), session: 2, ground: 2),
            "anchor yaw: replacement ground and new sessions establish separate baselines");

        policy.Reset();
        policy.SeedReference(1, 1, Quaternion.Identity);
        Sample(1, 100, Yaw(6));
        // Routine progress-window publications must not erase same-anchor evidence.
        policy.SeedReference(1, 1, Yaw(6));
        Sample(2, 350, Yaw(6));
        check(Sample(3, 600, Yaw(6)),
            "anchor yaw: the publication pose detects correction before first draw and survives window updates");
    }

    private static void CheckPendingRecovery(Action<bool, string> check)
    {
        var request = new ARCameraSpatialController.RouteWorldCorrectionRequest(
            1, true, 7, 10, 0, 0, 0, 0, 0, 6);
        check(!request.AcceptsRequalificationFrame(0, 11) &&
            !request.AcceptsRequalificationFrame(1, 10) &&
            request.AcceptsRequalificationFrame(1, 11),
            "world recovery: pending placement permits only a newer pose in its corrected world");

        var recovery = new ARWorldCorrectionRecoveryPolicy();
        check(recovery.Observe(1, 1) && !recovery.Observe(1, 1) && !recovery.Observe(1, 0) &&
            recovery.Observe(1, 2) && recovery.Observe(2, 1),
            "world recovery: reset walking evidence once per correction, including reused generations in a new session");

        var heading = new HeadingRevalidationPolicy();
        recovery = new ARWorldCorrectionRecoveryPolicy();
        var now = DateTimeOffset.UtcNow;
        HeadingRevalidationDecision decision = default;
        for (int i = 0; i <= 3; i++)
        {
            for (int tick = 0; tick < 8; tick++)
                if (recovery.Observe(1, 1)) heading.Reset("corrected world fixture");
            var tracked = MapToArCoordinates.Rotate(i * 12, 0, 20);
            decision = heading.Evaluate(0, C(i * 12), 5, 1.5, 90,
                RouteMatchConfidence.High, 90, (float)tracked.X, (float)tracked.Z,
                now.AddSeconds(i * 8), 1, 1, 0, requiresValidation: true);
        }
        check(decision.ShouldApplyCorrection && decision.ConfirmationCount >= 3,
            "world recovery: movement heading converges while the same placement request remains pending across timer ticks");

        foreach (string algorithm in new[] { "AStar", "MLD (Railway OSRM)" })
        {
            var route = new RouteResult([new(C(0), 0), new(C(120), 120)], 120, algorithm);
            var tracker = new RouteProgressTracker();
            tracker.SetRoute(route);
            var registration = new RouteDisplayProgressPolicy();
            RouteBacktrackingPolicy.TrackedMotion Motion(long world) => new(0, 0, 0, 1, 1, 0, world);
            void Observe(int seconds, long world)
            {
                var update = tracker.Update(C(0), 5);
                var at = now.AddSeconds(seconds);
                registration.ObserveGps(route, update, at);
                registration.ObserveRegistration(route, update, at, at, Motion(world));
            }
            Observe(0, 0);
            check(!registration.TryGetRegisteredReference(route, C(0), now.AddSeconds(1),
                Motion(1), out _), $"{algorithm}: pending recovery cannot publish the old world's registration");
            Observe(2, 1);
            Observe(7, 1);
            check(!registration.TryGetRegisteredReference(route, C(0), now.AddSeconds(7), Motion(1), out _),
                $"{algorithm}: corrected-world placement still requires independent requalification observations");
            Observe(12, 1);
            check(request.AcceptsRequalificationFrame(1, 11) &&
                registration.TryGetRegisteredReference(route, C(0), now.AddSeconds(12), Motion(1), out _),
                $"{algorithm}: qualified corrected-world registration can publish the replacement that ends the pending hold");
        }
    }

    private static GeoCoordinate C(double east) => new(0, east / 6371008.8 * 180 / Math.PI);
}
