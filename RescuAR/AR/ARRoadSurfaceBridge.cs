using System;
using System.Numerics;
using System.Threading;

namespace RescuAR.AR;

/// <summary>On-device perception facts. The MLD owner alone grants guidance permission.</summary>
public static class ARRoadSurfaceBridge
{
    public sealed record Request(long Trip, string Geometry, Vector2 Direction, byte Label);
    public sealed record Observation(Request Owner, RoadSurfaceAlignment.RoadLine Line,
        ARRenderGenerationToken Generation, long Ground, long Epoch, long World, long FrameTimestamp,
        long PublishedAt, int Evidence, DateTimeOffset ObservedAt)
    {
        public bool IsFor(ARCameraPoseBridge.SpatialSnapshot frame) => frame.IsFresh && frame.IsTracking &&
            frame.Pose.IsTracking && frame.Anchor.IsVerified && Generation.SessionGeneration == frame.Generation.SessionGeneration &&
            Ground == frame.Anchor.ReferenceGeneration && Epoch == frame.TrackingEpoch && World == frame.WorldFrame &&
            Environment.TickCount64 - PublishedAt is >= 0 and <= 3000 && RoadSurfaceAlignment.IsQualified(Line);
    }

    private static readonly object sync = new();
    private static Request? request;
    private static Observation? current;
    private static Observation? candidate;
    private static long firstAt;
    private static string status = "Preparing road alignment";
    public static Request? CurrentRequest => Volatile.Read(ref request);
    public static Observation? Current => Volatile.Read(ref current);
    public static string Status => Volatile.Read(ref status);

    public static void SetRequest(Request? next)
    {
        lock (sync)
        {
            if (next?.Trip != request?.Trip || next?.Geometry != request?.Geometry || next?.Label != request?.Label)
            {
                current = null;
                candidate = null;
                firstAt = 0;
            }
            Volatile.Write(ref request, next);
        }
    }

    public static void SetStatus(string value) => Volatile.Write(ref status, value);

    public static void Observe(Request owner, RoadSurfaceAlignment.RoadLine line,
        ARCameraPoseBridge.SpatialSnapshot frame, long? monotonicNow = null)
    {
        lock (sync)
        {
            if (request != owner || !frame.IsFresh || !frame.IsTracking || !frame.Anchor.IsVerified ||
                !RoadSurfaceAlignment.IsQualified(line)) return;
            long now = monotonicNow ?? Environment.TickCount64;
            var old = candidate;
            if (old is not null && frame.FrameTimestamp <= old.FrameTimestamp) return;
            int evidence = old is not null && old.IsFor(frame) &&
                RoadSurfaceAlignment.Agrees(old.Line, line) ? old.Evidence + 1 : 1;
            if (evidence == 1) firstAt = now;
            // Independent camera frames, not steps, establish repeatability.
            bool qualified = evidence >= 3 && now - firstAt >= 750;
            candidate = new(owner, line, frame.Generation, frame.Anchor.ReferenceGeneration,
                frame.TrackingEpoch, frame.WorldFrame, frame.FrameTimestamp, now, evidence,
                DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64 - now));
            if (qualified) Volatile.Write(ref current, candidate);
            SetStatus(qualified ? "Road alignment observed" : "Checking visible road alignment");
        }
    }
}
