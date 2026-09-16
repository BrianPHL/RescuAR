using System.Threading;

namespace RescuAR.AR;

/// <summary>
/// Atomic handoff for ARCore spatial state.
///
/// Camera pose, projection, ground-anchor pose, tracking state, and ARCore
/// frame timestamp are published together as ONE immutable snapshot. Evergine
/// reads that snapshot once and applies all spatial values from the same
/// ARCore Session.Update() result.
///
/// Telemetry remains separate because it describes what Evergine actually
/// rendered and must never advance the AR spatial version.
/// </summary>
public static class ARCameraPoseBridge
{
    private static readonly object sync =
        new();

    private static SpatialSnapshot spatialSnapshot =
        SpatialSnapshot.Unavailable;

    private static EngineTelemetry engineTelemetry =
        EngineTelemetry.Unavailable;

    private static ProjectionTelemetry projectionTelemetry =
        ProjectionTelemetry.Unavailable;

    private static long version;

    public static long Version =>
        Interlocked.Read(
            ref version);

    /// <summary>
    /// Returns one coherent copy of the complete latest ARCore spatial state.
    /// </summary>
    public static SpatialSnapshot CurrentFrame
    {
        get
        {
            lock (sync)
            {
                return spatialSnapshot;
            }
        }
    }

    /*
     * Compatibility accessors retained for the existing diagnostics.
     * All of them are derived from the SAME SpatialSnapshot rather than
     * independent mutable bridge states.
     */
    public static PoseSnapshot Current =>
        CurrentFrame.Pose;

    public static AnchorSnapshot CurrentAnchor =>
        CurrentFrame.Anchor;

    public static ProjectionSnapshot CurrentProjection =>
        CurrentFrame.Projection;

    public static EngineTelemetry CurrentEngineTelemetry
    {
        get
        {
            lock (sync)
            {
                return engineTelemetry;
            }
        }
    }

    public static ProjectionTelemetry CurrentProjectionTelemetry
    {
        get
        {
            lock (sync)
            {
                return projectionTelemetry;
            }
        }
    }

    /// <summary>
    /// Publishes one complete ARCore-frame spatial state atomically.
    /// This is the only method that should advance Version during tracking.
    /// </summary>
    public static void PublishFrame(
        bool isTracking,
        string trackingFailureReason,
        float positionX,
        float positionY,
        float positionZ,
        float rotationX,
        float rotationY,
        float rotationZ,
        float rotationW,
        float[]? columnMajorProjection,
        float nearPlane,
        float farPlane,
        bool anchorAvailable,
        bool anchorIsProvisional,
        float anchorX,
        float anchorY,
        float anchorZ,
        long frameTimestamp)
    {
        ProjectionSnapshot projection =
            CreateProjectionSnapshot(
                columnMajorProjection,
                nearPlane,
                farPlane);

        PoseSnapshot pose =
            new(
                isTracking,
                positionX,
                positionY,
                positionZ,
                rotationX,
                rotationY,
                rotationZ,
                rotationW,
                frameTimestamp);

        AnchorSnapshot anchor =
            anchorAvailable
                ? new AnchorSnapshot(
                    true,
                    anchorIsProvisional,
                    anchorX,
                    anchorY,
                    anchorZ)
                : AnchorSnapshot.Unavailable;

        long nextVersion =
            Interlocked.Increment(
                ref version);

        SpatialSnapshot next =
            new(
                nextVersion,
                frameTimestamp,
                isTracking,
                trackingFailureReason ?? string.Empty,
                pose,
                projection,
                anchor);

        lock (sync)
        {
            spatialSnapshot =
                next;
        }
    }

    /// <summary>
    /// Publishes a non-tracking frame without inventing a new camera pose.
    /// Evergine will hold its last applied valid transform.
    /// </summary>
    public static void PublishTrackingUnavailable(
        string trackingFailureReason,
        long frameTimestamp)
    {
        SpatialSnapshot previous =
            CurrentFrame;

        PoseSnapshot unavailablePose =
            new(
                false,
                previous.Pose.PositionX,
                previous.Pose.PositionY,
                previous.Pose.PositionZ,
                previous.Pose.RotationX,
                previous.Pose.RotationY,
                previous.Pose.RotationZ,
                previous.Pose.RotationW,
                frameTimestamp);

        long nextVersion =
            Interlocked.Increment(
                ref version);

        SpatialSnapshot next =
            new(
                nextVersion,
                frameTimestamp,
                false,
                trackingFailureReason ?? string.Empty,
                unavailablePose,
                previous.Projection,
                previous.Anchor);

        lock (sync)
        {
            spatialSnapshot =
                next;
        }
    }

    public static void PublishProjectionTelemetry(
        bool isClipDepthZeroToOne,
        bool flipYProjection)
    {
        lock (sync)
        {
            projectionTelemetry =
                new ProjectionTelemetry(
                    true,
                    isClipDepthZeroToOne,
                    flipYProjection);
        }
    }

    public static void PublishEngineTelemetry(
        long appliedSpatialVersion,
        long appliedFrameTimestamp,
        float cameraX,
        float cameraY,
        float cameraZ,
        float rotationX,
        float rotationY,
        float rotationZ,
        float rotationW,
        float targetX,
        float targetY,
        float targetZ,
        float cameraToTargetDistance)
    {
        lock (sync)
        {
            engineTelemetry =
                new EngineTelemetry(
                    true,
                    appliedSpatialVersion,
                    appliedFrameTimestamp,
                    cameraX,
                    cameraY,
                    cameraZ,
                    rotationX,
                    rotationY,
                    rotationZ,
                    rotationW,
                    targetX,
                    targetY,
                    targetZ,
                    cameraToTargetDistance);
        }
    }

    public static void Clear()
    {
        lock (sync)
        {
            spatialSnapshot =
                SpatialSnapshot.Unavailable;

            engineTelemetry =
                EngineTelemetry.Unavailable;

            projectionTelemetry =
                ProjectionTelemetry.Unavailable;
        }

        Interlocked.Increment(
            ref version);
    }

    private static ProjectionSnapshot CreateProjectionSnapshot(
        float[]? columnMajorValues,
        float nearPlane,
        float farPlane)
    {
        if (columnMajorValues is null ||
            columnMajorValues.Length < 16)
        {
            return ProjectionSnapshot.Unavailable;
        }

        /*
         * ARCore supplies column-major storage. Store named mathematical
         * row/column entries so the Evergine consumer is storage-agnostic.
         */
        return new ProjectionSnapshot(
            true,
            columnMajorValues[0],
            columnMajorValues[4],
            columnMajorValues[8],
            columnMajorValues[12],
            columnMajorValues[1],
            columnMajorValues[5],
            columnMajorValues[9],
            columnMajorValues[13],
            columnMajorValues[2],
            columnMajorValues[6],
            columnMajorValues[10],
            columnMajorValues[14],
            columnMajorValues[3],
            columnMajorValues[7],
            columnMajorValues[11],
            columnMajorValues[15],
            nearPlane,
            farPlane);
    }

    public readonly struct SpatialSnapshot
    {
        public static SpatialSnapshot Unavailable =>
            new(
                -1,
                long.MinValue,
                false,
                string.Empty,
                PoseSnapshot.Unavailable,
                ProjectionSnapshot.Unavailable,
                AnchorSnapshot.Unavailable);

        public SpatialSnapshot(
            long version,
            long frameTimestamp,
            bool isTracking,
            string trackingFailureReason,
            PoseSnapshot pose,
            ProjectionSnapshot projection,
            AnchorSnapshot anchor)
        {
            Version = version;
            FrameTimestamp = frameTimestamp;
            IsTracking = isTracking;
            TrackingFailureReason = trackingFailureReason;
            Pose = pose;
            Projection = projection;
            Anchor = anchor;
        }

        public long Version { get; }
        public long FrameTimestamp { get; }
        public bool IsTracking { get; }
        public string TrackingFailureReason { get; }
        public PoseSnapshot Pose { get; }
        public ProjectionSnapshot Projection { get; }
        public AnchorSnapshot Anchor { get; }
    }

    public readonly struct PoseSnapshot
    {
        public static PoseSnapshot Unavailable =>
            new(
                false,
                0,
                0,
                0,
                0,
                0,
                0,
                1,
                long.MinValue);

        public PoseSnapshot(
            bool isTracking,
            float positionX,
            float positionY,
            float positionZ,
            float rotationX,
            float rotationY,
            float rotationZ,
            float rotationW,
            long timestamp)
        {
            IsTracking = isTracking;
            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
            RotationX = rotationX;
            RotationY = rotationY;
            RotationZ = rotationZ;
            RotationW = rotationW;
            Timestamp = timestamp;
        }

        public bool IsTracking { get; }
        public float PositionX { get; }
        public float PositionY { get; }
        public float PositionZ { get; }
        public float RotationX { get; }
        public float RotationY { get; }
        public float RotationZ { get; }
        public float RotationW { get; }
        public long Timestamp { get; }
    }

    public readonly struct AnchorSnapshot
    {
        public static AnchorSnapshot Unavailable =>
            new(
                false,
                false,
                0,
                0,
                0);

        public AnchorSnapshot(
            bool isAvailable,
            bool isProvisional,
            float positionX,
            float positionY,
            float positionZ)
        {
            IsAvailable = isAvailable;
            IsProvisional = isProvisional;
            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
        }

        public bool IsAvailable { get; }
        public bool IsProvisional { get; }
        public float PositionX { get; }
        public float PositionY { get; }
        public float PositionZ { get; }
    }

    public readonly struct ProjectionSnapshot
    {
        public static ProjectionSnapshot Unavailable =>
            new(
                false,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0,
                0);

        public ProjectionSnapshot(
            bool isAvailable,
            float m11,
            float m12,
            float m13,
            float m14,
            float m21,
            float m22,
            float m23,
            float m24,
            float m31,
            float m32,
            float m33,
            float m34,
            float m41,
            float m42,
            float m43,
            float m44,
            float nearPlane,
            float farPlane)
        {
            IsAvailable = isAvailable;
            M11 = m11;
            M12 = m12;
            M13 = m13;
            M14 = m14;
            M21 = m21;
            M22 = m22;
            M23 = m23;
            M24 = m24;
            M31 = m31;
            M32 = m32;
            M33 = m33;
            M34 = m34;
            M41 = m41;
            M42 = m42;
            M43 = m43;
            M44 = m44;
            NearPlane = nearPlane;
            FarPlane = farPlane;
        }

        public bool IsAvailable { get; }
        public float M11 { get; }
        public float M12 { get; }
        public float M13 { get; }
        public float M14 { get; }
        public float M21 { get; }
        public float M22 { get; }
        public float M23 { get; }
        public float M24 { get; }
        public float M31 { get; }
        public float M32 { get; }
        public float M33 { get; }
        public float M34 { get; }
        public float M41 { get; }
        public float M42 { get; }
        public float M43 { get; }
        public float M44 { get; }
        public float NearPlane { get; }
        public float FarPlane { get; }
    }

    public readonly struct ProjectionTelemetry
    {
        public static ProjectionTelemetry Unavailable =>
            new(
                false,
                false,
                false);

        public ProjectionTelemetry(
            bool isAvailable,
            bool isClipDepthZeroToOne,
            bool flipYProjection)
        {
            IsAvailable = isAvailable;
            IsClipDepthZeroToOne = isClipDepthZeroToOne;
            FlipYProjection = flipYProjection;
        }

        public bool IsAvailable { get; }
        public bool IsClipDepthZeroToOne { get; }
        public bool FlipYProjection { get; }
    }

    public readonly struct EngineTelemetry
    {
        public static EngineTelemetry Unavailable =>
            new(
                false,
                -1,
                long.MinValue,
                0,
                0,
                0,
                0,
                0,
                0,
                1,
                0,
                0,
                0,
                0);

        public EngineTelemetry(
            bool isAvailable,
            long appliedSpatialVersion,
            long appliedFrameTimestamp,
            float cameraX,
            float cameraY,
            float cameraZ,
            float rotationX,
            float rotationY,
            float rotationZ,
            float rotationW,
            float targetX,
            float targetY,
            float targetZ,
            float cameraToTargetDistance)
        {
            IsAvailable = isAvailable;
            AppliedSpatialVersion = appliedSpatialVersion;
            AppliedFrameTimestamp = appliedFrameTimestamp;
            CameraX = cameraX;
            CameraY = cameraY;
            CameraZ = cameraZ;
            RotationX = rotationX;
            RotationY = rotationY;
            RotationZ = rotationZ;
            RotationW = rotationW;
            TargetX = targetX;
            TargetY = targetY;
            TargetZ = targetZ;
            CameraToTargetDistance = cameraToTargetDistance;
        }

        public bool IsAvailable { get; }
        public long AppliedSpatialVersion { get; }
        public long AppliedFrameTimestamp { get; }
        public float CameraX { get; }
        public float CameraY { get; }
        public float CameraZ { get; }
        public float RotationX { get; }
        public float RotationY { get; }
        public float RotationZ { get; }
        public float RotationW { get; }
        public float TargetX { get; }
        public float TargetY { get; }
        public float TargetZ { get; }
        public float CameraToTargetDistance { get; }
    }
}
