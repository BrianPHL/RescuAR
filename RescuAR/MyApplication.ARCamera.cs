using RescuAR.AR;
using System;
using System.Threading;

namespace RescuAR;

/// <summary>
/// Draw-loop integration for the Android ARCore camera pipeline.
///
/// Kept in a separate partial file so the generated/default MyApplication.cs
/// service-registration code does not need to be edited.
/// </summary>
public partial class MyApplication
{
    private long arGraphicsGeneration;
    public bool WasArDrawSkipped { get; private set; }

    public void BindArGraphicsGeneration(
        long graphicsGeneration)
    {
        if (graphicsGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(graphicsGeneration));
        }

        Interlocked.Exchange(
            ref arGraphicsGeneration,
            graphicsGeneration);
    }

    public override void DrawFrame(
        TimeSpan gameTime)
    {
        long drawGraphicsGeneration =
            Interlocked.Read(ref arGraphicsGeneration);

        /*
         * Consume at most one pending ARCore camera frame immediately before
         * Evergine executes its normal draw/present cycle.
         *
         * The ARCore worker only acquires frames and prepares UV metadata.
         * The Vulkan camera conversion itself now happens here rather than on
         * the Task.Run worker that calls Session.Update().
         */
        bool skipEvergineDraw =
            ARCameraTextureBridge.ProcessDrawThreadWork(
                drawGraphicsGeneration);
        WasArDrawSkipped = skipEvergineDraw;

        /*
         * Apply ARCore spatial state directly on the active Evergine draw
         * path. No Behavior lifecycle or intermediary callback is involved.
         */
        try
        {
            if (skipEvergineDraw)
            {
                return;
            }

            // Texture replacement happens during import above, after UpdateFrame.
            // Bind it here so this draw cannot sample a disposed resize target.
            ARCameraBackgroundBehavior.ProcessDrawThreadWork(drawGraphicsGeneration);

            ARCameraSpatialController.ProcessDrawThreadWork(
                drawGraphicsGeneration);

            base.DrawFrame(
                gameTime);
        }
        finally
        {
            ARCameraTextureBridge.CompleteDrawThreadFrame(
                drawGraphicsGeneration);
        }
    }
}
