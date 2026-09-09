using RescuAR.AR;
using System;

namespace RescuAR;

/// <summary>
/// Draw-loop integration for the Android ARCore camera pipeline.
///
/// Kept in a separate partial file so the generated/default MyApplication.cs
/// service-registration code does not need to be edited.
/// </summary>
public partial class MyApplication
{
    public override void DrawFrame(
        TimeSpan gameTime)
    {
        /*
         * Consume at most one pending ARCore camera frame immediately before
         * Evergine executes its normal draw/present cycle.
         *
         * The ARCore worker only acquires frames and prepares UV metadata.
         * The Vulkan camera conversion itself now happens here rather than on
         * the Task.Run worker that calls Session.Update().
         */
        ARCameraTextureBridge.ProcessDrawThreadWork();

        /*
         * Apply ARCore spatial state directly on the active Evergine draw
         * path. No Behavior lifecycle or intermediary callback is involved.
         */
        ARCameraSpatialController.ProcessDrawThreadWork();

        base.DrawFrame(
            gameTime);
    }
}
