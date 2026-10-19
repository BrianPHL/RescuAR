using System;

namespace RescuAR.AR;

/// <summary>Publication inputs in session-local metres; contains no absolute location.</summary>
public readonly record struct ARRoutePlacement(
    ARRenderGenerationToken Generation, long GroundReference, long SpatialVersion,
    long PublishedAt, float AnchorX, float AnchorZ, float CameraX, float CameraZ,
    DateTimeOffset? ObservationTime, string Source, double YawDegrees,
    double GpsToRoadEast = 0, double GpsToRoadNorth = 0,
    float GpsToRoadX = 0, float GpsToRoadZ = 0,
    long PosePublishedAt = 0, long FrameTimestamp = 0,
    long TrackingEpoch = 0, long WorldFrame = 0,
    System.Numerics.Quaternion? AnchorRotation = null,
    long ObservationPoseFrame = 0, long ObservationPoseSkewMilliseconds = long.MaxValue,
    float ObservationCameraX = float.NaN, float ObservationCameraZ = float.NaN)
{
    public bool IsAvailable => Generation.IsValid && GroundReference > 0 && SpatialVersion > 0 &&
        float.IsFinite(AnchorX) && float.IsFinite(AnchorZ) &&
        float.IsFinite(CameraX) && float.IsFinite(CameraZ);

    public bool IsFor(ARCameraPoseBridge.SpatialSnapshot frame) => !IsAvailable ||
        Generation.SessionGeneration == frame.Generation.SessionGeneration &&
        GroundReference == frame.Anchor.ReferenceGeneration;

    public bool HasSameOrigin(ARRoutePlacement other) => IsAvailable == other.IsAvailable &&
        (!IsAvailable || Generation.SessionGeneration == other.Generation.SessionGeneration &&
            GroundReference == other.GroundReference && TrackingEpoch == other.TrackingEpoch && WorldFrame == other.WorldFrame &&
            AnchorX == other.AnchorX && AnchorZ == other.AnchorZ);
}
