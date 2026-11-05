using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RescuAR.Diagnostics;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;

namespace RescuAR.Navigation.Diagnostics;

/// <summary>Replays captured policy-stage inputs with production navigation
/// policies. Readiness, accepted PDR advances and user choices are recorded
/// inputs; this does not simulate ARCore, camera pixels, sensors or the UI.</summary>
public static class NavigationDecisionReplay
{
    public sealed record EventResult(long Sequence, string Kind, long Trip, object Result);
    public sealed record Report(int Version, int Records, int Captures, bool ContiguousInput,
        int ComparedProgressSamples, int DifferentProgressSamples,
        IReadOnlyList<string> Issues, IReadOnlyList<EventResult> Events, string CoordinateMode);

    private static readonly JsonSerializerOptions json = new(NavigationReplayCodec.JsonOptions)
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class TripState
    {
        public readonly RouteDisplayProgressPolicy Registration = new();
        public readonly GpsPdrFusionPolicy Fusion = new();
        public readonly RouteProgressMotionPolicy ProgressMotion = new();
        public readonly RoutePositionObservationPolicy Position = new();
        public readonly NavigationStabilityPolicy Stability = new();
        public readonly SafeZoneConfirmationService Vicinity = new();
        public readonly ArrivalGuidancePolicy Arrival = new();
        public readonly Dictionary<string, RouteResult> Routes = new();
        public readonly Dictionary<string, RouteProgressTracker> Trackers = new();
        public RouteResult? ActiveRoute;
        public RouteResult? PreviousRoute;
        public Reading? PreviousReading;
        public RouteBacktrackingPolicy.TrackedMotion? PreviousMotion;
    }

    private sealed record Reading(double Latitude, double Longitude, double? AccuracyMeters,
        double? SpeedMetersPerSecond, double? CourseDegrees, double? CourseAccuracyDegrees,
        string Provider, DateTimeOffset ObservationTimeUtc)
    {
        public GeoCoordinate Coordinate => new(Latitude, Longitude);
        public double? QualifiedCourse => CourseAccuracyDegrees is { } accuracy &&
            (!double.IsFinite(accuracy) || accuracy < 0 || accuracy > 30) ? null : CourseDegrees;
    }

    public static Report Run(IEnumerable<string> lines)
    {
        var events = new List<EventResult>();
        var issues = new List<string>();
        var geometry = new Dictionary<string, RoutePoint?[]>();
        var trips = new Dictionary<long, TripState>();
        var captures = new HashSet<string>();
        var coordinateModes = new HashSet<string>();
        string? currentCapture = null;
        long lastSequence = 0;
        int records = 0, compared = 0, different = 0;
        bool contiguous = true;
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var document = JsonDocument.Parse(line);
            var envelope = document.RootElement;
            if (Text(envelope, "schema") != NavigationReplayCodec.Schema ||
                envelope.GetProperty("version").GetInt32() != NavigationReplayCodec.Version)
                throw new InvalidDataException("Unsupported navigation replay schema/version.");
            string capture = Text(envelope, "captureId");
            long sequence = envelope.GetProperty("sequence").GetInt64();
            if (capture != currentCapture)
            {
                if (!captures.Add(capture)) throw new InvalidDataException("Capture records must be grouped in capture order.");
                currentCapture = capture;
                lastSequence = 0;
                geometry.Clear(); trips.Clear();
            }
            if (sequence <= lastSequence) throw new InvalidDataException("Duplicate or out-of-order replay sequence.");
            if (sequence != lastSequence + 1)
            {
                contiguous = false;
                issues.Add($"Capture {captures.Count}: missing sequence before {sequence}.");
            }
            lastSequence = sequence;
            records++;
            string kind = Text(envelope, "kind");
            var data = envelope.GetProperty("data");
            long trip = data.TryGetProperty("trip", out var tripValue) ? tripValue.GetInt64() : 0;
            if (!trips.TryGetValue(trip, out var state)) trips[trip] = state = new();
            void Add(object result) => events.Add(new(sequence, kind, trip, result));
            try
            {
                switch (kind)
                {
                    case "capture_start":
                        coordinateModes.Add(data.TryGetProperty("syntheticRelativeCoordinates", out var synthetic) && synthetic.GetBoolean()
                            ? "synthetic-relative (absolute field GPS unavailable)"
                            : Boolean(data, "exactCoordinates") ? "exact-WGS84" : "unspecified");
                        Add(data.Clone());
                        break;
                    case "build":
                        Add(data.Clone());
                        break;
                    case "policy.reset":
                    {
                        bool Flag(string name) => data.TryGetProperty(name, out var value) && value.GetBoolean();
                        if (Flag("registration")) state.Registration.Reset();
                        if (Flag("position")) state.Position.Reset();
                        if (Flag("stability")) state.Stability.Reset();
                        if (Flag("progressMotion")) state.ProgressMotion.Reset();
                        if (Flag("fusion")) state.Fusion.Reset();
                        if (Flag("arrival")) { state.Arrival.Reset(); state.Vicinity.Reset(trip); }
                        Add(data.Clone());
                        break;
                    }
                    case "route.geometry":
                    {
                        string id = Text(data, "geometry");
                        int count = data.GetProperty("totalPoints").GetInt32();
                        int start = data.GetProperty("startIndex").GetInt32();
                        if (count < 2 || count > 1_000_000) throw new InvalidDataException("Unsupported route point count.");
                        if (!geometry.TryGetValue(id, out var points)) geometry[id] = points = new RoutePoint?[count];
                        var batch = Read<RoutePoint[]>(data, "points");
                        if (points.Length != count || start < 0 || start > count - batch.Length)
                            throw new InvalidDataException("Invalid route geometry batch.");
                        for (int i = 0; i < batch.Length; i++)
                        {
                            if (points[start + i] is not null && points[start + i] != batch[i])
                                throw new InvalidDataException("Conflicting route geometry batch.");
                            points[start + i] = batch[i];
                        }
                        break;
                    }
                    case "route.accepted":
                    {
                        string id = Text(data, "geometry");
                        if (!geometry.TryGetValue(id, out var points) || points.Any(p => p is null))
                            throw new InvalidDataException("Accepted route is missing exact geometry batches.");
                        var route = new RouteResult(points.Select(p => p!).ToArray(), Number(data, "totalDistanceMeters"),
                            Text(data, "algorithm"), Read<RouteProvenance>(data, "provenance"));
                        if (route.GeometryFingerprint != id) throw new InvalidDataException("Route geometry fingerprint does not match.");
                        // Geometry identity can stay identical across distinct
                        // accepted route objects. Registration rebind/transfer
                        // must still receive the preceding instance.
                        state.PreviousRoute = state.ActiveRoute;
                        state.ActiveRoute = route;
                        state.Routes[id] = route;
                        var tracker = new RouteProgressTracker();
                        tracker.SetRoute(route);
                        state.Trackers[id] = tracker;
                        if (Text(data, "reason") == "START")
                        {
                            state.Registration.Reset(); state.Fusion.Reset(); state.ProgressMotion.Reset();
                            state.Position.Reset(); state.Stability.Reset();
                        }
                        Add(new { geometry = id, route.Algorithm, route.TotalDistanceMeters, points = route.Points.Count });
                        break;
                    }
                    case "route.replacement":
                    {
                        var previous = Route(data, "previousGeometry");
                        var replacement = Route(data, "geometry");
                        AStarReplacementContinuityPolicy.TryCreate(previous, replacement, out var continuity);
                        var update = Read<RouteProgressTracker.RouteProgressUpdate>(data, "match");
                        var before = Read<RouteProgressTracker.ProgressSnapshot>(data, "previousProgress");
                        bool mapped = before.HasProgress && continuity is not null &&
                            continuity.TryMapProgress(before.CommittedProgressMeters, out _);
                        if (mapped && continuity!.TryMapProgress(before.CommittedProgressMeters, out double mappedProgress))
                            state.Trackers[replacement.GeometryFingerprint].ApplyFusionCorrection(
                                mappedProgress, update, "ASTAR_CORRIDOR_REBASE");
                        bool retained = state.Registration.RebindForAStarReplacement(previous, replacement,
                            continuity, update, Date(data, "observationTimeUtc"));
                        state.Fusion.RebindForAStarReplacement(mapped ? continuity : null);
                        state.Position.RebindForAStarReplacement(previous, replacement);
                        state.ProgressMotion.RebindForAStarReplacement(previous, replacement);
                        state.Stability.RebindForAStarReplacement(previous, replacement);
                        Add(new { sameCorridor = continuity is not null, progressMapped = mapped,
                            registrationPreserved = retained, supportedAtUtc = state.Registration.RegistrationSupportedAt });
                        break;
                    }
                    case "route.match":
                    {
                        var route = Route(data, "geometry");
                        var reading = Read<Reading>(data, "reading");
                        var update = state.Trackers[route.GeometryFingerprint].Update(reading.Coordinate,
                            reading.AccuracyMeters, reading.QualifiedCourse, reading.SpeedMetersPerSecond);
                        Add(new { computed = update, recorded = data.GetProperty("update").Clone() });
                        break;
                    }
                    case "navigation.startup":
                    {
                        var route = Route(data, "geometry");
                        var reading = Read<Reading>(data, "reading");
                        var origin = Read<GeoCoordinate>(data, "origin");
                        var tracker = state.Trackers[route.GeometryFingerprint];
                        var matched = tracker.Update(origin, reading.AccuracyMeters,
                            reading.QualifiedCourse, reading.SpeedMetersPerSecond);
                        var fusion = state.Fusion.EvaluateGps(0, false, matched, Date(data, "evaluatedAtUtc"));
                        var fused = matched;
                        if (matched.IsAccepted && Math.Abs(fusion.TargetProgressMeters - matched.CommittedProgressMeters) > .001)
                            fused = tracker.ApplyFusionCorrection(fusion.TargetProgressMeters, matched, "REPLAY_STARTUP_FUSION");
                        double? motionFilterProgress = null;
                        bool? motionFilterHeld = null;
                        if (fused.IsAccepted && !fused.IsOffRoute)
                        {
                            motionFilterProgress = state.ProgressMotion.Filter(route, origin, reading.AccuracyMeters,
                                reading.ObservationTimeUtc, fused.CommittedProgressMeters, fused.CommittedProgressMeters,
                                null, out bool held);
                            motionFilterHeld = held;
                        }
                        double recorded = Number(data.GetProperty("fused"), "committedProgressMeters");
                        bool differs = Math.Abs(fused.CommittedProgressMeters - recorded) > .01;
                        compared++; if (differs) different++;
                        Add(new { matched, fusion, fused, motionFilterProgress, motionFilterHeld,
                            recordedProgressMeters = recorded, computedProgressMeters = fused.CommittedProgressMeters, differs });
                        break;
                    }
                    case "registration.transfer":
                    {
                        var previous = Route(data, "previousGeometry");
                        var replacement = Route(data, "geometry");
                        var reading = Read<Reading>(data, "reading");
                        var retained = state.Registration.TryTransferRegistration(previous, replacement,
                            Read<RouteProgressTracker.RouteProgressUpdate>(data, "update"), reading.ObservationTimeUtc,
                            Date(data, "evaluatedAtUtc"), Read<RouteBacktrackingPolicy.TrackedMotion>(data, "motion"));
                        Add(new { retained, recorded = Boolean(data, "retained"),
                            supportedAtUtc = state.Registration.RegistrationSupportedAt });
                        break;
                    }
                    case "registration.observation":
                    {
                        var route = Route(data, "geometry");
                        var reading = Read<Reading>(data, "reading");
                        var update = Read<RouteProgressTracker.RouteProgressUpdate>(data, "update");
                        var motion = Read<RouteBacktrackingPolicy.TrackedMotion>(data, "motion");
                        state.Registration.ObserveGps(route, update, reading.ObservationTimeUtc);
                        var decision = state.Registration.ObserveRegistration(route, update,
                            reading.ObservationTimeUtc, Date(data, "evaluatedAtUtc"), motion);
                        state.Position.Seed(route, reading.Coordinate, reading.AccuracyMeters,
                            reading.ObservationTimeUtc, Date(data, "evaluatedAtUtc"), motion);
                        Add(new { computed = decision, recorded = data.GetProperty("decision").Clone(),
                            supportedAtUtc = state.Registration.RegistrationSupportedAt });
                        break;
                    }
                    case "navigation.observation":
                    {
                        var route = Route(data, "geometry");
                        var reading = Read<Reading>(data, "reading");
                        var before = Read<RouteProgressTracker.ProgressSnapshot>(data, "before");
                        var motion = ReadNullable<RouteBacktrackingPolicy.TrackedMotion>(data, "motion");
                        DateTimeOffset now = Date(data, "evaluatedAtUtc");
                        var observation = state.Position.Evaluate(route, reading.Coordinate, reading.AccuracyMeters,
                            reading.ObservationTimeUtc, now, motion);
                        var stability = state.Stability.ObserveGeographic(route, reading.Coordinate,
                            reading.AccuracyMeters, reading.ObservationTimeUtc, now);
                        var tracker = state.Trackers[route.GeometryFingerprint];
                        var matched = stability.Reliable ? tracker.Update(reading.Coordinate, reading.AccuracyMeters,
                            reading.QualifiedCourse, reading.SpeedMetersPerSecond) : Held(before, reading);
                        var fusion = !stability.Reliable
                            ? new GpsPdrFusionPolicy.GpsFusionDecision(GpsPdrFusionPolicy.GpsConfidence.Low,
                                GpsPdrFusionPolicy.GpsFusionAction.HoldLowReliability, before.CommittedProgressMeters,
                                0, 0, 0, 0, observation.Reason)
                            : state.Fusion.EvaluateGps(before.CommittedProgressMeters, before.HasProgress,
                                matched, reading.ObservationTimeUtc);
                        if (stability.Reliable && matched.IsAccepted && !matched.IsOffRoute)
                        {
                            double filtered = state.ProgressMotion.Filter(route, reading.Coordinate,
                                reading.AccuracyMeters, reading.ObservationTimeUtc, before.CommittedProgressMeters,
                                fusion.TargetProgressMeters, null, out bool stationary);
                            if (stationary) fusion = fusion with { TargetProgressMeters = filtered,
                                Action = GpsPdrFusionPolicy.GpsFusionAction.HoldStationary };
                        }
                        var fused = matched;
                        if (matched.IsAccepted && !matched.IsOffRoute &&
                            Math.Abs(fusion.TargetProgressMeters - matched.CommittedProgressMeters) > .001)
                            fused = tracker.ApplyFusionCorrection(fusion.TargetProgressMeters, matched, "REPLAY_FUSION");
                        double recordedProgress = Number(data.GetProperty("fused"), "committedProgressMeters");
                        bool differs = Math.Abs(fused.CommittedProgressMeters - recordedProgress) > .01;
                        compared++; if (differs) different++;
                        double? gpsDisplacement = state.PreviousReading is { } oldReading
                            ? oldReading.Coordinate.DistanceTo(reading.Coordinate) : null;
                        double? trackedDisplacement = motion is { } tracked && state.PreviousMotion is { } oldMotion &&
                            tracked.IsContinuousWith(oldMotion)
                            ? Math.Sqrt(Math.Pow(tracked.X - oldMotion.X, 2) + Math.Pow(tracked.Z - oldMotion.Z, 2)) : null;
                        state.PreviousReading = reading; state.PreviousMotion = motion;
                        Add(new { reading.Provider, reading.AccuracyMeters, reading.Latitude, reading.Longitude,
                            reading.ObservationTimeUtc, ageMilliseconds = (now - reading.ObservationTimeUtc).TotalMilliseconds,
                            gpsDisplacementMeters = gpsDisplacement, trackedDisplacementMeters = trackedDisplacement,
                            observation, stability, matched, fusion, computedProgressMeters = fused.CommittedProgressMeters,
                            recordedProgressMeters = recordedProgress, differs });
                        break;
                    }
                    case "navigation.pdr":
                    {
                        string id = Text(data, "geometry");
                        if (!state.Trackers.TryGetValue(id, out var tracker)) throw new InvalidDataException("PDR route is missing.");
                        Add(tracker.AdvanceDeadReckoning(Number(data, "advanceMeters")));
                        break;
                    }
                    case "arrival.observation":
                    {
                        var reading = Read<Reading>(data, "reading");
                        var target = Read<GeoCoordinate>(data, "destination");
                        var decision = state.Arrival.Evaluate(state.Vicinity,
                            new(reading.Coordinate, reading.AccuracyMeters, reading.Provider, reading.ObservationTimeUtc),
                            target, Number(data, "radiusMeters"), Number(data, "remainingMeters"),
                            Date(data, "evaluatedAtUtc"), trip, Boolean(data, "majorRoadBarrier"), Boolean(data, "geographicHeld"));
                        Add(new { computed = decision, evidence = state.Arrival.LastEvidence,
                            completesGuidance = ArrivalGuidancePolicy.ShouldComplete(decision, false, false),
                            recorded = data.GetProperty("decision").Clone() });
                        break;
                    }
                    case "arrival.user":
                        Add(new { userReported = true, completesGuidance = Boolean(data, "eligible") &&
                            Read<SafeZoneConfirmationService.SafeZoneDecision>(data, "decision").IsConfirmed });
                        break;
                }
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidDataException or JsonException or ArgumentException)
            {
                issues.Add($"Sequence {sequence} ({kind}): {exception.Message}");
            }
            RouteResult Route(JsonElement source, string key)
            {
                string id = Text(source, key);
                if (key == "previousGeometry" && state.PreviousRoute is { } previous &&
                    previous.GeometryFingerprint == id) return previous;
                return state.Routes.TryGetValue(id, out var route) ? route
                    : throw new InvalidDataException("Policy input references missing accepted route geometry.");
            }
        }
        if (records == 0) throw new InvalidDataException("No navigation replay records were found. Extract structured logs first.");
        return new(1, records, captures.Count, contiguous, compared, different, issues, events,
            coordinateModes.Count == 0 ? "unknown" : string.Join("; ", coordinateModes));
    }

    private static T Read<T>(JsonElement value, string key) =>
        value.GetProperty(key).Deserialize<T>(json) ?? throw new InvalidDataException($"Missing {key}.");
    private static T? ReadNullable<T>(JsonElement value, string key) where T : struct =>
        value.TryGetProperty(key, out var item) && item.ValueKind != JsonValueKind.Null ? item.Deserialize<T>(json) : null;
    private static string Text(JsonElement value, string key) => value.GetProperty(key).GetString() ?? string.Empty;
    private static double Number(JsonElement value, string key) => value.GetProperty(key).Deserialize<double>(json);
    private static DateTimeOffset Date(JsonElement value, string key) => value.GetProperty(key).GetDateTimeOffset();
    private static bool Boolean(JsonElement value, string key) => value.GetProperty(key).GetBoolean();
    private static RouteProgressTracker.RouteProgressUpdate Held(RouteProgressTracker.ProgressSnapshot before, Reading reading) =>
        new(false, false, false, before.SegmentIndex, before.RawProgressMeters, before.CommittedProgressMeters,
            before.RemainingMeters, before.CrossTrackErrorMeters, reading.AccuracyMeters, reading.Coordinate,
            before.SnappedCoordinate, "GPS_OBSERVATION_HELD", RouteCorridorPolicy.GetCorridorRadiusMeters(reading.AccuracyMeters, false),
            RouteMatchConfidence.Unavailable, double.NaN);
}
