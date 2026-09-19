using Evergine.Common.Graphics;
using Evergine.Components.Graphics3D;
using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Framework.Graphics.Materials;
using Evergine.Mathematics;
using RescuAR.Diagnostics;
using RescuAR.Navigation.Projection;
using System;
using System.Collections.Generic;
using System.Threading;
using NumericsVector3 = System.Numerics.Vector3;

namespace RescuAR.AR;

/// <summary>
/// Draw-thread AR route renderer.
///
/// Create() allocates a small reusable pool of route segments once. Runtime
/// route updates only modify those already-created Evergine entities on the
/// draw thread. No HTTP/navigation worker thread touches the scene graph.
///
/// Route points are local X/Z coordinates whose root is positioned on the
/// current ARCore ground anchor by ARCameraSpatialController.
/// </summary>
public static class ARRouteRenderer
{
    private const string LogTag =
        "RescuAR-ARRoute";
    public const string RouteRootEntityName =
        "ARRouteRoot";

    private const float RouteWidthMeters =
        0.65f;

    private const float RouteThicknessMeters =
        0.04f;

    /*
     * Adjacent cube segments meet at different yaw angles around road bends.
     * A bounded longitudinal extension removes hairline gaps on nearly
     * straight joins. It tapers to zero as bends sharpen so overlapping cubes
     * cannot form a large wedge around a corner.
     */
    private const float MaximumJoinExtensionMeters =
        0.12f;

    private const double FullOverlapTurnDegrees =
        20.0;

    private const double NoOverlapTurnDegrees =
        60.0;

    private const float ArrowWingLengthMeters =
        0.82f;

    private const float ArrowHalfWidthMeters =
        0.46f;

    private const float ArrowWingWidthMeters =
        0.26f;

    private const int ArrowWingCount =
        2;

    private const int OccludedDepthSamplesRequiredToHide =
        3;

    private const int ClearDepthSamplesRequiredToShow =
        2;

    /*
     * The route visual has two bounded local horizons: a 40 m road-following
     * window during normal navigation and a short recovery window after
     * verified off-course detection. The renderer still reuses a fixed pool;
     * ordinary OSRM/A* pedestrian geometry is sparse enough that 64 segments
     * covers either local window without per-frame allocation.
     */
    private const int MaxRouteSegments =
        64;

    private static readonly object sync =
        new();

    private static Entity? activeRouteRoot;

    private static long boundGraphicsGeneration;

    private static SegmentSlot[] segmentSlots =
        [];

    private static SegmentSlot[] arrowSlots =
        [];

    private static long appliedRouteVersion =
        -1;

    private static int activeSegmentCount;

    private static int depthOcclusionRequested;

    public static bool DepthOcclusionRequested =>
        Volatile.Read(
            ref depthOcclusionRequested) ==
        1;

    public static int ActiveSegmentCount
    {
        get
        {
            lock (sync)
            {
                return activeSegmentCount;
            }
        }
    }

    public static long AppliedRouteVersion
    {
        get
        {
            lock (sync)
            {
                return appliedRouteVersion;
            }
        }
    }

    /// <summary>
    /// Creates an initially-empty route hierarchy using a clone of the
    /// capsule's already-proven material/render path.
    /// </summary>
    public static Entity Create(
        Material sourceMaterial)
    {
        ArgumentNullException.ThrowIfNull(
            sourceMaterial);

        Material routeMaterial =
            sourceMaterial.Clone();

        StandardMaterial standardMaterial =
            new(routeMaterial)
            {
                BaseColorLinear =
                    new LinearColor
                    {
                        A = 1.0f,
                        AsVector3 =
                            new Vector3(
                                0.0f,
                                0.82f,
                                0.92f)
                    },

                LightingEnabled =
                    false,

                IBLEnabled =
                    false,

                Metallic =
                    0.0f,

                Roughness =
                    1.0f
            };

        Entity routeRoot =
            new()
            {
                Name =
                    RouteRootEntityName,

                IsEnabled =
                    false
            };

        routeRoot.AddComponent(
            new Transform3D());

        SegmentSlot[] slots =
            new SegmentSlot[
                MaxRouteSegments];

        for (int i = 0;
             i < slots.Length;
             i++)
        {
            SegmentSlot slot =
                CreateSegmentSlot(
                    i,
                    standardMaterial.Material);

            slots[i] =
                slot;

            routeRoot.AddChild(
                slot.Entity);
        }

        SegmentSlot[] arrows =
            new SegmentSlot[
                ArrowWingCount];

        for (int i = 0;
             i < arrows.Length;
             i++)
        {
            SegmentSlot arrow =
                CreateSegmentSlot(
                    MaxRouteSegments +
                        i,
                    standardMaterial.Material);

            arrow.Entity.Name =
                $"ARRouteForwardArrow_{i}";

            arrows[i] =
                arrow;

            routeRoot.AddChild(
                arrow.Entity);
        }

        lock (sync)
        {
            activeRouteRoot =
                routeRoot;

            boundGraphicsGeneration =
                ARRenderGenerationBridge.Current.GraphicsGeneration;

            segmentSlots =
                slots;

            arrowSlots =
                arrows;

            appliedRouteVersion =
                -1;

            activeSegmentCount =
                0;

            Volatile.Write(
                ref depthOcclusionRequested,
                0);
        }

        AndroidLog.Debug(
            LogTag,
            $"AR route renderer created with {MaxRouteSegments} pooled route " +
            $"segments and a {ArrowWingCount}-wing forward arrowhead.");

        return routeRoot;
    }

    /// <summary>
    /// Applies the newest route snapshot to the pooled Evergine geometry.
    ///
    /// Must be called on the Evergine draw thread.
    ///
    /// Returns true when at least one valid segment is available.
    /// </summary>
    public static bool ProcessDrawThreadWork(
        Entity routeRoot)
    {
        ArgumentNullException.ThrowIfNull(
            routeRoot);

        SegmentSlot[] slots;
        SegmentSlot[] arrows;

        lock (sync)
        {
            if (!ReferenceEquals(
                    activeRouteRoot,
                    routeRoot) ||
                !ARRenderGenerationBridge.IsCurrentGraphics(
                    boundGraphicsGeneration))
            {
                return false;
            }

            slots =
                segmentSlots;

            arrows =
                arrowSlots;
        }

        ARRouteBridge.RouteSnapshot snapshot =
            ARRouteBridge.Current;

        if (!ARRenderGenerationBridge.IsCurrentSession(
                snapshot.Generation))
        {
            DisableAll(slots);
            DisableAll(arrows);
            activeSegmentCount = 0;
            return false;
        }

        if (snapshot.Version ==
            appliedRouteVersion)
        {
            return activeSegmentCount >
                0;
        }

        appliedRouteVersion =
            snapshot.Version;

        if (!snapshot.IsAvailable ||
            snapshot.Points.Count <
                2)
        {
            DisableAll(
                slots);

            DisableAll(
                arrows);

            activeSegmentCount =
                0;

            AndroidLog.Warn(
                LogTag,
                "Renderer received no usable route geometry: " +
                $"routeVersion={snapshot.Version}, " +
                $"available={snapshot.IsAvailable}, " +
                $"points={snapshot.Points.Count}");

            return false;
        }

        ARRouteGeometrySanitizer.GeometryPreparationResult prepared =
            ARRouteGeometrySanitizer.Prepare(
                snapshot.Points,
                slots.Length +
                    1);

        IReadOnlyList<ArHorizontalRoutePoint> renderPoints =
            prepared.Points;

        if (renderPoints.Count <
            2)
        {
            DisableAll(
                slots);

            DisableAll(
                arrows);

            activeSegmentCount =
                0;

            AndroidLog.Warn(
                LogTag,
                "Renderer rejected route geometry after removing invalid or tiny segments.");

            return false;
        }

        int requestedSegmentCount =
            Math.Min(
                renderPoints.Count -
                    1,
                slots.Length);

        int renderedSegmentCount =
            0;

        for (int i = 0;
             i < requestedSegmentCount;
             i++)
        {
            ArHorizontalRoutePoint start =
                renderPoints[i];

            ArHorizontalRoutePoint end =
                renderPoints[i + 1];

            if (TryApplySegment(
                    slots[i],
                    start,
                    end,
                    RouteWidthMeters,
                    GetJoinExtensionMeters(
                        renderPoints,
                        i),
                    GetJoinExtensionMeters(
                        renderPoints,
                        i +
                            1)))
            {
                renderedSegmentCount++;
            }
            else
            {
                slots[i].Entity.IsEnabled =
                    false;
            }
        }

        for (int i = requestedSegmentCount;
             i < slots.Length;
             i++)
        {
            slots[i].GeometryAvailable =
                false;

            slots[i].Entity.IsEnabled =
                false;
        }

        bool arrowVisible =
            ApplyForwardArrow(
                arrows,
                renderPoints);

        activeSegmentCount =
            renderedSegmentCount;

        AndroidLog.Debug(
            LogTag,
            "Renderer applied route snapshot: " +
            $"routeVersion={snapshot.Version}, " +
            $"inputPoints={snapshot.Points.Count}, " +
            $"preparedPoints={renderPoints.Count}, " +
            $"removedPoints={prepared.RemovedPointCount}, " +
            $"beveledCorners={prepared.BeveledCornerCount}, " +
            $"subdivisionPoints={prepared.InsertedSubdivisionPointCount}, " +
            $"truncated={prepared.WasTruncated}, " +
            $"requestedSegments={requestedSegmentCount}, " +
            $"activeSegments={activeSegmentCount}, " +
            $"forwardArrow={arrowVisible}");

        return activeSegmentCount >
            0;
    }

    public static void TeardownGraphicsGeneration(
        long graphicsGeneration)
    {
        lock (sync)
        {
            if (boundGraphicsGeneration != graphicsGeneration)
            {
                return;
            }

            DisableAll(segmentSlots);
            DisableAll(arrowSlots);

            if (activeRouteRoot is not null)
            {
                activeRouteRoot.IsEnabled = false;
            }

            activeRouteRoot = null;
            segmentSlots = [];
            arrowSlots = [];
            appliedRouteVersion = -1;
            activeSegmentCount = 0;
            boundGraphicsGeneration = 0;
            Volatile.Write(ref depthOcclusionRequested, 0);
        }
    }

    public static void SetDepthOcclusionRequested(
        bool requested)
    {
        Volatile.Write(
            ref depthOcclusionRequested,
            requested
                ? 1
                : 0);
    }

    /// <summary>
    /// Applies camera-relative width and conservative nearby depth occlusion
    /// to the already-pooled route geometry. Called once per tracked ARCore
    /// frame; it performs no scene allocation and samples one depth location
    /// per nearby segment.
    /// </summary>
    public static void ApplyCameraVisualPolicy(
        Vector3 routeRootWorldPosition,
        ARCameraPoseBridge.SpatialSnapshot frame)
    {
        if (!frame.IsTracking ||
            !frame.Pose.IsTracking)
        {
            return;
        }

        SegmentSlot[] slots;
        SegmentSlot[] arrows;

        lock (sync)
        {
            slots =
                segmentSlots;

            arrows =
                arrowSlots;
        }

        ARDepthOcclusionBridge.DepthSnapshot depth =
            ARFrameCoherencePolicy.GetDepthForSpatialFrame(
                frame);

        ApplyCameraVisualPolicy(
            slots,
            routeRootWorldPosition,
            frame,
            depth,
            protectNearCameraSegments: true);

        ApplyCameraVisualPolicy(
            arrows,
            routeRootWorldPosition,
            frame,
            depth,
            protectNearCameraSegments: false);
    }

    private static void ApplyCameraVisualPolicy(
        SegmentSlot[] slots,
        Vector3 routeRootWorldPosition,
        ARCameraPoseBridge.SpatialSnapshot frame,
        ARDepthOcclusionBridge.DepthSnapshot depth,
        bool protectNearCameraSegments)
    {
        float cameraX =
            frame.Pose.PositionX;

        float cameraZ =
            frame.Pose.PositionZ;

        for (int i = 0;
             i < slots.Length;
             i++)
        {
            SegmentSlot slot =
                slots[i];

            if (!slot.GeometryAvailable)
            {
                slot.Entity.IsEnabled =
                    false;

                continue;
            }

            float worldX =
                routeRootWorldPosition.X +
                slot.LocalMidpoint.X;

            float worldY =
                routeRootWorldPosition.Y +
                slot.LocalMidpoint.Y;

            float worldZ =
                routeRootWorldPosition.Z +
                slot.LocalMidpoint.Z;

            NumericsVector3 worldMidpoint =
                new(
                    worldX,
                    worldY,
                    worldZ);

            float deltaX =
                worldX -
                cameraX;

            float deltaZ =
                worldZ -
                cameraZ;

            float horizontalDistance =
                MathF.Sqrt(
                    deltaX * deltaX +
                    deltaZ * deltaZ);

            float routeWidth =
                ARRouteVisualPolicy.GetRouteWidthMeters(
                    horizontalDistance);

            float widthScale =
                routeWidth /
                RouteWidthMeters;

            slot.Transform.LocalScale =
                new Vector3(
                    MathF.Max(
                        0.18f,
                        slot.BaseWidthMeters *
                            widthScale),
                    slot.Transform.LocalScale.Y,
                    slot.Transform.LocalScale.Z);

            bool nearCameraExempt =
                protectNearCameraSegments &&
                (slot.Index ==
                    0 ||
                 horizontalDistance <=
                    ARRouteVisualPolicy.NearCameraOcclusionExemptionMeters);

            if (nearCameraExempt ||
                !ARRouteVisualPolicy.IsDepthSnapshotUsable(
                    depth,
                    frame.FrameTimestamp))
            {
                ResetOcclusionState(
                    slot,
                    visible: true);

                continue;
            }

            if (slot.LastOcclusionDepthVersion ==
                depth.Version)
            {
                slot.Entity.IsEnabled =
                    !slot.DepthOccluded;

                continue;
            }

            slot.LastOcclusionDepthVersion =
                depth.Version;

            NumericsVector3 worldStart =
                new(
                    routeRootWorldPosition.X +
                        slot.LocalStart.X,
                    routeRootWorldPosition.Y +
                        slot.LocalStart.Y,
                    routeRootWorldPosition.Z +
                        slot.LocalStart.Z);

            NumericsVector3 worldEnd =
                new(
                    routeRootWorldPosition.X +
                        slot.LocalEnd.X,
                    routeRootWorldPosition.Y +
                        slot.LocalEnd.Y,
                    routeRootWorldPosition.Z +
                        slot.LocalEnd.Z);

            bool occluded =
                horizontalDistance <=
                    ARRouteVisualPolicy.MaximumOcclusionDistanceMeters &&
                ARRouteVisualPolicy.IsWorldSegmentOccluded(
                    depth,
                    frame.FrameTimestamp,
                    worldStart,
                    worldMidpoint,
                    worldEnd);

            UpdateOcclusionState(
                slot,
                occluded,
                horizontalDistance,
                depth.Version);
        }
    }

    private static void UpdateOcclusionState(
        SegmentSlot slot,
        bool occluded,
        float horizontalDistance,
        long depthVersion)
    {
        bool previousState =
            slot.DepthOccluded;

        if (occluded)
        {
            slot.ConsecutiveOccludedSamples++;
            slot.ConsecutiveClearSamples =
                0;

            if (!slot.DepthOccluded &&
                slot.ConsecutiveOccludedSamples >=
                    OccludedDepthSamplesRequiredToHide)
            {
                slot.DepthOccluded =
                    true;
            }
        }
        else
        {
            slot.ConsecutiveClearSamples++;
            slot.ConsecutiveOccludedSamples =
                0;

            if (slot.DepthOccluded &&
                slot.ConsecutiveClearSamples >=
                    ClearDepthSamplesRequiredToShow)
            {
                slot.DepthOccluded =
                    false;
            }
        }

        slot.Entity.IsEnabled =
            !slot.DepthOccluded;

        if (previousState !=
            slot.DepthOccluded)
        {
            AndroidLog.Debug(
                LogTag,
                "ROUTE SEGMENT OCCLUSION CHANGED: " +
                $"segment={slot.Index}, " +
                $"hidden={slot.DepthOccluded}, " +
                $"distance={horizontalDistance:F2}m, " +
                $"depthVersion={depthVersion}, " +
                $"occludedSamples={slot.ConsecutiveOccludedSamples}, " +
                $"clearSamples={slot.ConsecutiveClearSamples}.");
        }
    }

    private static void ResetOcclusionState(
        SegmentSlot slot,
        bool visible)
    {
        slot.DepthOccluded =
            !visible;

        slot.ConsecutiveOccludedSamples =
            0;

        slot.ConsecutiveClearSamples =
            0;

        slot.LastOcclusionDepthVersion =
            long.MinValue;

        slot.Entity.IsEnabled =
            visible &&
            slot.GeometryAvailable;
    }

    private static bool TryApplySegment(
        SegmentSlot slot,
        ArHorizontalRoutePoint start,
        ArHorizontalRoutePoint end,
        float widthMeters = RouteWidthMeters,
        float startExtensionMeters = 0.0f,
        float endExtensionMeters = 0.0f)
    {
        slot.GeometryAvailable =
            false;

        Vector3 startPoint =
            new(
                start.X,
                0.0f,
                start.Z);

        Vector3 endPoint =
            new(
                end.X,
                0.0f,
                end.Z);

        Vector3 delta =
            endPoint -
            startPoint;

        float horizontalLength =
            MathF.Sqrt(
                delta.X * delta.X +
                delta.Z * delta.Z);

        if (horizontalLength <=
            0.05f)
        {
            return false;
        }

        float directionX =
            delta.X /
            horizontalLength;

        float directionZ =
            delta.Z /
            horizontalLength;

        float maximumExtension =
            MathF.Min(
                MaximumJoinExtensionMeters,
                horizontalLength *
                    0.25f);

        float boundedStartExtension =
            Math.Clamp(
                startExtensionMeters,
                0.0f,
                maximumExtension);

        float boundedEndExtension =
            Math.Clamp(
                endExtensionMeters,
                0.0f,
                maximumExtension);

        Vector3 horizontalDirection =
            new(
                directionX,
                0.0f,
                directionZ);

        startPoint -=
            horizontalDirection *
            boundedStartExtension;

        endPoint +=
            horizontalDirection *
            boundedEndExtension;

        delta =
            endPoint -
            startPoint;

        horizontalLength =
            MathF.Sqrt(
                delta.X * delta.X +
                delta.Z * delta.Z);

        Vector3 midpoint =
            (startPoint + endPoint) *
            0.5f;

        float yaw =
            MathF.Atan2(
                delta.X,
                delta.Z);

        slot.Transform.LocalPosition =
            midpoint;

        slot.Transform.LocalRotation =
            new Vector3(
                0.0f,
                yaw,
                0.0f);

        slot.Transform.LocalScale =
            new Vector3(
                widthMeters,
                RouteThicknessMeters,
                horizontalLength);

        slot.LocalMidpoint =
            midpoint;

        slot.LocalStart =
            startPoint;

        slot.LocalEnd =
            endPoint;

        slot.BaseWidthMeters =
            widthMeters;

        slot.GeometryAvailable =
            true;

        ResetOcclusionState(
            slot,
            visible: true);

        return true;
    }

    private static float GetJoinExtensionMeters(
        IReadOnlyList<ArHorizontalRoutePoint> points,
        int vertexIndex)
    {
        if (vertexIndex <=
                0 ||
            vertexIndex >=
                points.Count -
                    1)
        {
            return 0.0f;
        }

        ArHorizontalRoutePoint previous =
            points[vertexIndex - 1];

        ArHorizontalRoutePoint corner =
            points[vertexIndex];

        ArHorizontalRoutePoint next =
            points[vertexIndex + 1];

        float incomingX =
            corner.X -
            previous.X;

        float incomingZ =
            corner.Z -
            previous.Z;

        float outgoingX =
            next.X -
            corner.X;

        float outgoingZ =
            next.Z -
            corner.Z;

        float incomingLength =
            MathF.Sqrt(
                incomingX * incomingX +
                incomingZ * incomingZ);

        float outgoingLength =
            MathF.Sqrt(
                outgoingX * outgoingX +
                outgoingZ * outgoingZ);

        if (incomingLength <=
                0.05f ||
            outgoingLength <=
                0.05f)
        {
            return 0.0f;
        }

        double dot =
            incomingX /
                incomingLength *
                outgoingX /
                outgoingLength +
            incomingZ /
                incomingLength *
                outgoingZ /
                outgoingLength;

        double turnDegrees =
            Math.Acos(
                Math.Clamp(
                    dot,
                    -1.0,
                    1.0)) *
            180.0 /
            Math.PI;

        if (turnDegrees >=
            NoOverlapTurnDegrees)
        {
            return 0.0f;
        }

        double overlapScale =
            turnDegrees <=
                FullOverlapTurnDegrees
                ? 1.0
                : (NoOverlapTurnDegrees -
                   turnDegrees) /
                    (NoOverlapTurnDegrees -
                     FullOverlapTurnDegrees);

        float lengthLimit =
            MathF.Min(
                incomingLength,
                outgoingLength) *
            0.25f;

        return MathF.Min(
                MaximumJoinExtensionMeters,
                lengthLimit) *
            (float)overlapScale;
    }

    private static bool ApplyForwardArrow(
        SegmentSlot[] arrows,
        System.Collections.Generic.IReadOnlyList<ArHorizontalRoutePoint> points)
    {
        if (arrows.Length <
                ArrowWingCount ||
            points.Count <
                2)
        {
            DisableAll(
                arrows);

            return false;
        }

        ArHorizontalRoutePoint end =
            points[^1];

        ArHorizontalRoutePoint? previous =
            null;

        for (int i = points.Count -
                     2;
             i >=
             0;
             i--)
        {
            float deltaX =
                end.X -
                points[i].X;

            float deltaZ =
                end.Z -
                points[i].Z;

            float length =
                MathF.Sqrt(
                    deltaX * deltaX +
                    deltaZ * deltaZ);

            if (length >
                0.01f)
            {
                previous =
                    points[i];

                break;
            }
        }

        if (!previous.HasValue)
        {
            DisableAll(
                arrows);

            return false;
        }

        float routeDeltaX =
            end.X -
            previous.Value.X;

        float routeDeltaZ =
            end.Z -
            previous.Value.Z;

        float routeLength =
            MathF.Sqrt(
                routeDeltaX * routeDeltaX +
                routeDeltaZ * routeDeltaZ);

        if (routeLength <=
            0.01f)
        {
            DisableAll(
                arrows);

            return false;
        }

        float directionX =
            routeDeltaX /
            routeLength;

        float directionZ =
            routeDeltaZ /
            routeLength;

        float perpendicularX =
            -directionZ;

        float perpendicularZ =
            directionX;

        float arrowBaseX =
            end.X -
            directionX *
            ArrowWingLengthMeters;

        float arrowBaseZ =
            end.Z -
            directionZ *
            ArrowWingLengthMeters;

        ArHorizontalRoutePoint leftWing =
            new(
                arrowBaseX +
                    perpendicularX *
                    ArrowHalfWidthMeters,
                arrowBaseZ +
                    perpendicularZ *
                    ArrowHalfWidthMeters,
                end.DistanceFromWindowStartMeters);

        ArHorizontalRoutePoint rightWing =
            new(
                arrowBaseX -
                    perpendicularX *
                    ArrowHalfWidthMeters,
                arrowBaseZ -
                    perpendicularZ *
                    ArrowHalfWidthMeters,
                end.DistanceFromWindowStartMeters);

        bool leftVisible =
            TryApplySegment(
                arrows[0],
                leftWing,
                end,
                ArrowWingWidthMeters);

        bool rightVisible =
            TryApplySegment(
                arrows[1],
                rightWing,
                end,
                ArrowWingWidthMeters);

        if (!leftVisible)
        {
            arrows[0].Entity.IsEnabled =
                false;
        }

        if (!rightVisible)
        {
            arrows[1].Entity.IsEnabled =
                false;
        }

        return leftVisible &&
            rightVisible;
    }

    private static SegmentSlot CreateSegmentSlot(
        int index,
        Material material)
    {
        Transform3D transform =
            new()
            {
                LocalPosition =
                    new Vector3(
                        0.0f,
                        0.0f,
                        0.0f),

                LocalRotation =
                    new Vector3(
                        0.0f,
                        0.0f,
                        0.0f),

                LocalScale =
                    new Vector3(
                        RouteWidthMeters,
                        RouteThicknessMeters,
                        0.01f)
            };

        Entity entity =
            new Entity()
            {
                Name =
                    $"ARRouteSegment_{index}",

                IsEnabled =
                    false
            }
            .AddComponent(
                transform)
            .AddComponent(
                new MaterialComponent
                {
                    Material =
                        material,

                    UseCopy =
                        false,

                    AsignedTo =
                        "Default"
                })
            .AddComponent(
                new CubeMesh
                {
                    Size =
                        1.0f
                })
            .AddComponent(
                new MeshRenderer
                {
                    IsCullingEnabled =
                        false,

                    IsEnabled =
                        true
                });

        return new SegmentSlot(
            entity,
            transform);
    }

    private static void DisableAll(
        SegmentSlot[] slots)
    {
        for (int i = 0;
             i < slots.Length;
             i++)
        {
            slots[i].GeometryAvailable =
                false;

            ResetOcclusionState(
                slots[i],
                visible: false);
        }
    }

    private sealed class SegmentSlot
    {
        public SegmentSlot(
            Entity entity,
            Transform3D transform)
        {
            Entity =
                entity;

            Transform =
                transform;

            string name =
                entity.Name ??
                string.Empty;

            int separatorIndex =
                name.LastIndexOf(
                    '_');

            Index =
                separatorIndex >= 0 &&
                int.TryParse(
                    name[(separatorIndex + 1)..],
                    out int parsedIndex)
                    ? parsedIndex
                    : -1;
        }

        public int Index { get; }

        public Entity Entity { get; }

        public Transform3D Transform { get; }

        public Vector3 LocalMidpoint { get; set; }

        public Vector3 LocalStart { get; set; }

        public Vector3 LocalEnd { get; set; }

        public float BaseWidthMeters { get; set; } =
            RouteWidthMeters;

        public bool GeometryAvailable { get; set; }

        public bool DepthOccluded { get; set; }

        public int ConsecutiveOccludedSamples { get; set; }

        public int ConsecutiveClearSamples { get; set; }

        public long LastOcclusionDepthVersion { get; set; } =
            long.MinValue;
    }
}
