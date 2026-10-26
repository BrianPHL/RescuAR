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
    private static string lastRenderReason = "no cue published";
    public static string LastRenderReason => System.Threading.Volatile.Read(ref lastRenderReason);
    public readonly record struct Ownership(long Trip, string Geometry, long CalibrationRevision,
        long TrackingEpoch, long WorldFrame, string Role);

    public static void SetOwnership(Ownership ownership)
    {
        lock (sync)
        {
            if (activeOwnership != ownership)
            {
                current = default;
                System.Threading.Volatile.Write(ref lastRenderReason, "cue ownership changed");
            }
            activeOwnership = ownership;
        }
    }

    public readonly record struct Snapshot(bool Active, Vector2 Direction,
        GeoCoordinate Origin, double? Accuracy, DateTimeOffset FixTimestamp,
        ARRenderGenerationToken Generation, long GroundReference, ARGroundTrust GroundTrust,
        long PublishedAt, Ownership Owner = default, Vector2? WorldTarget = null);

    public static void Clear(string reason = "cue cleared")
    {
        lock (sync) current = default;
        System.Threading.Volatile.Write(ref lastRenderReason, reason);
    }

    public static bool Publish(GeoCoordinate origin, GeoCoordinate target, double yaw,
        double? accuracy, DateTimeOffset timestamp, long expectedSession,
        Ownership owner = default, Vector2? registeredDirection = null, Vector2? worldTarget = null)
    {
        if (owner.Role == "MAPPED_TARGET" &&
            (worldTarget is not { } targetWorld || !float.IsFinite(targetWorld.X) || !float.IsFinite(targetWorld.Y)))
        {
            Clear("mapped target registration unavailable");
            return false;
        }
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
                frame.Generation, frame.Anchor.ReferenceGeneration, frame.Anchor.Trust, Environment.TickCount64, owner, worldTarget);
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
            {
                System.Threading.Volatile.Write(ref lastRenderReason, "cue ownership changed");
                return default;
            }
        }
        bool qualified = CanRender(snapshot, frame.Generation, frame.Anchor.ReferenceGeneration,
            frame.Anchor.Trust, frame.IsFresh && frame.IsTracking && frame.Pose.IsTracking &&
                frame.Anchor.IsAvailable && frame.Anchor.GroundState.RenderGeneration == frame.Generation,
            DateTimeOffset.UtcNow, Environment.TickCount64);
        if (!qualified)
        {
            if (snapshot.Active)
                System.Threading.Volatile.Write(ref lastRenderReason,
                    snapshot.GroundReference != frame.Anchor.ReferenceGeneration ? "ground reference changed" :
                    Environment.TickCount64 - snapshot.PublishedAt > MaximumAgeMilliseconds ? "cue publication expired" :
                    !RouteStartupLocationPolicy.CanPlace(snapshot.Origin, snapshot.Accuracy, snapshot.FixTimestamp, DateTimeOffset.UtcNow)
                        ? "GPS cue support expired or inaccurate" : "tracking, generation or floor unqualified");
            return default;
        }
        if (!TryResolveDirection(snapshot, frame.Pose.PositionX, frame.Pose.PositionZ, out var direction))
        {
            System.Threading.Volatile.Write(ref lastRenderReason, "mapped target reached or unavailable");
            return default;
        }
        System.Threading.Volatile.Write(ref lastRenderReason, "qualified cue");
        return snapshot with { Direction = direction };
    }

    /// <summary>A mapped cue always points from this camera to its retained world target.</summary>
    public static bool TryResolveDirection(Snapshot snapshot, float cameraX, float cameraZ, out Vector2 direction)
    {
        direction = snapshot.Direction;
        if (snapshot.Owner.Role != "MAPPED_TARGET") return true;
        if (snapshot.WorldTarget is not { } target || !float.IsFinite(cameraX) || !float.IsFinite(cameraZ)) return false;
        var delta = target - new Vector2(cameraX, cameraZ);
        float length = delta.LengthSquared();
        if (!float.IsFinite(length) || length < 1) return false;
        direction = Vector2.Normalize(delta);
        return true;
    }

    public static bool CanRender(Snapshot snapshot, ARRenderGenerationToken generation,
        long groundReference, ARGroundTrust trust, bool trackedFloor,
        DateTimeOffset now, long monotonicNow)
    {
        long age = monotonicNow - snapshot.PublishedAt;
        float length = snapshot.Direction.LengthSquared();
        bool mappedTargetAvailable = snapshot.Owner.Role != "MAPPED_TARGET" ||
            snapshot.WorldTarget is { } target && float.IsFinite(target.X) && float.IsFinite(target.Y);
        return snapshot.Active && mappedTargetAvailable && trackedFloor && generation.IsValid &&
            snapshot.Generation == generation && snapshot.GroundReference > 0 &&
            snapshot.GroundReference == groundReference && trust != ARGroundTrust.None &&
            snapshot.GroundTrust == trust && age >= 0 && age <= MaximumAgeMilliseconds &&
            float.IsFinite(length) && MathF.Abs(length - 1) < 0.01f &&
            RouteStartupLocationPolicy.CanPlace(snapshot.Origin, snapshot.Accuracy, snapshot.FixTimestamp, now);
    }
}
