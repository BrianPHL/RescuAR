using Android.Util;
using Google.AR.Core;
using RescuAR.AR;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// Camera-tab lifecycle for the retained ARCore Session.
///
/// IMPORTANT:
/// The main ArCoreService declaration must be:
///
///     public sealed partial class ArCoreService : IArCoreService
///
/// This file deliberately keeps normal Camera-tab pause/resume separate from
/// full ARCore failure/disposal cleanup. A tab switch releases the physical
/// camera but retains the Session, spatial anchor, and guidance state.
/// </summary>
public sealed partial class ArCoreService
{
    /// <inheritdoc />
    public void PauseCameraSession()
    {
        bool cameraProcessorIdle =
            ARCameraTextureBridge.SuspendProcessing(
                TimeSpan.FromSeconds(
                    2));

        if (!cameraProcessorIdle)
        {
            Log.Warn(
                Tag,
                "Timed out waiting for the Vulkan camera converter to " +
                "become idle. Processing remains suspended for surface teardown.");
        }

        Session? currentSession =
            session;

        if (currentSession is null)
        {
            Log.Debug(
                Tag,
                "PauseCameraSession(): no ARCore Session exists.");

            return;
        }

        if (sessionPaused)
        {
            Log.Debug(
                Tag,
                "PauseCameraSession(): ARCore Session is already paused.");

            return;
        }

        Log.Debug(
            Tag,
            "Pausing ARCore camera session...");

        /*
         * A normal tab pause must NOT use StopFrameLoop(), because the
         * existing full-stop helper also releases spatial guidance state.
         *
         * Cancel the Session.Update() worker, wait for it to leave, and
         * release only the pending camera HardwareBuffer.
         */
        CancellationTokenSource? cancellation =
            frameLoopCancellation;

        Task? runningLoop =
            frameLoopTask;

        frameLoopCancellation =
            null;

        if (cancellation is not null)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (Exception exception)
            {
                Log.Warn(
                    Tag,
                    "Cancelling ARCore frame loop during pause failed: " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }

        if (runningLoop is not null &&
            !runningLoop.IsCompleted)
        {
            try
            {
                /*
                 * Session.Update() is BLOCKING but normally returns promptly.
                 * Waiting here prevents Session.Pause() from racing the
                 * worker's current Update().
                 */
                runningLoop.Wait(
                    TimeSpan.FromSeconds(
                        2));
            }
            catch (AggregateException exception)
            {
                Log.Warn(
                    Tag,
                    "ARCore frame loop ended with an exception while pausing: " +
                    $"{exception.Flatten().InnerException?.Message}");
            }
        }

        frameLoopTask =
            null;

        ReleasePendingCameraFrame();

        bool gateEntered =
            false;

        try
        {
            updateGate.Wait();
            gateEntered =
                true;

            currentSession.Pause();

            sessionPaused =
                true;

            lastProcessedTimestamp =
                long.MinValue;

            processedFrameCount =
                0;

            fpsWindowStartTimestamp =
                Environment.TickCount64;
        }
        catch (Exception exception)
        {
            Log.Error(
                Tag,
                $"ARCore Session.Pause() failed: {exception}");

            /*
             * If Pause failed, the Session should still be treated as active.
             */
            sessionPaused =
                false;
        }
        finally
        {
            if (gateEntered)
            {
                updateGate.Release();
            }

            cancellation?.Dispose();
        }

        if (sessionPaused)
        {
            Log.Debug(
                Tag,
                "ARCore camera session paused. Physical camera released; " +
                "ARCore Session and guidance state retained.");
        }
    }

    /// <inheritdoc />
    public bool ResumeCameraSession()
    {
        Session? currentSession =
            session;

        if (currentSession is null)
        {
            Log.Warn(
                Tag,
                "ResumeCameraSession(): no retained ARCore Session exists.");

            return false;
        }

        if (!sessionPaused)
        {
            Log.Debug(
                Tag,
                "ARCore Session is already active. Ensuring frame loop is running.");

            ARCameraTextureBridge.ResumeProcessing();

            StartFrameLoop();

            return true;
        }

        Log.Debug(
            Tag,
            "Resuming retained ARCore camera session...");

        bool gateEntered =
            false;

        try
        {
            updateGate.Wait();
            gateEntered =
                true;

            /*
             * Display geometry may have changed while another Shell tab was
             * visible. The normal Update() path will apply the newest
             * geometry before consuming the first resumed frame.
             */
            currentSession.Resume();

            sessionPaused =
                false;

            lastProcessedTimestamp =
                long.MinValue;

            processedFrameCount =
                0;

            fpsWindowStartTimestamp =
                Environment.TickCount64;

            hasLoggedTransformedUv =
                false;

            lastLoggedTrackingState =
                null;

            lastLoggedTrackingFailureReason =
                null;

            ARCameraTextureBridge.ResumeProcessing();
        }
        catch (Exception exception)
        {
            sessionPaused =
                true;

            Log.Error(
                Tag,
                $"ARCore Session.Resume() failed: {exception}");

            return false;
        }
        finally
        {
            if (gateEntered)
            {
                updateGate.Release();
            }
        }

        StartFrameLoop();

        Log.Debug(
            Tag,
            "Retained ARCore camera session resumed. " +
            "Automatic frame loop restarted.");

        return true;
    }
}
