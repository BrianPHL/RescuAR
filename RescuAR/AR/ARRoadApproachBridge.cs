using System;
using System.Numerics;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Projection;

namespace RescuAR.AR;

/// <summary>Independent floor arrow, never part of the mapped route bridge.</summary>
public static class ARRoadApproachBridge
{
    public const long MaximumAgeMilliseconds = 2500;
    private static readonly object sync = new();
    private static Snapshot current;
    private static Ownership activeOwnership;
    public readonly record struct Ownership(long Trip, string Geometry, long CalibrationRevision,
        long TrackingEpoch, long WorldFrame, string Role);

    public static void SetOwnership(Ownership ownership)
    {
        lock (sync)
        {
            if (activeOwnership != ownership) current = default;
            activeOwnership = ownership;
        }
    }

    public readonly record struct Snapshot(bool Active, Vector2 Direction,
        GeoCoordinate Origin, double? Accuracy, DateTimeOffset FixTimestamp,
        ARRenderGenerationToken Generation, long GroundReference, ARGroundTrust GroundTrust,
        long PublishedAt, Ownership Owner = default);

    public static void Clear() { lock (sync) current = default; }

    public static bool Publish(GeoCoordinate origin, GeoCoordinate target, double yaw,
        double? accuracy, DateTimeOffset timestamp, long expectedSession,
        Ownership owner = default, Vector2? registeredDirection = null)
    {
        if (!RouteStartupLocationPolicy.CanPlace(origin, accuracy, timestamp, DateTimeOffset.UtcNow) ||
            !RoadApproachCuePolicy.TryGetDirection(origin, target, yaw, out var direction, out _) ||
            !ARFrameCoherencePolicy.TryGetSpatialFrameForCurrentCamera(out var frame) ||
            frame.Generation.SessionGeneration != expectedSession ||
            !frame.IsFresh || !frame.IsTracking || !frame.Pose.IsTracking ||
            !frame.Anchor.IsAvailable || frame.Anchor.Trust == ARGroundTrust.None ||
            frame.Anchor.GroundState.RenderGeneration != frame.Generation)
        {
            Clear();
            return false;
        }
        if (registeredDirection is { } mapped)
        {
            if (!float.IsFinite(mapped.LengthSquared()) || mapped.LengthSquared() < .0001f) { Clear(); return false; }
            direction = Vector2.Normalize(mapped);
        }
        lock (sync)
        {
            if (owner != activeOwnership || owner != default &&
                (owner.TrackingEpoch != frame.TrackingEpoch || owner.WorldFrame != frame.WorldFrame)) return false;
            current = new(true, direction, origin, accuracy, timestamp,
                frame.Generation, frame.Anchor.ReferenceGeneration, frame.Anchor.Trust, Environment.TickCount64, owner);
        }
        return true;
    }

    public static Snapshot GetForFrame(ARCameraPoseBridge.SpatialSnapshot frame)
    {
        Snapshot snapshot;
        lock (sync)
        {
            snapshot = current;
            if (snapshot.Owner != activeOwnership || snapshot.Owner != default &&
                (snapshot.Owner.TrackingEpoch != frame.TrackingEpoch || snapshot.Owner.WorldFrame != frame.WorldFrame))
                return default;
        }
        return CanRender(snapshot, frame.Generation, frame.Anchor.ReferenceGeneration,
            frame.Anchor.Trust, frame.IsFresh && frame.IsTracking && frame.Pose.IsTracking &&
                frame.Anchor.IsAvailable && frame.Anchor.GroundState.RenderGeneration == frame.Generation,
            DateTimeOffset.UtcNow, Environment.TickCount64) ? snapshot : default;
    }

    public static bool CanRender(Snapshot snapshot, ARRenderGenerationToken generation,
        long groundReference, ARGroundTrust trust, bool trackedFloor,
        DateTimeOffset now, long monotonicNow)
    {
        long age = monotonicNow - snapshot.PublishedAt;
        float length = snapshot.Direction.LengthSquared();
        return snapshot.Active && trackedFloor && generation.IsValid &&
            snapshot.Generation == generation && snapshot.GroundReference > 0 &&
            snapshot.GroundReference == groundReference && trust != ARGroundTrust.None &&
            snapshot.GroundTrust == trust && age >= 0 && age <= MaximumAgeMilliseconds &&
            float.IsFinite(length) && MathF.Abs(length - 1) < 0.01f &&
            RouteStartupLocationPolicy.CanPlace(snapshot.Origin, snapshot.Accuracy, snapshot.FixTimestamp, now);
    }
}
