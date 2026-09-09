using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Mathematics;
using System;
using System.Threading;

namespace RescuAR.AR;

/// <summary>
/// Drives the Evergine Camera entity from the newest ARCore
/// display-oriented pose.
///
/// This build retains the validated direct ARCore pose mapping and adds
/// ARCore's display-aware projection to the Evergine Camera3D through
/// SetCustomProjection().
/// </summary>
public sealed class ARCameraPoseBehavior : Behavior
{
    /*
     * Spatial application is deliberately executed from MyApplication.DrawFrame()
     * rather than Behavior.Update(). The latest runtime logs proved that the
     * Behavior update path could stop advancing while DrawFrame and the camera
     * pipeline continued at ~30 FPS.
     *
     * ARCore remains the sole spatial authority; this callback only copies the
     * newest coherent snapshot into Evergine immediately before rendering.
     */
    private static Action? drawThreadProcessor;

    public static void ProcessDrawThreadWork()
    {
        Action? processor =
            Volatile.Read(
                ref drawThreadProcessor);

        processor?.Invoke();
    }

    private const string TestTargetEntityName =
        "capsule";

    private Transform3D? cameraTransform;
    private Camera3D? cameraComponent;
    private Entity? testTargetEntity;
    private Transform3D? testTargetTransform;

    private long appliedVersion =
        -1;

    protected override bool OnAttached()
    {
        if (!base.OnAttached())
        {
            return false;
        }

        cameraTransform =
            Owner.FindComponent<Transform3D>();

        if (cameraTransform is null)
        {
            return false;
        }

        cameraComponent =
            Owner.FindComponent<Camera3D>();

        if (cameraComponent is null)
        {
            return false;
        }

        /*
         * Keep the serialized clipping planes synchronized with the values
         * ARCore uses to build its projection matrix.
         */
        cameraComponent.NearPlane =
            0.1f;

        cameraComponent.FarPlane =
            1000.0f;

        /*
         * Keep this disabled during AR projection validation. The previous
         * test showed more reliable visibility with CPU frustum culling off.
         */
        cameraComponent.FrustumCullingEnabled =
            false;

        testTargetEntity =
            Owner.EntityManager.Find(
                TestTargetEntityName);

        testTargetTransform =
            testTargetEntity?.FindComponent<Transform3D>();

        /*
         * The serialized MyScene.wescene position is editor-only.
         * Hide the capsule until ARCore supplies a real floor anchor.
         */
        if (testTargetEntity is not null)
        {
            testTargetEntity.IsEnabled =
                false;
        }

        Volatile.Write(
            ref drawThreadProcessor,
            ApplyLatestSpatialSnapshot);

        return true;
    }

    protected override void Update(
        TimeSpan gameTime)
    {
        /*
         * Intentionally empty.
         *
         * The AR camera transform, projection, and ground-anchor target are
         * now applied on Evergine's draw thread by ProcessDrawThreadWork(),
         * immediately before base.DrawFrame(). This avoids the update-loop
         * stall demonstrated by the snapshot telemetry.
         */
    }

    private void ApplyLatestSpatialSnapshot()
    {
        if (cameraTransform is null)
        {
            return;
        }

        /*
         * Read ONE immutable ARCore spatial snapshot. Camera pose,
         * projection, and anchor below therefore always originate from the
         * same Session.Update() result.
         */
        ARCameraPoseBridge.SpatialSnapshot frame =
            ARCameraPoseBridge.CurrentFrame;

        if (frame.Version ==
            appliedVersion)
        {
            PublishTelemetry(
                frame.Version,
                frame.FrameTimestamp);

            return;
        }

        appliedVersion =
            frame.Version;

        if (!frame.IsTracking ||
            !frame.Pose.IsTracking)
        {
            /*
             * Hold the last valid rendered pose while ARCore is paused.
             * Do not partially apply anchor/projection from an unusable frame.
             */
            PublishTelemetry(
                frame.Version,
                frame.FrameTimestamp);

            return;
        }

        ARCameraPoseBridge.AnchorSnapshot anchor =
            frame.Anchor;

        if (anchor.IsAvailable &&
            testTargetTransform is not null)
        {
            testTargetTransform.Position =
                new Vector3(
                    anchor.PositionX,
                    anchor.PositionY,
                    anchor.PositionZ);

            if (testTargetEntity is not null &&
                !testTargetEntity.IsEnabled)
            {
                testTargetEntity.IsEnabled =
                    true;
            }
        }
        else if (testTargetEntity is not null &&
                 testTargetEntity.IsEnabled)
        {
            testTargetEntity.IsEnabled =
                false;
        }

        ARCameraPoseBridge.ProjectionSnapshot projection =
            frame.Projection;

        if (projection.IsAvailable &&
            cameraComponent is not null)
        {
            Matrix4x4 customProjection =
                new(
                    projection.M11,
                    projection.M12,
                    projection.M13,
                    projection.M14,
                    projection.M21,
                    projection.M22,
                    projection.M23,
                    projection.M24,
                    projection.M31,
                    projection.M32,
                    projection.M33,
                    projection.M34,
                    projection.M41,
                    projection.M42,
                    projection.M43,
                    projection.M44);

            cameraComponent.SetCustomProjection(
                ref customProjection);

            ARCameraPoseBridge.PublishProjectionTelemetry(
                cameraComponent.IsClipDepthZeroToOne,
                cameraComponent.FlipYProjection);
        }

        ARCameraPoseBridge.PoseSnapshot pose =
            frame.Pose;

        /*
         * ARCore is the sole tracking authority. Evergine's Transform3D is
         * only the rendering representation of this exact ARCore-frame pose.
         */
        cameraTransform.Position =
            new Vector3(
                pose.PositionX,
                pose.PositionY,
                pose.PositionZ);

        cameraTransform.Orientation =
            new Quaternion(
                pose.RotationX,
                pose.RotationY,
                pose.RotationZ,
                pose.RotationW);

        PublishTelemetry(
            frame.Version,
            frame.FrameTimestamp);
    }

    private void PublishTelemetry(
        long appliedSpatialVersion,
        long appliedFrameTimestamp)
    {
        if (cameraTransform is null ||
            testTargetTransform is null ||
            testTargetEntity is null ||
            !testTargetEntity.IsEnabled)
        {
            return;
        }

        Vector3 camera =
            cameraTransform.Position;

        Quaternion rotation =
            cameraTransform.Orientation;

        Vector3 target =
            testTargetTransform.Position;

        float deltaX =
            target.X - camera.X;

        float deltaY =
            target.Y - camera.Y;

        float deltaZ =
            target.Z - camera.Z;

        float distance =
            MathF.Sqrt(
                deltaX * deltaX +
                deltaY * deltaY +
                deltaZ * deltaZ);

        ARCameraPoseBridge.PublishEngineTelemetry(
            appliedSpatialVersion,
            appliedFrameTimestamp,
            camera.X,
            camera.Y,
            camera.Z,
            rotation.X,
            rotation.Y,
            rotation.Z,
            rotation.W,
            target.X,
            target.Y,
            target.Z,
            distance);
    }

    protected override void OnDetached()
    {
        Action? currentProcessor =
            Volatile.Read(
                ref drawThreadProcessor);

        if (currentProcessor is not null &&
            ReferenceEquals(
                currentProcessor.Target,
                this))
        {
            Volatile.Write(
                ref drawThreadProcessor,
                null);
        }

        if (cameraComponent is not null)
        {
            cameraComponent.ResetCustomProjection();
        }

        cameraTransform =
            null;

        cameraComponent =
            null;

        testTargetEntity =
            null;

        testTargetTransform =
            null;

        base.OnDetached();
    }
}
