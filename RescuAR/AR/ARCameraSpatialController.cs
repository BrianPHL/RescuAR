using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Mathematics;
using RescuAR.Diagnostics;
using RescuAR.Navigation.Projection;
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

    private const string ContinuityLogTag =
        "RescuAR-ARContinuity";
    private static readonly object sync =
        new();

    private static Transform3D? cameraTransform;
    private static Camera3D? cameraComponent;
    private static Entity? targetEntity;
    private static Transform3D? targetTransform;

    private static Entity? routeEntity;
    private static Transform3D? routeTransform;

    /*
     * Camera-module view gate.
     *
     * Route navigation state remains retained while the user switches to the
     * Camera module's 2D Map or Flood Depth sub-tabs, but the cyan AR route
     * itself must only render on the AR Camera sub-tab.
     *
     * MAUI only writes this flag. Evergine entities are still changed solely
     * on the draw thread inside ProcessDrawThreadWork().
     */
    private static bool routeRenderingEnabled =
        true;

    private static Entity? floodDepthEntity;
    private static Transform3D? floodDepthTransform;

    /*
     * AnchorSnapshot.PositionY is now the actual ARCore ground-anchor height.
     * Any visual offset belongs in this renderer so navigation/flood geometry
     * remains metric and the bridge never publishes a display-specific Y.
     */
    private const float RouteYOffsetAboveGroundMeters =
        0.015f;

    /*
     * The legacy capsule is retained only as a development ground marker and
     * flattened into a small disc-like shape. Its center sits slightly above
     * the floor so the marker does not z-fight with the camera image.
     */
    private const float GroundMarkerCenterOffsetMeters =
        0.015f;

    private static readonly Vector3 GroundMarkerScale =
        new(0.18f, 0.02f, 0.18f);

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

    /*
     * LARGE ARCORE WORLD-CORRECTION GUARD
     * -----------------------------------
     * Small live Anchor refinements are intentionally ignored by the V5
     * horizontal route-root lock because following every centimeter of
     * refinement would make the cyan guidance jitter.
     *
     * A multi-meter Anchor correction is different: it means ARCore has
     * materially refined/relocalized its world model while the route is still
     * locked to the old X/Z frame. In that case the existing local route
     * points were also calculated from the old camera-to-anchor relationship,
     * so merely moving the route root to the new Anchor is NOT sufficient.
     *
     * The draw thread therefore detects only sustained large corrections and
     * publishes a lightweight rebase request. CameraPage consumes that request
     * on its existing diagnostic tick and republishes the CURRENT route window
     * using the same route progress but the corrected camera/anchor relation.
     */
    private const float LargeRouteWorldCorrectionThresholdMeters =
        1.50f;

    private const int LargeRouteWorldCorrectionConfirmationMilliseconds =
        250;

    private static long largeRouteWorldCorrectionCandidateStartedTimestamp =
        long.MinValue;

    private static long largeRouteWorldCorrectionCandidateRouteVersion =
        -1;

    private static long lastWorldCorrectionRequestedRouteVersion =
        -1;

    private static long worldCorrectionRequestGeneration;

    private static RouteWorldCorrectionRequest pendingWorldCorrectionRequest =
        RouteWorldCorrectionRequest.Unavailable;

    /*
     * VISUAL CONTINUITY HOLD
     * ----------------------
     * ARCore PAUSED poses are not valid for spatial updates. Instead of
     * immediately removing user-facing AR content, remember whether each
     * feature has previously received a valid tracked placement. During a
     * temporary tracking/anchor interruption the Evergine camera already
     * holds its last valid transform; route/flood roots now do the same.
     *
     * No stale pose is ever used to update geometry. We simply keep the last
     * valid world transform visible until ARCore recovers, the content is
     * explicitly cleared, the route view gate is disabled, or a new Session
     * resets continuity state.
     */
    private static bool hasValidRouteSpatialPlacement;
    private static bool hasValidFloodDepthSpatialPlacement;

    private static bool visualContinuityHoldActive;
    private static long visualContinuityHoldStartedTimestamp =
        long.MinValue;

    private static long appliedVersion =
        -1;

    private static bool initialized;

    private static bool? lastLoggedTrackingValid;
    private static bool? lastLoggedAnchorAvailable;
    private static bool? lastLoggedRouteGeometry;
    private static bool? lastLoggedRouteVisible;
    private static bool? lastLoggedRouteGroundHeightPlausible;

    private static bool? lastLoggedFloodGeometry;
    private static bool? lastLoggedFloodVisible;

    private const int FloodMetricTelemetryIntervalMilliseconds =
        1000;

    private static long lastFloodMetricTelemetryTimestamp =
        long.MinValue;

    /// <summary>
    /// Latest sustained multi-meter ARCore world correction that requires the
    /// active navigation route window to be republished from CameraPage.
    ///
    /// This is a request/acknowledgement bridge only. Evergine scene objects
    /// remain draw-thread owned and CameraPage never mutates them directly.
    /// </summary>
    public static RouteWorldCorrectionRequest CurrentRouteWorldCorrectionRequest
    {
        get
        {
            lock (sync)
            {
                return pendingWorldCorrectionRequest;
            }
        }
    }

    public static void AcknowledgeRouteWorldCorrection(
        long requestGeneration,
        string reason)
    {
        bool acknowledged =
            false;

        lock (sync)
        {
            if (pendingWorldCorrectionRequest.IsPending &&
                pendingWorldCorrectionRequest.Generation ==
                    requestGeneration)
            {
                pendingWorldCorrectionRequest =
                    RouteWorldCorrectionRequest.Unavailable;

                acknowledged =
                    true;
            }
        }

        if (acknowledged)
        {
            AndroidLog.Debug(
                RouteLogTag,
                "ARCore world-correction route rebase request acknowledged: " +
                $"generation={requestGeneration}, " +
                $"reason='{(string.IsNullOrWhiteSpace(reason) ? "<unspecified>" : reason)}'.");
        }
    }

    public static void Initialize(
        Entity cameraEntity,
        Entity capsuleEntity,
        Entity arRouteEntity,
        Entity arFloodDepthEntity)
    {
        ArgumentNullException.ThrowIfNull(
            cameraEntity);

        ArgumentNullException.ThrowIfNull(
            capsuleEntity);

        ArgumentNullException.ThrowIfNull(
            arRouteEntity);

        ArgumentNullException.ThrowIfNull(
            arFloodDepthEntity);

        Transform3D? resolvedCameraTransform =
            cameraEntity.FindComponent<Transform3D>();

        Camera3D? resolvedCameraComponent =
            cameraEntity.FindComponent<Camera3D>();

        Transform3D? resolvedTargetTransform =
            capsuleEntity.FindComponent<Transform3D>();

        Transform3D? resolvedRouteTransform =
            arRouteEntity.FindComponent<Transform3D>();

        Transform3D? resolvedFloodDepthTransform =
            arFloodDepthEntity.FindComponent<Transform3D>();

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

        if (resolvedFloodDepthTransform is null)
        {
            throw new InvalidOperationException(
                "AR flood-depth root does not contain Transform3D.");
        }

        resolvedCameraComponent.NearPlane =
            0.1f;

        resolvedCameraComponent.FarPlane =
            1000.0f;

        resolvedCameraComponent.FrustumCullingEnabled =
            false;

        // Convert the old tall capsule into a compact floor diagnostic marker.
        resolvedTargetTransform.LocalScale =
            GroundMarkerScale;

        capsuleEntity.IsEnabled =
            false;

        arRouteEntity.IsEnabled =
            false;

        arFloodDepthEntity.IsEnabled =
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

            routeRenderingEnabled =
                true;

            floodDepthEntity =
                arFloodDepthEntity;

            floodDepthTransform =
                resolvedFloodDepthTransform;

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

            largeRouteWorldCorrectionCandidateStartedTimestamp =
                long.MinValue;

            largeRouteWorldCorrectionCandidateRouteVersion =
                -1;

            lastWorldCorrectionRequestedRouteVersion =
                -1;

            worldCorrectionRequestGeneration =
                0;

            pendingWorldCorrectionRequest =
                RouteWorldCorrectionRequest.Unavailable;

            lastLoggedTrackingValid =
                null;

            lastLoggedAnchorAvailable =
                null;

            lastLoggedRouteGeometry =
                null;

            lastLoggedRouteVisible =
                null;

            lastLoggedRouteGroundHeightPlausible =
                null;

            lastLoggedFloodGeometry =
                null;

            lastLoggedFloodVisible =
                null;

            lastFloodMetricTelemetryTimestamp =
                long.MinValue;

            hasValidRouteSpatialPlacement =
                false;

            hasValidFloodDepthSpatialPlacement =
                false;

            visualContinuityHoldActive =
                false;

            visualContinuityHoldStartedTimestamp =
                long.MinValue;

            initialized =
                true;
        }
    }

    public static void SetRouteRenderingEnabled(
        bool enabled,
        string reason)
    {
        lock (sync)
        {
            routeRenderingEnabled =
                enabled;
        }

        AndroidLog.Debug(
            RouteLogTag,
            "AR route rendering gate changed: " +
            $"enabled={enabled}, " +
            $"reason='{(string.IsNullOrWhiteSpace(reason) ? "<unspecified>" : reason)}'.");
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

            largeRouteWorldCorrectionCandidateStartedTimestamp =
                long.MinValue;

            largeRouteWorldCorrectionCandidateRouteVersion =
                -1;

            lastWorldCorrectionRequestedRouteVersion =
                -1;

            pendingWorldCorrectionRequest =
                RouteWorldCorrectionRequest.Unavailable;

            /*
             * ResetRouteRootLock is invoked when a genuinely new ARCore
             * Session/world frame is created. Never carry last-world visual
             * placements across that boundary.
             */
            hasValidRouteSpatialPlacement =
                false;

            hasValidFloodDepthSpatialPlacement =
                false;

            visualContinuityHoldActive =
                false;

            visualContinuityHoldStartedTimestamp =
                long.MinValue;

            lastFloodMetricTelemetryTimestamp =
                long.MinValue;

            lastLoggedRouteGroundHeightPlausible =
                null;
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
        bool routeRenderingAllowed;

        Entity? floodDepthRoot;
        Transform3D? floodDepthRootTransform;

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

            routeRenderingAllowed =
                routeRenderingEnabled;

            floodDepthRoot =
                floodDepthEntity;

            floodDepthRootTransform =
                floodDepthTransform;
        }

        if (camera is null ||
            camera3D is null ||
            capsule is null ||
            capsuleTransform is null ||
            route is null ||
            routeRootTransform is null ||
            floodDepthRoot is null ||
            floodDepthRootTransform is null)
        {
            return;
        }

        /*
         * Route responses and flood-depth updates are independent from ARCore
         * spatial-frame versions. Process both bridges every draw before any
         * spatial early-return.
         */
        bool hasFloodDepthGeometry =
            ARFloodDepthRenderer.ProcessDrawThreadWork(
                floodDepthRoot);

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

        float cameraHeightAboveGroundMeters =
            float.NaN;

        bool routeGroundHeightPlausible =
            trackingValid &&
            anchor.IsAvailable &&
            LocalArNavigationPolicy
                .IsCameraHeightAboveGroundPlausible(
                    frame.Pose.PositionY,
                    anchor.PositionY,
                    out cameraHeightAboveGroundMeters);

        LogRouteGroundHeightStateIfChanged(
            trackingValid,
            anchor,
            routeGroundHeightPlausible,
            frame.Pose.PositionY,
            cameraHeightAboveGroundMeters,
            frame.Version);

        /*
         * GROUND MARKER / FLOOD BASELINE
         * ------------------------------
         * Apply the compact ground marker before flood placement. The flood
         * root then derives its baseline directly from that SAME marker
         * transform rather than independently reconstructing the ground pose.
         *
         * This makes the visual contract explicit:
         *
         *   marker center = floor + 0.015 m
         *   flood local Y=0 = marker center - 0.015 m = floor
         *
         * Therefore a selected 0.10 m flood surface is exactly 10 cm above the
         * same floor represented by the marker.
         */
        if (trackingValid &&
            anchor.IsAvailable)
        {
            capsuleTransform.Position =
                new Vector3(
                    anchor.PositionX,
                    anchor.PositionY +
                        GroundMarkerCenterOffsetMeters,
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
         * FLOOD DEPTH AR SPACE
         * --------------------
         * Flood depth is independent from navigation-route placement and is
         * rooted on the exact same detected ground used by the marker above.
         *
         * The renderer owns only LOCAL metric geometry:
         *   local Y=0      -> floor
         *   local Y=depth  -> water surface
         *
         * The root transform below owns the world-space floor placement.
         */
        bool floodDepthHeldFromLastValidPlacement =
            false;

        if (hasFloodDepthGeometry &&
            trackingValid &&
            anchor.IsAvailable)
        {
            Vector3 markerPosition =
                capsuleTransform.Position;

            floodDepthRootTransform.Position =
                new Vector3(
                    markerPosition.X,
                    markerPosition.Y -
                        GroundMarkerCenterOffsetMeters,
                    markerPosition.Z);

            floodDepthRoot.IsEnabled =
                true;

            hasValidFloodDepthSpatialPlacement =
                true;
        }
        else if (!hasFloodDepthGeometry)
        {
            /*
             * Explicit bridge clear / Flood Depth mode exit wins over visual
             * continuity. Do not resurrect geometry the feature no longer
             * considers active.
             */
            floodDepthRoot.IsEnabled =
                false;

            hasValidFloodDepthSpatialPlacement =
                false;
        }
        else if (hasValidFloodDepthSpatialPlacement)
        {
            /*
             * Freeze the last valid AR-space water placement. The camera is
             * frozen to its own last valid ARCore pose by the existing early
             * return below, so the scene does not blink out during brief
             * relocalization.
             */
            floodDepthRoot.IsEnabled =
                true;

            floodDepthHeldFromLastValidPlacement =
                true;
        }
        else
        {
            floodDepthRoot.IsEnabled =
                false;
        }

        LogFloodDepthStateIfChanged(
            trackingValid,
            anchor.IsAvailable,
            hasFloodDepthGeometry,
            floodDepthRoot.IsEnabled,
            frame.Version,
            floodDepthRootTransform.Position.Y);

        LogFloodMetricTelemetryIfNeeded(
            frame,
            floodDepthRoot,
            floodDepthRootTransform);

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

            largeRouteWorldCorrectionCandidateStartedTimestamp =
                long.MinValue;

            largeRouteWorldCorrectionCandidateRouteVersion =
                -1;

            lastWorldCorrectionRequestedRouteVersion =
                -1;

            lock (sync)
            {
                /*
                 * Any newly published route version is already based on the
                 * newest CameraPage spatial snapshot. A request tied to the
                 * previous geometry is therefore obsolete.
                 */
                if (pendingWorldCorrectionRequest.IsPending &&
                    pendingWorldCorrectionRequest.RouteVersion !=
                        rendererRouteVersion)
                {
                    pendingWorldCorrectionRequest =
                        RouteWorldCorrectionRequest.Unavailable;
                }
            }
        }

        if (hasRouteGeometry &&
            trackingValid &&
            anchor.IsAvailable &&
            routeGroundHeightPlausible &&
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
         * Route X/Z use the per-route-version lock. Y deliberately continues
         * following the live Anchor so small floor-height refinement remains
         * possible without horizontal route translation.
         *
         * Monitor horizontal Anchor refinement even while the AR Camera route
         * is temporarily hidden by the Camera-module view gate. Otherwise a
         * large ARCore world correction could occur on another sub-tab and
         * leave stale route placement waiting when the user returns.
         */
        if (hasRouteGeometry &&
            trackingValid &&
            anchor.IsAvailable &&
            routeGroundHeightPlausible &&
            routeRootHorizontalLocked)
        {
            MonitorRouteAnchorRefinement(
                rendererRouteVersion,
                frame.Version,
                anchor);
        }

        if (hasRouteGeometry &&
            trackingValid &&
            anchor.IsAvailable &&
            routeGroundHeightPlausible &&
            routeRootHorizontalLocked &&
            routeRenderingAllowed)
        {
            routeRootTransform.Position =
                new Vector3(
                    lockedRouteRootX,
                    anchor.PositionY +
                        RouteYOffsetAboveGroundMeters,
                    lockedRouteRootZ);

            route.IsEnabled =
                true;

            hasValidRouteSpatialPlacement =
                true;
        }
        else
        {
            bool routeExplicitlySuppressed =
                !routeRenderingAllowed;

            if (!hasRouteGeometry)
            {
                /*
                 * Route bridge Clear() is authoritative. A continuity hold
                 * must never keep an intentionally removed route on screen.
                 */
                route.IsEnabled =
                    false;

                hasValidRouteSpatialPlacement =
                    false;
            }
            else if (routeExplicitlySuppressed)
            {
                /*
                 * Camera sub-tab selection is also authoritative. Preserve the
                 * remembered placement so AR Camera can return seamlessly, but
                 * do not render the route in 2D Map/Flood Depth modes.
                 */
                route.IsEnabled =
                    false;
            }
            else if (trackingValid &&
                     anchor.IsAvailable &&
                     !routeGroundHeightPlausible)
            {
                /*
                 * An implausible camera-to-ground height is a safety failure,
                 * not a temporary tracking interruption. Never preserve the
                 * previous cyan route at head height while anchor recovery
                 * searches for a new floor reference.
                 */
                route.IsEnabled =
                    false;

                hasValidRouteSpatialPlacement =
                    false;
            }
            else if (hasValidRouteSpatialPlacement)
            {
                /*
                 * Temporary camera/anchor loss: keep the route at its last
                 * valid root transform instead of making it disappear. No
                 * stale ARCore pose is applied.
                 */
                route.IsEnabled =
                    true;
            }
            else
            {
                route.IsEnabled =
                    false;
            }
        }

        bool routeHeldFromLastValidPlacement =
            route.IsEnabled &&
            hasRouteGeometry &&
            routeRenderingAllowed &&
            hasValidRouteSpatialPlacement &&
            (!trackingValid || !anchor.IsAvailable);

        LogVisualContinuityHoldIfChanged(
            trackingValid,
            anchor.IsAvailable,
            routeHeldFromLastValidPlacement,
            floodDepthHeldFromLastValidPlacement,
            frame.TrackingFailureReason,
            frame.Version);

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
             * User-facing route/flood content may remain visible at its last
             * valid spatial placement until tracking returns.
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

    private static void MonitorRouteAnchorRefinement(
        long routeVersion,
        long spatialVersion,
        ARCameraPoseBridge.AnchorSnapshot anchor)
    {
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
                $"routeVersion={routeVersion}, " +
                $"lockedRoot=(" +
                $"{lockedRouteRootX:F2}," +
                $"{lockedRouteRootZ:F2}), " +
                $"liveAnchor=(" +
                $"{anchor.PositionX:F2}," +
                $"{anchor.PositionZ:F2}), " +
                $"anchorDrift={horizontalAnchorDrift:F2} m");
        }

        if (horizontalAnchorDrift <
            LargeRouteWorldCorrectionThresholdMeters)
        {
            largeRouteWorldCorrectionCandidateStartedTimestamp =
                long.MinValue;

            largeRouteWorldCorrectionCandidateRouteVersion =
                -1;

            return;
        }

        long now =
            Environment.TickCount64;

        if (largeRouteWorldCorrectionCandidateRouteVersion !=
                routeVersion ||
            largeRouteWorldCorrectionCandidateStartedTimestamp ==
                long.MinValue)
        {
            largeRouteWorldCorrectionCandidateRouteVersion =
                routeVersion;

            largeRouteWorldCorrectionCandidateStartedTimestamp =
                now;

            AndroidLog.Warn(
                RouteLogTag,
                "Large ARCore anchor refinement candidate detected: " +
                $"routeVersion={routeVersion}, " +
                $"spatialVersion={spatialVersion}, " +
                $"anchorDrift={horizontalAnchorDrift:F2} m. " +
                $"Confirming for {LargeRouteWorldCorrectionConfirmationMilliseconds} ms " +
                "before requesting a route-window rebase.");

            return;
        }

        if (lastWorldCorrectionRequestedRouteVersion ==
                routeVersion ||
            now -
                largeRouteWorldCorrectionCandidateStartedTimestamp <
                LargeRouteWorldCorrectionConfirmationMilliseconds)
        {
            return;
        }

        long requestGeneration;

        lock (sync)
        {
            worldCorrectionRequestGeneration++;

            requestGeneration =
                worldCorrectionRequestGeneration;

            pendingWorldCorrectionRequest =
                new RouteWorldCorrectionRequest(
                    requestGeneration,
                    true,
                    routeVersion,
                    spatialVersion,
                    horizontalAnchorDrift,
                    lockedRouteRootX,
                    lockedRouteRootZ,
                    anchor.PositionX,
                    anchor.PositionZ);
        }

        lastWorldCorrectionRequestedRouteVersion =
            routeVersion;

        AndroidLog.Warn(
            RouteLogTag,
            "LARGE ARCORE WORLD CORRECTION CONFIRMED: " +
            $"requestGeneration={requestGeneration}, " +
            $"routeVersion={routeVersion}, " +
            $"spatialVersion={spatialVersion}, " +
            $"lockedRoot=({lockedRouteRootX:F2},{lockedRouteRootZ:F2}), " +
            $"liveAnchor=({anchor.PositionX:F2},{anchor.PositionZ:F2}), " +
            $"anchorDrift={horizontalAnchorDrift:F2} m. " +
            "Requesting immediate current-window republish; the existing route " +
            "remains visible until the replacement version is ready.");
    }

    public readonly struct RouteWorldCorrectionRequest
    {
        public static RouteWorldCorrectionRequest Unavailable =>
            new(
                -1,
                false,
                -1,
                -1,
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                0.0f);

        public RouteWorldCorrectionRequest(
            long generation,
            bool isPending,
            long routeVersion,
            long spatialVersion,
            float anchorDriftMeters,
            float lockedRootX,
            float lockedRootZ,
            float liveAnchorX,
            float liveAnchorZ)
        {
            Generation =
                generation;

            IsPending =
                isPending;

            RouteVersion =
                routeVersion;

            SpatialVersion =
                spatialVersion;

            AnchorDriftMeters =
                anchorDriftMeters;

            LockedRootX =
                lockedRootX;

            LockedRootZ =
                lockedRootZ;

            LiveAnchorX =
                liveAnchorX;

            LiveAnchorZ =
                liveAnchorZ;
        }

        public long Generation { get; }
        public bool IsPending { get; }
        public long RouteVersion { get; }
        public long SpatialVersion { get; }
        public float AnchorDriftMeters { get; }
        public float LockedRootX { get; }
        public float LockedRootZ { get; }
        public float LiveAnchorX { get; }
        public float LiveAnchorZ { get; }
    }

    private static void LogVisualContinuityHoldIfChanged(
        bool trackingValid,
        bool anchorAvailable,
        bool routeHeld,
        bool floodHeld,
        string trackingFailureReason,
        long spatialVersion)
    {
        bool holdActive =
            routeHeld ||
            floodHeld;

        if (holdActive ==
            visualContinuityHoldActive)
        {
            return;
        }

        long now =
            Environment.TickCount64;

        if (holdActive)
        {
            visualContinuityHoldActive =
                true;

            visualContinuityHoldStartedTimestamp =
                now;

            AndroidLog.Warn(
                ContinuityLogTag,
                "VISUAL HOLD ENTERED: keeping last valid AR placement " +
                "visible while live spatial updates are unavailable. " +
                $"spatialVersion={spatialVersion}, " +
                $"tracking={trackingValid}, " +
                $"anchor={anchorAvailable}, " +
                $"routeHeld={routeHeld}, " +
                $"floodHeld={floodHeld}, " +
                $"failure='{(string.IsNullOrWhiteSpace(trackingFailureReason) ? "<none>" : trackingFailureReason)}'. " +
                "Geometry is frozen; it is not being updated from a PAUSED pose.");

            return;
        }

        long holdDuration =
            visualContinuityHoldStartedTimestamp ==
                long.MinValue
                ? 0
                : Math.Max(
                    0,
                    now -
                    visualContinuityHoldStartedTimestamp);

        visualContinuityHoldActive =
            false;

        visualContinuityHoldStartedTimestamp =
            long.MinValue;

        AndroidLog.Debug(
            ContinuityLogTag,
            "VISUAL HOLD EXITED: live ARCore spatial placement resumed or " +
            "content was explicitly hidden/cleared. " +
            $"spatialVersion={spatialVersion}, " +
            $"tracking={trackingValid}, " +
            $"anchor={anchorAvailable}, " +
            $"heldFor={holdDuration}ms.");
    }

    private static void LogFloodMetricTelemetryIfNeeded(
        ARCameraPoseBridge.SpatialSnapshot frame,
        Entity floodDepthRoot,
        Transform3D floodDepthRootTransform)
    {
        if (!floodDepthRoot.IsEnabled ||
            !frame.IsTracking ||
            !frame.Pose.IsTracking)
        {
            return;
        }

        long now =
            Environment.TickCount64;

        if (lastFloodMetricTelemetryTimestamp !=
                long.MinValue &&
            now -
                lastFloodMetricTelemetryTimestamp <
                FloodMetricTelemetryIntervalMilliseconds)
        {
            return;
        }

        lastFloodMetricTelemetryTimestamp =
            now;

        float groundWorldY =
            floodDepthRootTransform.Position.Y;

        float depth =
            ARFloodDepthRenderer.AppliedDepthMeters;

        float surfaceWorldY =
            groundWorldY +
            depth;

        float cameraWorldY =
            frame.Pose.PositionY;

        float cameraHeightAboveGround =
            cameraWorldY -
            groundWorldY;

        AndroidLog.Debug(
            "RescuAR-FloodDepth",
            "AR FLOOD WORLD LOCK: " +
            $"spatialVersion={frame.Version}, " +
            "baselineSource=GroundMarker, " +
            $"cameraWorldY={cameraWorldY:F3} m, " +
            $"groundWorldY={groundWorldY:F3} m, " +
            $"cameraHeightAboveGround={cameraHeightAboveGround:F3} m, " +
            $"depth={depth:F2} m, " +
            $"surfaceWorldY={surfaceWorldY:F3} m.");
    }

    private static void LogFloodDepthStateIfChanged(
        bool trackingValid,
        bool anchorAvailable,
        bool hasFloodGeometry,
        bool floodVisible,
        long spatialVersion,
        float groundWorldY)
    {
        bool changed =
            lastLoggedTrackingValid !=
                trackingValid ||
            lastLoggedAnchorAvailable !=
                anchorAvailable ||
            lastLoggedFloodGeometry !=
                hasFloodGeometry ||
            lastLoggedFloodVisible !=
                floodVisible;

        if (!changed)
        {
            return;
        }

        lastLoggedFloodGeometry =
            hasFloodGeometry;

        lastLoggedFloodVisible =
            floodVisible;

        AndroidLog.Debug(
            "RescuAR-FloodDepth",
            "AR flood-depth visibility state: " +
            $"spatialVersion={spatialVersion}, " +
            $"tracking={trackingValid}, " +
            $"anchor={anchorAvailable}, " +
            $"geometry={hasFloodGeometry}, " +
            $"visible={floodVisible}, " +
            $"groundWorldY={groundWorldY:F2} m, " +
            $"depth={ARFloodDepthRenderer.AppliedDepthMeters:F2} m.");
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

    private static void LogRouteGroundHeightStateIfChanged(
        bool trackingValid,
        ARCameraPoseBridge.AnchorSnapshot anchor,
        bool heightPlausible,
        float cameraWorldY,
        float cameraHeightAboveGroundMeters,
        long spatialVersion)
    {
        if (!trackingValid ||
            !anchor.IsAvailable)
        {
            lastLoggedRouteGroundHeightPlausible =
                null;

            return;
        }

        if (lastLoggedRouteGroundHeightPlausible ==
            heightPlausible)
        {
            return;
        }

        lastLoggedRouteGroundHeightPlausible =
            heightPlausible;

        string message =
            "AR route ground-height validation changed: " +
            $"spatialVersion={spatialVersion}, " +
            $"plausible={heightPlausible}, " +
            $"cameraY={cameraWorldY:F2} m, " +
            $"groundY={anchor.PositionY:F2} m, " +
            $"cameraHeight={cameraHeightAboveGroundMeters:F2} m, " +
            $"allowed=[{LocalArNavigationPolicy.MinimumPlausibleCameraHeightAboveGroundMeters:F2}," +
            $"{LocalArNavigationPolicy.MaximumPlausibleCameraHeightAboveGroundMeters:F2}] m.";

        if (heightPlausible)
        {
            AndroidLog.Debug(
                RouteLogTag,
                message);
        }
        else
        {
            AndroidLog.Warn(
                RouteLogTag,
                message +
                " Cyan route rendering is suppressed until a valid floor anchor is available.");
        }
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
