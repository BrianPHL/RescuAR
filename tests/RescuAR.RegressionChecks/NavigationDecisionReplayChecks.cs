using System.Globalization;
using System.Text.Json;
using RescuAR.Diagnostics;
using RescuAR.Navigation.Diagnostics;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;

internal static class NavigationDecisionReplayChecks
{
    private const string Capture = "0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-11-05T09:30:00Z", CultureInfo.InvariantCulture);

    // Coordinates below are constructed around (0,0) from session-relative
    // metre values. They are not the November 5 user's absolute GPS positions.
    private static GeoCoordinate C(double east, double north = 0) => new(
        north / 6371008.8 * 180 / Math.PI, east / 6371008.8 * 180 / Math.PI);

    public static void Run(Action<bool, string> check)
    {
        string[] reconstruction = BuildRelativeFieldReconstruction();
        var report = NavigationDecisionReplay.Run(reconstruction);
        check(report.Records == reconstruction.Length && report.Captures == 1 &&
            report.ContiguousInput && report.Issues.Count == 0,
            "decision replay: synthetic relative field reconstruction loads complete batched geometry and policy records");
        using var label = JsonDocument.Parse(reconstruction[0]);
        check(!label.RootElement.GetProperty("data").GetProperty("exactCoordinates").GetBoolean() &&
            label.RootElement.GetProperty("data").GetProperty("syntheticRelativeCoordinates").GetBoolean(),
            "decision replay: the relative November 5 reconstruction is explicitly labelled as synthetic, not recovered private GPS");

        var registration = report.Events.Where(e => e.Kind == "registration.observation").Select(Result).ToArray();
        var disputed = registration[^2];
        var repeated = registration[^1];
        check(Math.Abs(disputed.GetProperty("computed").GetProperty("innovationMeters").GetDouble() - 25.302) < .01 &&
            !disputed.GetProperty("computed").GetProperty("changed").GetBoolean(),
            "decision replay: actual registration policy reproduces the captured 25.302-metre GPS/AR disagreement");
        var replacement = Result(report.Events.Single(e => e.Kind == "route.replacement"));
        check(replacement.GetProperty("sameCorridor").GetBoolean() &&
            replacement.GetProperty("progressMapped").GetBoolean() &&
            replacement.GetProperty("registrationPreserved").GetBoolean() &&
            repeated.GetProperty("computed").GetProperty("state").GetString() == "CORRECTION_AWAITING_EVIDENCE" &&
            repeated.GetProperty("computed").GetProperty("innovationMeters").GetDouble() ==
                disputed.GetProperty("computed").GetProperty("innovationMeters").GetDouble() &&
            repeated.GetProperty("supportedAtUtc").GetDateTimeOffset() ==
                disputed.GetProperty("supportedAtUtc").GetDateTimeOffset(),
            "decision replay: A* replacement and duplicate input retain the old transform/support age instead of reseeding disputed GPS");
        var step = Result(report.Events.Single(e => e.Kind == "navigation.pdr"));
        using var replacementInput = JsonDocument.Parse(reconstruction.Single(line =>
            line.Contains("\"kind\":\"route.replacement\"", StringComparison.Ordinal)));
        double mapped = replacementInput.RootElement.GetProperty("data").GetProperty("match")
            .GetProperty("committedProgressMeters").GetDouble();
        check(mapped > 31 && mapped < 33 &&
            Math.Abs(step.GetProperty("committedProgressMeters").GetDouble() - mapped - .7) < 1e-8,
            "decision replay: accepted old progress retains the captured geometric rebase before adding the recorded 0.7-metre step");

        Arrival(check);
        StartupAndResetOrdering(check);
        RegistrationTransfer(check);
        IdenticalGeometryReplacement(check);
        ProviderDisplacement(check);
        InvalidInput(check);
    }

    public static string[] BuildRelativeFieldReconstruction()
    {
        var old = Road("MLD (Railway OSRM, Hazard-Aware)", (.257, 2.682), (-97.320, 11.689),
            (-113.673, 13.245), (-197.049, 20.918), (-237.931, 24.698), (-266.763, 27.367));
        var next = Road("AStar (Hazard-Aware)", (-36.483, 6.079), (-97.288, 11.700),
            (-113.662, 13.212), (-197.028, 20.929), (-237.952, 24.710), (-266.700, 27.364));
        var lines = new List<string>();
        Add(lines, "capture_start", new { exactCoordinates = false, syntheticRelativeCoordinates = true,
            reconstruction = "November 5 session-relative registration/handoff inputs; absolute GPS was redacted",
            coordinateOrigin = "synthetic WGS84 (0,0)", recoveredAbsoluteFieldCoordinates = false });
        AddRoute(lines, old, "START");
        var seedMotion = new RouteBacktrackingPolicy.TrackedMotion(27.586f, 15.731f, 110.219, 1, 35, 9, 0);
        var seed = Fix(old, C(-39.430, -8.905), 13.19);
        AddRegistration(lines, old, seed, seedMotion, At, At, "ACQUISITION_SEEDED", 0);
        var supportAt = At.AddSeconds(18.013);
        AddRegistration(lines, old, Fix(old, C(-63.690, 6.529), 14.19),
            seedMotion with { X = 31.462f, Z = 32.303f, GroundReference = 40 },
            supportAt, supportAt, "CORRECTION_AWAITING_EVIDENCE", double.NaN);
        var disputedAt = At.AddSeconds(26.809);
        var evaluatedAt = At.AddSeconds(29.5);
        var motion = seedMotion with { X = 33.041f, Z = 36.267f, GroundReference = 40 };
        var disputed = Fix(old, C(-36.870, 1.892), 8.58);
        AddRegistration(lines, old, disputed, motion, disputedAt, evaluatedAt, "CORRECTION_AWAITING_EVIDENCE", 25.302);
        AddRoute(lines, next, "REPLAN");
        var previousTracker = new RouteProgressTracker();
        previousTracker.SetRoute(old);
        previousTracker.ApplyFusionCorrection(68.825, disputed, "SYNTHETIC_RECONSTRUCTION_PROGRESS");
        var replacementTracker = new RouteProgressTracker();
        replacementTracker.SetRoute(next);
        var match = replacementTracker.Update(disputed.GpsCoordinate, 8.58);
        if (!AStarReplacementContinuityPolicy.TryCreate(old, next, out var continuity) ||
            !continuity!.TryMapProgress(68.825, out double mapped))
            throw new InvalidOperationException("Synthetic handoff geometry lost its intended shared corridor.");
        match = replacementTracker.ApplyFusionCorrection(mapped, match, "SYNTHETIC_RECONSTRUCTION_REBASE");
        Add(lines, "route.replacement", new { trip = 1, previousGeometry = old.GeometryFingerprint,
            geometry = next.GeometryFingerprint, previousProgress = previousTracker.Current, match,
            observationTimeUtc = disputedAt, evaluatedAtUtc = evaluatedAt });
        AddRegistration(lines, next, match, motion, disputedAt, evaluatedAt,
            "CORRECTION_AWAITING_EVIDENCE", 25.302);
        Add(lines, "navigation.pdr", new { trip = 1, geometry = next.GeometryFingerprint,
            advanceMeters = .7, evaluatedAtUtc = evaluatedAt.AddSeconds(1) });
        return lines.ToArray();
    }

    private static void Arrival(Action<bool, string> check)
    {
        var lines = Start();
        foreach (var observation in new[] { (Distance: 23.4, Seconds: 0, Accuracy: 7.5),
            (Distance: 24.3, Seconds: 4, Accuracy: 8.5) })
            Add(lines, "arrival.observation", new { trip = 1,
                reading = Reading(C(observation.Distance), observation.Accuracy, "gps", At.AddSeconds(observation.Seconds)),
                destination = C(0), radiusMeters = 30, remainingMeters = double.PositiveInfinity,
                evaluatedAtUtc = At.AddSeconds(observation.Seconds), majorRoadBarrier = false,
                geographicHeld = false, decision = SafeZoneConfirmationService.SafeZoneDecision.Unavailable });
        var report = NavigationDecisionReplay.Run(lines);
        var last = Result(report.Events.Last());
        check(report.Issues.Count == 0 && last.GetProperty("computed").GetProperty("isConfirmed").GetBoolean() &&
            !last.GetProperty("completesGuidance").GetBoolean(),
            "decision replay: repeated fresh 23–24-metre vicinity observations with no route stay informational and never complete guidance");
        Add(lines, "policy.reset", new { trip = 1, reason = "same destination restarted", arrival = true });
        Add(lines, "arrival.observation", new { trip = 1, reading = Reading(C(24), 8, "gps", At.AddSeconds(8)),
            destination = C(0), radiusMeters = 30, remainingMeters = double.PositiveInfinity,
            evaluatedAtUtc = At.AddSeconds(8), majorRoadBarrier = false, geographicHeld = false,
            decision = SafeZoneConfirmationService.SafeZoneDecision.Unavailable });
        var reset = NavigationDecisionReplay.Run(lines);
        var afterReset = Result(reset.Events.Last());
        check(reset.Issues.Count == 0 && !afterReset.GetProperty("computed").GetProperty("isConfirmed").GetBoolean() &&
            afterReset.GetProperty("computed").GetProperty("confirmationCount").GetInt32() == 1,
            "decision replay: same-trip arrival resets remove the old informational confirmation before another observation");
    }

    private static void StartupAndResetOrdering(Action<bool, string> check)
    {
        var route = Road("AStar", (0, 0), (100, 0));
        var lines = Start();
        AddRoute(lines, route, "START");
        var tracker = new RouteProgressTracker();
        tracker.SetRoute(route);
        var initial = tracker.Update(C(10), 5);
        Add(lines, "navigation.startup", new { trip = 1, geometry = route.GeometryFingerprint,
            reading = Reading(C(10), 5, "gps", At), origin = C(10), matched = initial, fused = initial,
            evaluatedAtUtc = At, motionFilterProgress = 10, motionFilterHeld = false });
        var motion = new RouteBacktrackingPolicy.TrackedMotion(0, 0, 0, 1, 1, 1, 0);
        AddRegistration(lines, route, initial, motion, At, At, "ACQUISITION_SEEDED", 0);
        Add(lines, "navigation.pdr", new { trip = 1, geometry = route.GeometryFingerprint, advanceMeters = .7 });
        tracker.AdvanceDeadReckoning(.7);
        var before = tracker.Current;
        var matched = tracker.Update(C(10), 5);
        var fused = tracker.ApplyFusionCorrection(10.7, matched, "EXPECTED_STATIONARY_PROGRESS");
        Add(lines, "navigation.observation", new { trip = 1, geometry = route.GeometryFingerprint,
            reading = Reading(C(10), 5, "gps", At.AddSeconds(2)), before, matched, fused,
            motion, evaluatedAtUtc = At.AddSeconds(2) });
        AddRegistration(lines, route, fused, motion, At.AddSeconds(2), At.AddSeconds(2), "REFERENCE_SUPPORTED", 0);
        Add(lines, "policy.reset", new { trip = 1, reason = "new heading calibration", registration = true, position = true });
        AddRegistration(lines, route, Fix(route, C(12), 5), motion with { X = 2, YawDegrees = 10 },
            At.AddSeconds(4), At.AddSeconds(4), "ACQUISITION_SEEDED", 0);
        var report = NavigationDecisionReplay.Run(lines);
        var step = Result(report.Events.Single(e => e.Kind == "navigation.pdr"));
        var observation = Result(report.Events.Single(e => e.Kind == "navigation.observation"));
        check(report.Issues.Count == 0 && Math.Abs(step.GetProperty("committedProgressMeters").GetDouble() - 10.7) < .001 &&
            observation.GetProperty("fusion").GetProperty("action").GetInt32() == (int)GpsPdrFusionPolicy.GpsFusionAction.HoldStationary &&
            !observation.GetProperty("observation").GetProperty("isHeld").GetBoolean(),
            "decision replay: startup initializes tracker/fusion/stationary state before PDR and evaluates GPS before its registration stage");
        check(Result(report.Events.Last(e => e.Kind == "registration.observation"))
                .GetProperty("computed").GetProperty("state").GetString() == "ACQUISITION_SEEDED" &&
            report.DifferentProgressSamples == 0,
            "decision replay: an explicit heading reset clears only the recorded registration/position scopes before fresh acquisition");
    }

    private static void RegistrationTransfer(Action<bool, string> check)
    {
        foreach (double endpoint in new[] { 120.0, 100.0 })
            RegistrationTransfer(check, endpoint);
    }

    private static void RegistrationTransfer(Action<bool, string> check, double endpoint)
    {
        var old = Road("MLD (Railway OSRM)", (0, 0), (100, 0));
        var next = Road("MLD (Railway OSRM)", (0, 0), (endpoint, 0));
        var lines = Start();
        AddRoute(lines, old, "START");
        var motion = new RouteBacktrackingPolicy.TrackedMotion(0, 0, 0, 1, 1, 1, 0);
        AddRegistration(lines, old, Fix(old, C(10), 5), motion, At, At, "ACQUISITION_SEEDED", 0);
        AddRoute(lines, next, "REPLAN");
        var matched = Fix(next, C(12), 5);
        Add(lines, "route.match", new { trip = 1, geometry = next.GeometryFingerprint,
            reading = Reading(C(12), 5, "gps", At.AddSeconds(2)), update = matched });
        Add(lines, "registration.transfer", new { trip = 1, previousGeometry = old.GeometryFingerprint,
            geometry = next.GeometryFingerprint, reading = Reading(C(12), 5, "gps", At.AddSeconds(2)),
            update = matched, motion = motion with { X = 2 }, evaluatedAtUtc = At.AddSeconds(2), retained = true });
        Add(lines, "navigation.pdr", new { trip = 1, geometry = next.GeometryFingerprint, advanceMeters = .7 });
        var report = NavigationDecisionReplay.Run(lines);
        var transfer = Result(report.Events.Single(e => e.Kind == "registration.transfer"));
        check(report.Issues.Count == 0 && transfer.GetProperty("retained").GetBoolean() &&
            transfer.GetProperty("supportedAtUtc").GetDateTimeOffset() == At &&
            Math.Abs(Result(report.Events.Last()).GetProperty("committedProgressMeters").GetDouble() - 12.7) < .001,
            endpoint == 100
                ? "decision replay: identical geometry retains distinct old/new route instances for safe registration transfer"
                : "decision replay: ordinary replacement matching and safe registration transfer retain original support and initialize replacement progress");
    }

    private static void IdenticalGeometryReplacement(Action<bool, string> check)
    {
        var old = Road("MLD (Railway OSRM)", (0, 0), (100, 0));
        var next = Road("AStar", (0, 0), (100, 0));
        var lines = Start();
        AddRoute(lines, old, "START");
        var motion = new RouteBacktrackingPolicy.TrackedMotion(0, 0, 0, 1, 1, 1, 0);
        var previousTracker = new RouteProgressTracker();
        previousTracker.SetRoute(old);
        var previous = previousTracker.Update(C(10), 5);
        AddRegistration(lines, old, previous, motion, At, At, "ACQUISITION_SEEDED", 0);
        AddRoute(lines, next, "REPLAN");
        Add(lines, "route.replacement", new { trip = 1, previousGeometry = old.GeometryFingerprint,
            geometry = next.GeometryFingerprint, previousProgress = previousTracker.Current,
            match = Fix(next, C(12), 5), observationTimeUtc = At.AddSeconds(2) });
        var report = NavigationDecisionReplay.Run(lines);
        var replacement = Result(report.Events.Single(e => e.Kind == "route.replacement"));
        check(report.Issues.Count == 0 && old.GeometryFingerprint == next.GeometryFingerprint &&
            replacement.GetProperty("registrationPreserved").GetBoolean() &&
            replacement.GetProperty("progressMapped").GetBoolean(),
            "decision replay: identical MLD/A* geometry preserves the preceding registration object during corridor rebind");
    }

    private static void ProviderDisplacement(Action<bool, string> check)
    {
        var route = Road("AStar", (0, 0), (100, 0));
        var lines = Start();
        AddRoute(lines, route, "START");
        var tracker = new RouteProgressTracker();
        tracker.SetRoute(route);
        for (int i = 0; i < 2; i++)
        {
            var coordinate = C(i == 0 ? 10 : 30);
            var before = tracker.Current;
            var matched = tracker.Update(coordinate, i == 0 ? 5 : 15);
            var recorded = matched with { CommittedProgressMeters = -999 }; // Deliberately different baseline.
            Add(lines, "navigation.observation", new { trip = 1, geometry = route.GeometryFingerprint,
                reading = Reading(coordinate, i == 0 ? 5 : 15, i == 0 ? "gps" : "network", At.AddSeconds(i * 2)),
                before, fused = recorded, evaluatedAtUtc = At.AddSeconds(i * 2),
                motion = new RouteBacktrackingPolicy.TrackedMotion(i, 0, 0, 1, 1, 1, 0) });
        }
        var report = NavigationDecisionReplay.Run(lines);
        var last = Result(report.Events.Last(e => e.Kind == "navigation.observation"));
        check(report.Issues.Count == 0 && last.GetProperty("provider").GetString() == "network" &&
            Math.Abs(last.GetProperty("gpsDisplacementMeters").GetDouble() - 20) < .001 &&
            Math.Abs(last.GetProperty("trackedDisplacementMeters").GetDouble() - 1) < .001,
            "decision replay: provider changes report measured 20-metre GPS versus 1-metre tracked displacement without replacing either input");
        check(report.ComparedProgressSamples == 2 && report.DifferentProgressSamples == 2,
            "decision replay: changed policy progress is surfaced against an intentionally different captured baseline");
    }

    private static void InvalidInput(Action<bool, string> check)
    {
        var start = Start();
        check(Throws<InvalidDataException>(() => NavigationDecisionReplay.Run(start.Concat(start))) &&
            Throws<InvalidDataException>(() => NavigationDecisionReplay.Run(Array.Empty<string>())),
            "decision replay: duplicate/out-of-order sequences and empty inputs cannot silently run");
        check(Throws<InvalidDataException>(() => NavigationDecisionReplay.Run(new[]
            { start[0].Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal) })),
            "decision replay: unsupported schema versions are rejected before policy execution");
        var gap = Start();
        Add(gap, "capture_checkpoint", new { });
        gap[1] = gap[1].Replace("\"sequence\":2", "\"sequence\":3", StringComparison.Ordinal);
        var missing = NavigationDecisionReplay.Run(gap);
        check(!missing.ContiguousInput && missing.Issues.Any(issue => issue.Contains("missing sequence", StringComparison.Ordinal)),
            "decision replay: a missing entire event is reported instead of presenting a complete reconstruction");
        var route = Road("AStar", (0, 0), (100, 0));
        var invalid = Start();
        Add(invalid, "route.geometry", new { trip = 1, geometry = "incorrect-fingerprint", totalPoints = 2,
            startIndex = 0, points = route.Points });
        Add(invalid, "route.accepted", new { trip = 1, geometry = "incorrect-fingerprint", reason = "START",
            route.Algorithm, route.TotalDistanceMeters, route.Provenance });
        var mismatch = NavigationDecisionReplay.Run(invalid);
        check(mismatch.Issues.Any(issue => issue.Contains("fingerprint", StringComparison.Ordinal)) &&
            !mismatch.Events.Any(e => e.Kind == "route.accepted"),
            "decision replay: route fingerprint mismatches cannot silently feed navigation policies");
        var incomplete = Start();
        Add(incomplete, "route.geometry", new { trip = 1, geometry = route.GeometryFingerprint, totalPoints = 2,
            startIndex = 0, points = route.Points.Take(1).ToArray() });
        Add(incomplete, "route.accepted", new { trip = 1, geometry = route.GeometryFingerprint, reason = "START",
            route.Algorithm, route.TotalDistanceMeters, route.Provenance });
        check(NavigationDecisionReplay.Run(incomplete).Issues.Any(issue => issue.Contains("missing exact geometry", StringComparison.Ordinal)),
            "decision replay: incomplete geometry batches are identified before route acceptance");
    }

    private static object Reading(GeoCoordinate coordinate, double accuracy, string provider, DateTimeOffset observedAt) =>
        new { latitude = coordinate.Latitude, longitude = coordinate.Longitude, accuracyMeters = accuracy,
            speedMetersPerSecond = (double?)null, courseDegrees = (double?)null, courseAccuracyDegrees = (double?)null,
            provider, observationTimeUtc = observedAt, providerTimestampUtc = observedAt };

    private static void AddRegistration(List<string> lines, RouteResult route,
        RouteProgressTracker.RouteProgressUpdate update, RouteBacktrackingPolicy.TrackedMotion motion,
        DateTimeOffset observedAt, DateTimeOffset evaluatedAt, string recordedState, double innovation) =>
        Add(lines, "registration.observation", new { trip = 1, geometry = route.GeometryFingerprint,
            reading = Reading(update.GpsCoordinate, update.AccuracyMeters!.Value, "gps", observedAt), update, motion,
            evaluatedAtUtc = evaluatedAt, decision = new RouteDisplayProgressPolicy.RegistrationDecision(recordedState, innovation, false) });

    private static List<string> Start()
    {
        var lines = new List<string>();
        Add(lines, "capture_start", new { exactCoordinates = false, syntheticRelativeCoordinates = true });
        return lines;
    }

    private static void AddRoute(List<string> lines, RouteResult route, string reason)
    {
        int split = Math.Max(1, route.Points.Count / 2);
        for (int start = 0; start < route.Points.Count; start += split)
            Add(lines, "route.geometry", new { trip = 1, geometry = route.GeometryFingerprint,
                totalPoints = route.Points.Count, startIndex = start, points = route.Points.Skip(start).Take(split).ToArray() });
        Add(lines, "route.accepted", new { trip = 1, geometry = route.GeometryFingerprint, reason,
            route.Algorithm, route.TotalDistanceMeters, route.Provenance });
    }

    private static void Add(List<string> lines, string kind, object data) => lines.Add(JsonSerializer.Serialize(new
    {
        schema = NavigationReplayCodec.Schema, version = NavigationReplayCodec.Version, captureId = Capture,
        sequence = lines.Count + 1, kind, utc = At, monotonicMs = 1000L + lines.Count, data
    }, NavigationReplayCodec.JsonOptions));

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

    private static JsonElement Result(NavigationDecisionReplay.EventResult result) =>
        JsonSerializer.SerializeToElement(result.Result, NavigationReplayCodec.JsonOptions);

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }
}
