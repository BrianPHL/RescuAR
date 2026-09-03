using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Mathematics;
using RescuAR.Diagnostics;
using System;

namespace RescuAR.AR;

/// <summary>
/// Persistent ARCore -> Evergine spatial renderer.
///
/// ARCore remains the sole camera/spatial authority. Navigation route geometry
/// has its own independent bridge because an MLD response can arrive between
/// ARCore frames. Both are consumed only on the Evergine draw thread.
/// </summary>
public static class ARCameraSpatialController
{
    private const string RouteLogTag =
        "RescuAR-ARRoute";
    private static readonly object sync =
        new();

    private static Transform3D? cameraTransform;
    private static Camera3D? cameraComponent;
    private static Entity? targetEntity;
    private static Transform3D? targetTransform;

    private static Entity? routeEntity;
    private static Transform3D? routeTransform;

    /*
     * ArCoreService publishes the diagnostic capsule CENTER at
     * groundAnchorY + 0.25 m. The ribbon center should sit 0.015 m above the
     * floor, therefore:
     *
     *   -0.25 + 0.015 = -0.235 m
     */
    private const float RouteYOffsetFromPublishedAnchor =
        -0.235f;

    /*
     * V5 ROUTE ROOT LOCK
     *
     * ARCore may continue refining an Anchor's horizontal pose after the
     * Anchor has already become TRACKING. The route root must not copy that
     * live X/Z every frame, otherwise stationary guidance visibly drifts.
     *
     * Instead, X/Z are locked once for each applied route version. A new
     * route version represents an intentional navigation-placement event
     * (initial route, progress-window update, recovery rebase, reroute).
     */
    private static bool routeRootHorizontalLocked;

    private static float lockedRouteRootX;
    private static float lockedRouteRootZ;

    private static long lockedRouteVersion =
        -1;

    private static int lastLoggedAnchorDriftBucket =
        -1;

    private const float AnchorDriftLogStepMeters =
        0.25f;

    private static long appliedVersion =
        -1;

    private static bool initialized;

    private static bool? lastLoggedTrackingValid;
    private static bool? lastLoggedAnchorAvailable;
    private static bool? lastLoggedRouteGeometry;
    private static bool? lastLoggedRouteVisible;

    public static void Initialize(
        Entity cameraEntity,
        Entity capsuleEntity,
        Entity arRouteEntity)
    {
        ArgumentNullException.ThrowIfNull(
            cameraEntity);

        ArgumentNullException.ThrowIfNull(
            capsuleEntity);

        ArgumentNullException.ThrowIfNull(
            arRouteEntity);

        Transform3D? resolvedCameraTransform =
            cameraEntity.FindComponent<Transform3D>();

        Camera3D? resolvedCameraComponent =
            cameraEntity.FindComponent<Camera3D>();

        Transform3D? resolvedTargetTransform =
            capsuleEntity.FindComponent<Transform3D>();

        Transform3D? resolvedRouteTransform =
            arRouteEntity.FindComponent<Transform3D>();

        if (resolvedCameraTransform is null)
        {
            throw new InvalidOperationException(
                "Camera entity does not contain Transform3D.");
        }

        if (resolvedCameraComponent is null)
        {
            throw new InvalidOperationException(
                "Camera entity does not contain Camera3D.");
        }

        if (resolvedTargetTransform is null)
        {
            throw new InvalidOperationException(
                "Capsule entity does not contain Transform3D.");
        }

        if (resolvedRouteTransform is null)
        {
            throw new InvalidOperationException(
                "AR route root does not contain Transform3D.");
        }

        resolvedCameraComponent.NearPlane =
            0.1f;

        resolvedCameraComponent.FarPlane =
            1000.0f;

        resolvedCameraComponent.FrustumCullingEnabled =
            false;

        capsuleEntity.IsEnabled =
            false;

        arRouteEntity.IsEnabled =
            false;

        lock (sync)
        {
            cameraTransform =
                resolvedCameraTransform;

            cameraComponent =
                resolvedCameraComponent;

            targetEntity =
                capsuleEntity;

            targetTransform =
                resolvedTargetTransform;

            routeEntity =
                arRouteEntity;

            routeTransform =
                resolvedRouteTransform;

            appliedVersion =
                -1;

            routeRootHorizontalLocked =
                false;

            lockedRouteRootX =
                0.0f;

            lockedRouteRootZ =
                0.0f;

            lockedRouteVersion =
                -1;

            lastLoggedAnchorDriftBucket =
                -1;

            lastLoggedTrackingValid =
                null;

            lastLoggedAnchorAvailable =
                null;

            lastLoggedRouteGeometry =
                null;

            lastLoggedRouteVisible =
                null;

            initialized =
                true;
        }
    }

    /// <summary>
    /// Invalidates only the route-root placement state.
    ///
    /// CameraPage calls this when a genuinely new ARCore Session creates a new
    /// arbitrary world frame. Normal Camera-tab pause/resume intentionally
    /// keeps the lock.
    /// </summary>
    public static void ResetRouteRootLock(
        string reason)
    {
        lock (sync)
        {
            routeRootHorizontalLocked =
                false;

            lockedRouteRootX =
                0.0f;

            lockedRouteRootZ =
                0.0f;

            lockedRouteVersion =
                -1;

            lastLoggedAnchorDriftBucket =
                -1;
        }

        AndroidLog.Debug(
            RouteLogTag,
            "V5 route-root X/Z lock reset: " +
            (string.IsNullOrWhiteSpace(reason)
                ? "<unspecified>"
                : reason));
    }

    public static void ProcessDrawThreadWork()
    {
        Transform3D? camera;
        Camera3D? camera3D;
        Entity? capsule;
        Transform3D? capsuleTransform;
        Entity? route;
        Transform3D? routeRootTransform;

        lock (sync)
        {
            if (!initialized)
            {
                return;
            }

            camera =
                cameraTransform;

            camera3D =
                cameraComponent;

            capsule =
                targetEntity;

            capsuleTransform =
                targetTransform;

            route =
                routeEntity;

            routeRootTransform =
                routeTransform;
        }

        if (camera is null ||
            camera3D is null ||
            capsule is null ||
            capsuleTransform is null ||
            route is null ||
            routeRootTransform is null)
        {
            return;
        }

        /*
         * Route responses are independent from ARCore spatial-frame versions.
         * Process the route bridge every draw before any spatial early-return.
         */
        bool hasRouteGeometry =
            ARRouteRenderer.ProcessDrawThreadWork(
                route);

        long rendererRouteVersion =
            ARRouteRenderer.AppliedRouteVersion;

        ARCameraPoseBridge.SpatialSnapshot frame =
            ARCameraPoseBridge.CurrentFrame;

        bool trackingValid =
            frame.IsTracking &&
            frame.Pose.IsTracking;

        ARCameraPoseBridge.AnchorSnapshot anchor =
            frame.Anchor;

        /*
         * A route bridge Clear() and every usable Publish() increment the route
         * version. Any version change invalidates the previous X/Z lock.
         *
         * If the new version contains usable geometry, the first draw with a
         * valid tracked Anchor establishes the new horizontal lock.
         */
        if (rendererRouteVersion !=
            lockedRouteVersion)
        {
            lockedRouteVersion =
                rendererRouteVersion;

            routeRootHorizontalLocked =
                false;

            lastLoggedAnchorDriftBucket =
                -1;
        }

        if (hasRouteGeometry &&
            trackingValid &&
            anchor.IsAvailable &&
            !routeRootHorizontalLocked)
        {
            lockedRouteRootX =
                anchor.PositionX;

            lockedRouteRootZ =
                anchor.PositionZ;

            routeRootHorizontalLocked =
                true;

            lastLoggedAnchorDriftBucket =
                -1;

            AndroidLog.Debug(
                RouteLogTag,
                "V5 route-root X/Z LOCKED: " +
                $"routeVersion={rendererRouteVersion}, " +
                $"lockedRoot=(" +
                $"{lockedRouteRootX:F2}," +
                $"{lockedRouteRootZ:F2}), " +
                $"anchorY={anchor.PositionY:F2}");
        }

        /*
         * The diagnostic capsule continues to represent the LIVE ARCore
         * Anchor. It is allowed to move as ARCore refines that Anchor.
         */
        if (trackingValid &&
            anchor.IsAvailable)
        {
            capsuleTransform.Position =
                new Vector3(
                    anchor.PositionX,
                    anchor.PositionY,
                    anchor.PositionZ);

            if (!capsule.IsEnabled)
            {
                capsule.IsEnabled =
                    true;
            }
        }
        else if (!anchor.IsAvailable &&
                 capsule.IsEnabled)
        {
            capsule.IsEnabled =
                false;
        }

        /*
         * Route X/Z use the per-route-version lock. Y deliberately continues
         * following the live Anchor so small floor-height refinement remains
         * possible without horizontal route translation.
         */
        if (hasRouteGeometry &&
            trackingValid &&
            anchor.IsAvailable &&
            routeRootHorizontalLocked)
        {
            routeRootTransform.Position =
                new Vector3(
                    lockedRouteRootX,
                    anchor.PositionY +
                        RouteYOffsetFromPublishedAnchor,
                    lockedRouteRootZ);

            route.IsEnabled =
                true;

            float driftX =
                anchor.PositionX -
                lockedRouteRootX;

            float driftZ =
                anchor.PositionZ -
                lockedRouteRootZ;

            float horizontalAnchorDrift =
                MathF.Sqrt(
                    driftX * driftX +
                    driftZ * driftZ);

            int driftBucket =
                (int)MathF.Floor(
                    horizontalAnchorDrift /
                    AnchorDriftLogStepMeters);

            if (driftBucket >=
                    1 &&
                driftBucket !=
                    lastLoggedAnchorDriftBucket)
            {
                lastLoggedAnchorDriftBucket =
                    driftBucket;

                AndroidLog.Debug(
                    RouteLogTag,
                    "V5 route root remains X/Z locked while ARCore anchor " +
                    "refines: " +
                    $"routeVersion={rendererRouteVersion}, " +
                    $"lockedRoot=(" +
                    $"{lockedRouteRootX:F2}," +
                    $"{lockedRouteRootZ:F2}), " +
                    $"liveAnchor=(" +
                    $"{anchor.PositionX:F2}," +
                    $"{anchor.PositionZ:F2}), " +
                    $"anchorDrift={horizontalAnchorDrift:F2} m");
            }
        }
        else
        {
            /*
             * Keep the horizontal lock through temporary tracking/anchor loss,
             * but hide route guidance until spatial validity returns.
             */
            route.IsEnabled =
                false;
        }

        LogRouteStateIfChanged(
            trackingValid,
            anchor.IsAvailable,
            hasRouteGeometry,
            route.IsEnabled,
            frame.Version);

        /*
         * A route version can change between ARCore frames. All route
         * renderer/root-lock work above must occur before this early return.
         */
        if (frame.Version ==
            appliedVersion)
        {
            PublishTelemetry(
                frame.Version,
                frame.FrameTimestamp,
                camera,
                capsule,
                capsuleTransform);

            return;
        }

        appliedVersion =
            frame.Version;

        if (!trackingValid)
        {
            /*
             * Preserve the last valid camera transform/projection internally.
             * Route visibility was already suppressed above.
             */
            PublishTelemetry(
                frame.Version,
                frame.FrameTimestamp,
                camera,
                capsule,
                capsuleTransform);

            return;
        }

        ARCameraPoseBridge.ProjectionSnapshot projection =
            frame.Projection;

        if (projection.IsAvailable)
        {
            Matrix4x4 evergineProjection =
                new(
                    projection.M11,
                    projection.M21,
                    projection.M31,
                    projection.M41,

                    projection.M12,
                    projection.M22,
                    projection.M32,
                    projection.M42,

                    projection.M13,
                    projection.M23,
                    projection.M33,
                    projection.M43,

                    projection.M14,
                    projection.M24,
                    projection.M34,
                    projection.M44);

            if (camera3D.IsClipDepthZeroToOne)
            {
                Matrix4x4 depthConversion =
                    new(
                        1.0f, 0.0f, 0.0f, 0.0f,
                        0.0f, 1.0f, 0.0f, 0.0f,
                        0.0f, 0.0f, 0.5f, 0.0f,
                        0.0f, 0.0f, 0.5f, 1.0f);

                evergineProjection =
                    Matrix4x4.Multiply(
                        evergineProjection,
                        depthConversion);
            }

            if (camera3D.FlipYProjection)
            {
                evergineProjection.M12 *=
                    -1.0f;

                evergineProjection.M22 *=
                    -1.0f;

                evergineProjection.M32 *=
                    -1.0f;

                evergineProjection.M42 *=
                    -1.0f;
            }

            camera3D.SetCustomProjection(
                ref evergineProjection);
        }

        ARCameraPoseBridge.PublishProjectionTelemetry(
            camera3D.IsClipDepthZeroToOne,
            camera3D.FlipYProjection);

        ARCameraPoseBridge.PoseSnapshot pose =
            frame.Pose;

        camera.Position =
            new Vector3(
                pose.PositionX,
                pose.PositionY,
                pose.PositionZ);

        camera.Orientation =
            new Quaternion(
                pose.RotationX,
                pose.RotationY,
                pose.RotationZ,
                pose.RotationW);

        PublishTelemetry(
            frame.Version,
            frame.FrameTimestamp,
            camera,
            capsule,
            capsuleTransform);
    }

    private static void LogRouteStateIfChanged(
        bool trackingValid,
        bool anchorAvailable,
        bool hasRouteGeometry,
        bool routeVisible,
        long spatialVersion)
    {
        bool changed =
            lastLoggedTrackingValid !=
                trackingValid ||
            lastLoggedAnchorAvailable !=
                anchorAvailable ||
            lastLoggedRouteGeometry !=
                hasRouteGeometry ||
            lastLoggedRouteVisible !=
                routeVisible;

        if (!changed)
        {
            return;
        }

        lastLoggedTrackingValid =
            trackingValid;

        lastLoggedAnchorAvailable =
            anchorAvailable;

        lastLoggedRouteGeometry =
            hasRouteGeometry;

        lastLoggedRouteVisible =
            routeVisible;

        AndroidLog.Debug(
            RouteLogTag,
            "AR route visibility state: " +
            $"spatialVersion={spatialVersion}, " +
            $"tracking={trackingValid}, " +
            $"anchor={anchorAvailable}, " +
            $"geometry={hasRouteGeometry}, " +
            $"visible={routeVisible}");
    }

    private static void PublishTelemetry(
        long spatialVersion,
        long frameTimestamp,
        Transform3D camera,
        Entity capsule,
        Transform3D capsuleTransform)
    {
        if (!capsule.IsEnabled)
        {
            return;
        }

        Vector3 cameraPosition =
            camera.Position;

        Quaternion cameraRotation =
            camera.Orientation;

        Vector3 targetPosition =
            capsuleTransform.Position;

        float dx =
            targetPosition.X -
            cameraPosition.X;

        float dy =
            targetPosition.Y -
            cameraPosition.Y;

        float dz =
            targetPosition.Z -
            cameraPosition.Z;

        float distance =
            MathF.Sqrt(
                dx * dx +
                dy * dy +
                dz * dz);

        ARCameraPoseBridge.PublishEngineTelemetry(
            spatialVersion,
            frameTimestamp,
            cameraPosition.X,
            cameraPosition.Y,
            cameraPosition.Z,
            cameraRotation.X,
            cameraRotation.Y,
            cameraRotation.Z,
            cameraRotation.W,
            targetPosition.X,
            targetPosition.Y,
            targetPosition.Z,
            distance);
    }
}
