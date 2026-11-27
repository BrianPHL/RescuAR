using Android.Util;
using RescuAR.AR;
using RescuAR.MAUI.Services;

namespace RescuAR.MAUI.Platforms.Android.Services;

public sealed partial class ArCoreService
{
    private readonly ArCameraFrameAcquisitionPolicy cameraFrameAcquisition = new();

    private readonly record struct CameraFrameAttempt(ArCameraFrameRun Run, long PublicationCycle,
        ArCameraFrameAcquisitionEvent Event, Exception? Error = null);

    private ArCameraFrameRun CurrentCameraFrameRun() => new(
        Interlocked.Read(ref currentSessionGeneration), Interlocked.Read(ref graphicsGeneration),
        Interlocked.Read(ref lifecycleRequestGeneration));

    private bool OwnsCameraFrameRun(ArCameraFrameRun run) =>
        run.Owns(CurrentCameraFrameRun(), runIntent.ShouldRun && graphicsContext is not null);

    private bool CanPublishCameraFrame(ARRenderGenerationToken generation, long publicationCycle) =>
        runIntent.ShouldRun && graphicsContext is not null && ARRenderGenerationBridge.IsCurrent(generation) &&
        publicationCycle == Interlocked.Read(ref cameraFramePublicationCycle);

    private void AdvanceCameraFramePublicationCycle()
    {
        lock (pendingCameraFrameLock) Interlocked.Increment(ref cameraFramePublicationCycle);
    }

    // Called after releasing updateGate. The retiring producer must exit so the
    // lifecycle owner can drain it before invoking native Session.Pause/Resume.
    private bool HandleCameraFrameAttempt(CameraFrameAttempt attempt, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || !OwnsCameraFrameRun(attempt.Run) ||
            attempt.PublicationCycle != Interlocked.Read(ref cameraFramePublicationCycle)) return true;

        var decision = cameraFrameAcquisition.Observe(attempt.Run, attempt.Event, Environment.TickCount64);
        switch (decision.Action)
        {
            case ArCameraFrameAcquisitionAction.Continue:
                return true;
            case ArCameraFrameAcquisitionAction.Wait:
                Log.Warn(Tag, $"ARCORE_FRAME_ACQUISITION_RETRY delayMs={decision.DelayMilliseconds}; " +
                    $"failure={attempt.Error?.GetType().Name ?? "Unavailable"}.");
                return !cancellationToken.WaitHandle.WaitOne(decision.DelayMilliseconds);
            default:
                TrackTransition(Task.Run(() => RecoverFrameAcquisitionAsync(attempt, decision)),
                    "ARCore frame acquisition");
                return false;
        }
    }

    private async Task<ArCoreLifecycleResult> RecoverFrameAcquisitionAsync(
        CameraFrameAttempt attempt, ArCameraFrameAcquisitionDecision decision)
    {
        ArCameraFrameRun run = attempt.Run;
        bool gateEntered = await EnterGateAsync(lifecycleGate, LifecycleGateTimeout, CancellationToken.None)
            .ConfigureAwait(false);
        try
        {
            if (!OwnsCameraFrameRun(run) || attempt.PublicationCycle != Interlocked.Read(ref cameraFramePublicationCycle))
                return SupersededLifecycleRequest(run.Request);

            if (!gateEntered)
                return ReportFrameAcquisitionFault(run, ArCoreFailureCode.TimedOut,
                    "The AR camera could not recover in time. Switch to 2D Map.");

            if (TryGetTerminalCameraPipelineFailure(out _)) return CurrentLifecycleResult(false);

            bool retry = decision.Action == ArCameraFrameAcquisitionAction.RecoverSession;
            Log.Warn(Tag, $"ARCORE_FRAME_ACQUISITION_STOP action={decision.Action}; " +
                $"failure={attempt.Error?.GetType().Name ?? attempt.Event.ToString()}; session={run.Session}; graphics={run.Graphics}.");

            if (!SetLifecycleState(ArCoreLifecycleState.Pausing, ArCoreFailure.None, run.Request))
                return SupersededLifecycleRequest(run.Request);

            bool paused = await PauseSessionCoreAsync(CancellationToken.None).ConfigureAwait(false);
            if (!OwnsCameraFrameRun(run)) return SupersededLifecycleRequest(run.Request);
            if (!paused)
                return ReportFrameAcquisitionFault(run, ArCoreFailureCode.SessionPauseFailed,
                    "The AR camera could not pause safely. Switch to 2D Map.");

            if (!retry)
            {
                if (!HasCameraPermission())
                    return ReportFrameAcquisitionFault(run, ArCoreFailureCode.CameraPermissionDenied,
                        "Camera permission is required for AR. Switch to 2D Map or restore camera access.");

                return ReportFrameAcquisitionFault(run,
                    ArCoreLifecyclePolicy.ClassifyFailureCode(attempt.Error, ArCoreFailureCode.CameraUnavailable),
                    "The AR camera stopped supplying images. Switch to 2D Map or reopen Camera to try again.");
            }

            if (!SetLifecycleState(ArCoreLifecycleState.Paused, ArCoreFailure.None, run.Request))
                return SupersededLifecycleRequest(run.Request);

            await Task.Delay(decision.DelayMilliseconds).ConfigureAwait(false);
            if (!OwnsCameraFrameRun(run)) return SupersededLifecycleRequest(run.Request);
            if (TryGetTerminalCameraPipelineFailure(out _)) return CurrentLifecycleResult(false);
            if (!HasCameraPermission())
                return ReportFrameAcquisitionFault(run, ArCoreFailureCode.CameraPermissionDenied,
                    "Camera permission is required for AR. Switch to 2D Map or restore camera access.");

            bool resumed = await ResumeSessionCoreAsync(CancellationToken.None).ConfigureAwait(false);
            if (!OwnsCameraFrameRun(run)) return SupersededLifecycleRequest(run.Request);
            if (!resumed)
                return ReportFrameAcquisitionFault(run,
                    ArCoreLifecyclePolicy.ClassifyFailureCode(lastSessionOperationException, ArCoreFailureCode.SessionResumeFailed),
                    "The AR camera could not restart. Switch to 2D Map or reopen Camera to try again.");

            if (!SetLifecycleState(ArCoreLifecycleState.Running, ArCoreFailure.None, run.Request))
                return SupersededLifecycleRequest(run.Request);
            return CurrentLifecycleResult(true);
        }
        finally
        {
            if (gateEntered) lifecycleGate.Release();
        }
    }

    private ArCoreLifecycleResult ReportFrameAcquisitionFault(ArCameraFrameRun run, ArCoreFailureCode code, string message)
    {
        lock (lifecycleStateLock)
        {
            if (!OwnsCameraFrameRun(run)) return SupersededLifecycleRequest(run.Request);
            runIntent.Request(false);
            ARRenderGenerationBridge.Suspend(run.Session, run.Graphics);
            ARCameraTextureBridge.SuspendProcessing(TimeSpan.Zero);
        }

        if (!SetLifecycleState(ArCoreLifecycleState.Faulted,
            new(code, ArCoreFailureClassification.Recoverable, message), run.Request))
            return SupersededLifecycleRequest(run.Request);

        return CurrentLifecycleResult(false);
    }
}
