using Evergine.Common.Graphics;
using Evergine.Components.Graphics3D;
using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Framework.Graphics.Materials;
using Evergine.Mathematics;
using RescuAR.Diagnostics;
using RescuAR.Navigation.Projection;
using System;

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
     * A small longitudinal overlap removes hairline gaps between those cubes
     * so the cyan polyline reads as one continuous solid route.
     */
    private const float RouteSegmentOverlapMeters =
        0.24f;

    private const float ArrowWingLengthMeters =
        0.82f;

    private const float ArrowHalfWidthMeters =
        0.46f;

    private const float ArrowWingWidthMeters =
        0.26f;

    private const int ArrowWingCount =
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

    private static SegmentSlot[] segmentSlots =
        [];

    private static SegmentSlot[] arrowSlots =
        [];

    private static long appliedRouteVersion =
        -1;

    private static int activeSegmentCount;

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

            segmentSlots =
                slots;

            arrowSlots =
                arrows;

            appliedRouteVersion =
                -1;

            activeSegmentCount =
                0;
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
                    routeRoot))
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

        int requestedSegmentCount =
            Math.Min(
                snapshot.Points.Count -
                    1,
                slots.Length);

        int renderedSegmentCount =
            0;

        for (int i = 0;
             i < requestedSegmentCount;
             i++)
        {
            ArHorizontalRoutePoint start =
                snapshot.Points[i];

            ArHorizontalRoutePoint end =
                snapshot.Points[i + 1];

            if (TryApplySegment(
                    slots[i],
                    start,
                    end))
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
            slots[i].Entity.IsEnabled =
                false;
        }

        bool arrowVisible =
            ApplyForwardArrow(
                arrows,
                snapshot.Points);

        activeSegmentCount =
            renderedSegmentCount;

        AndroidLog.Debug(
            LogTag,
            "Renderer applied route snapshot: " +
            $"routeVersion={snapshot.Version}, " +
            $"inputPoints={snapshot.Points.Count}, " +
            $"requestedSegments={requestedSegmentCount}, " +
            $"activeSegments={activeSegmentCount}, " +
            $"forwardArrow={arrowVisible}");

        return activeSegmentCount >
            0;
    }

    private static bool TryApplySegment(
        SegmentSlot slot,
        ArHorizontalRoutePoint start,
        ArHorizontalRoutePoint end,
        float widthMeters = RouteWidthMeters)
    {
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
            0.01f)
        {
            return false;
        }

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
                horizontalLength +
                    RouteSegmentOverlapMeters);

        slot.Entity.IsEnabled =
            true;

        return true;
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
            slots[i].Entity.IsEnabled =
                false;
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
        }

        public Entity Entity { get; }

        public Transform3D Transform { get; }
    }
}
