using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Mathematics;
using System;

namespace RescuAR.AR;

/// <summary>
/// Persistent ARCore -> Evergine spatial renderer.
///
/// ARCore is the sole spatial authority. This controller owns only references
/// to the already-loaded Evergine Camera and diagnostic capsule entities.
/// MyApplication.DrawFrame() invokes it directly immediately before rendering.
///
/// This intentionally does NOT inherit Behavior and does NOT depend on
/// Behavior.Update(), OnAttached(), OnDetached(), or a registered delegate.
/// </summary>
public static class ARCameraSpatialController
{
    private static readonly object sync =
        new();

    private static Transform3D? cameraTransform;
    private static Camera3D? cameraComponent;
    private static Entity? targetEntity;
    private static Transform3D? targetTransform;

    private static Entity? routeEntity;
    private static Transform3D? routeTransform;

    /*
     * ArCoreService currently publishes the diagnostic capsule CENTER at
     * groundAnchorY + 0.25 m. The route ribbon should instead sit almost on
     * the detected floor. With a 0.03 m-thick ribbon, putting its center
     * 0.015 m above the plane yields:
     *
     *   -0.25 + 0.015 = -0.235 m
     *
     * relative to the published capsule-center anchor Y.
     */
    private const float RouteYOffsetFromPublishedAnchor =
        -0.235f;

    private static long appliedVersion =
        -1;

    private static bool initialized;

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

        /*
         * Never render the serialized editor position. The capsule becomes
         * visible only when the current ARCore snapshot contains a real
         * ground anchor.
         */
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

            initialized =
                true;
        }
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

        ARCameraPoseBridge.SpatialSnapshot frame =
            ARCameraPoseBridge.CurrentFrame;

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

        if (!frame.IsTracking ||
            !frame.Pose.IsTracking)
        {
            /*
             * Preserve the last valid rendered camera/anchor/projection
             * during ARCore PAUSED. The applied version still advances so
             * telemetry proves that this draw path is alive.
             */
            PublishTelemetry(
                frame.Version,
                frame.FrameTimestamp,
                camera,
                capsule,
                capsuleTransform);

            return;
        }

        ARCameraPoseBridge.AnchorSnapshot anchor =
            frame.Anchor;

        if (anchor.IsAvailable)
        {
            capsuleTransform.Position =
                new Vector3(
                    anchor.PositionX,
                    anchor.PositionY,
                    anchor.PositionZ);

            /*
             * Place the route root on the same detected ARCore floor anchor,
             * correcting only for the capsule-center Y offset currently
             * embedded in AnchorSnapshot.
             *
             * Route segment geometry itself is entirely local to this root.
             */
            routeRootTransform.Position =
                new Vector3(
                    anchor.PositionX,
                    anchor.PositionY +
                        RouteYOffsetFromPublishedAnchor,
                    anchor.PositionZ);

            if (!capsule.IsEnabled)
            {
                capsule.IsEnabled =
                    true;
            }

            if (!route.IsEnabled)
            {
                route.IsEnabled =
                    true;
            }
        }
        else
        {
            if (capsule.IsEnabled)
            {
                capsule.IsEnabled =
                    false;
            }

            if (route.IsEnabled)
            {
                route.IsEnabled =
                    false;
            }
        }

        ARCameraPoseBridge.ProjectionSnapshot projection =
            frame.Projection;

        if (projection.IsAvailable)
        {
            /*
             * ARCore -> Evergine custom-projection conversion.
             *
             * ARCore exposes an OpenGL-style projection in column-major
             * storage / column-vector mathematical convention.
             *
             * ARCameraPoseBridge already converts ARCore's column-major array
             * into named mathematical row/column entries. Evergine, however,
             * uses row-vector matrices:
             *
             *   ViewProjection = View * Projection
             *   translation is M41/M42/M43
             *   perspective M34 = -1
             *
             * Therefore the ARCore mathematical matrix must first be
             * TRANSPOSED into Evergine's row-vector convention.
             */
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

            /*
             * ARCore's OpenGL projection maps clip depth to [-1, +1].
             * This Android/Vulkan Evergine camera reports
             * IsClipDepthZeroToOne = true, so convert clip-space Z to [0, 1].
             *
             * With Evergine row vectors this is a post-projection transform:
             *
             *   z01 = 0.5 * zGL + 0.5 * wGL
             *   w01 = wGL
             *
             * P01 = Pgl * DepthMinusOneToOne_To_ZeroToOne
             */
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

            /*
             * SetCustomProjection bypasses Camera3D.RefreshProjection(), so
             * any framebuffer Y flip that Evergine would normally apply must
             * also be applied to the custom matrix.
             *
             * Negate the complete Y clip-space column because the calibrated
             * ARCore matrix may contain an off-center Y term (M32), not only
             * the symmetric M22 term of Evergine's default perspective.
             */
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
