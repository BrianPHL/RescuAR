using Android.Util;
using RescuAR.AR;
using RescuAR.MAUI.Services;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// Owns native-bridge readiness and the bounded camera-import failure policy.
/// Permanent failures are terminal for the active graphics generation;
/// unknown failures receive only a small, time-backed retry budget.
/// </summary>
public sealed partial class ArCoreService
{
    private const int CameraImportTransientFailureBudget = 3;
    private const long CameraImportInitialBackoffMilliseconds = 250;
    private const long CameraImportMaximumBackoffMilliseconds = 2_000;

    private readonly object cameraPipelineFailureLock =
        new();

    private int consecutiveCameraImportFailures;
    private long cameraImportRetryAfterMilliseconds;
    private long terminalCameraPipelineGraphicsGeneration = -1;
    private ArCoreFailure terminalCameraPipelineFailure = ArCoreFailure.None;
    private long cameraFramesRejectedByFailurePolicy;
    private int nativeBridgeSelfTestLogWritten;

    private bool EnsureNativeBridgeReadyForStart(
        out ArCoreFailure failure)
    {
        NativeBridgeReadiness readiness =
            AHardwareBufferInterop.EnsureReady();

        UpdateNativeBridgeCapability(
            readiness);

        if (readiness.IsReady)
        {
            if (Interlocked.CompareExchange(
                    ref nativeBridgeSelfTestLogWritten,
                    1,
                    0) == 0)
            {
                Log.Info(
                    Tag,
                    "ARCORE_NATIVE_BRIDGE_SELF_TEST result=PASS, " +
                    $"loader='{readiness.LoaderName}'.");
            }

            failure =
                ArCoreFailure.None;

            return true;
        }

        failure =
            new ArCoreFailure(
                ArCoreFailureCode.NativeBridgeUnavailable,
                ArCoreFailureClassification.Terminal,
                "The native AR camera bridge could not be loaded. " +
                "AR guidance has been disabled. Reinstall a verified ARM64 build.");

        ARRenderGenerationBridge.Invalidate();
        InvalidatePublishedSessionState();

        ARCameraSpatialController.SetRouteRenderingEnabled(
            false,
            "native AR camera bridge startup self-test failed");

        if (Interlocked.CompareExchange(
                ref nativeBridgeSelfTestLogWritten,
                1,
                0) == 0)
        {
            Log.Error(
                Tag,
                "ARCORE_NATIVE_BRIDGE_SELF_TEST result=FAIL, " +
                $"loader='{readiness.LoaderName}', " +
                $"failureType={readiness.FailureType}, " +
                $"message='{readiness.FailureMessage}'.");
        }

        return false;
    }

    private void UpdateNativeBridgeCapability(
        NativeBridgeReadiness readiness)
    {
        lock (lifecycleStateLock)
        {
            capabilitySnapshot = capabilitySnapshot with
            {
                Version = ++capabilityVersion,
                CapturedAtUtc = DateTimeOffset.UtcNow,
                NativeBridgeReady = readiness.IsReady,
                Reason = readiness.IsReady
                    ? "Native camera bridge startup self-test passed."
                    : "Native camera bridge startup self-test failed: " +
                      $"{readiness.FailureType}: {readiness.FailureMessage}",
            };
        }
    }

    private void ResetCameraPipelineForGraphicsGeneration(
        long newGraphicsGeneration)
    {
        lock (cameraPipelineFailureLock)
        {
            consecutiveCameraImportFailures = 0;
            cameraImportRetryAfterMilliseconds = 0;

            if (terminalCameraPipelineGraphicsGeneration !=
                newGraphicsGeneration)
            {
                terminalCameraPipelineGraphicsGeneration = -1;
                terminalCameraPipelineFailure = ArCoreFailure.None;
            }
        }
    }

    private bool TryGetTerminalCameraPipelineFailure(
        out ArCoreFailure failure)
    {
        lock (cameraPipelineFailureLock)
        {
            if (terminalCameraPipelineGraphicsGeneration ==
                Interlocked.Read(ref graphicsGeneration))
            {
                failure =
                    terminalCameraPipelineFailure;

                return failure.Classification ==
                    ArCoreFailureClassification.Terminal;
            }

            failure =
                ArCoreFailure.None;

            return false;
        }
    }

    private bool CanAttemptCameraImport(
        long frameGraphicsGeneration)
    {
        lock (cameraPipelineFailureLock)
        {
            if (terminalCameraPipelineGraphicsGeneration ==
                frameGraphicsGeneration)
            {
                cameraFramesRejectedByFailurePolicy++;
                return false;
            }

            if (Environment.TickCount64 <
                cameraImportRetryAfterMilliseconds)
            {
                cameraFramesRejectedByFailurePolicy++;
                return false;
            }

            return true;
        }
    }

    private void RecordCameraImportSuccess()
    {
        lock (cameraPipelineFailureLock)
        {
            consecutiveCameraImportFailures = 0;
            cameraImportRetryAfterMilliseconds = 0;
        }
    }

    private void HandleCameraImportFailure(
        Exception exception,
        long failedGraphicsGeneration)
    {
        ArCameraImportFailureDisposition disposition =
            ArCameraImportFailurePolicy.Classify(
                exception);

        if (disposition ==
            ArCameraImportFailureDisposition.RetryWithBackoff)
        {
            int failureCount;
            long backoffMilliseconds;

            lock (cameraPipelineFailureLock)
            {
                failureCount =
                    ++consecutiveCameraImportFailures;

                backoffMilliseconds =
                    Math.Min(
                        CameraImportInitialBackoffMilliseconds <<
                            Math.Min(failureCount - 1, 3),
                        CameraImportMaximumBackoffMilliseconds);

                cameraImportRetryAfterMilliseconds =
                    Environment.TickCount64 + backoffMilliseconds;
            }

            if (failureCount <
                CameraImportTransientFailureBudget)
            {
                Log.Warn(
                    Tag,
                    "ARCORE_CAMERA_IMPORT_RETRY " +
                    $"attempt={failureCount}, " +
                    $"budget={CameraImportTransientFailureBudget}, " +
                    $"backoffMs={backoffMilliseconds}, " +
                    $"failureType={exception.GetType().Name}, " +
                    $"message='{exception.Message}'.");

                return;
            }

            EnterTerminalCameraPipelineFailure(
                failedGraphicsGeneration,
                ArCoreFailureCode.RendererUnavailable,
                "The AR camera importer repeatedly failed. " +
                "AR guidance has been disabled for this graphics session.",
                exception,
                disposition);

            return;
        }

        ArCoreFailureCode code =
            disposition == ArCameraImportFailureDisposition.TerminalNativeBridge
                ? ArCoreFailureCode.NativeBridgeUnavailable
                : disposition == ArCameraImportFailureDisposition.TerminalUnsupportedFormat
                    ? ArCoreFailureCode.CameraPassthroughUnavailable
                    : ArCoreFailureCode.RendererUnavailable;

        string message =
            code == ArCoreFailureCode.NativeBridgeUnavailable
                ? "The native AR camera bridge became unavailable. " +
                  "AR guidance has been disabled."
                : code == ArCoreFailureCode.CameraPassthroughUnavailable
                    ? "This device's camera buffer format is not supported by " +
                      "the AR renderer. AR guidance has been disabled."
                    : "The Vulkan AR camera renderer failed. " +
                      "AR guidance has been disabled.";

        EnterTerminalCameraPipelineFailure(
            failedGraphicsGeneration,
            code,
            message,
            exception,
            disposition);
    }

    private void EnterTerminalCameraPipelineFailure(
        long failedGraphicsGeneration,
        ArCoreFailureCode code,
        string message,
        Exception exception,
        ArCameraImportFailureDisposition disposition)
    {
        ArCoreFailure failure =
            new(
                code,
                ArCoreFailureClassification.Terminal,
                message);

        lock (cameraPipelineFailureLock)
        {
            if (terminalCameraPipelineGraphicsGeneration ==
                failedGraphicsGeneration)
            {
                return;
            }

            terminalCameraPipelineGraphicsGeneration =
                failedGraphicsGeneration;

            terminalCameraPipelineFailure =
                failure;
        }

        if (code == ArCoreFailureCode.NativeBridgeUnavailable)
        {
            NativeBridgeReadiness readiness =
                AHardwareBufferInterop.MarkUnavailable(
                    exception);

            UpdateNativeBridgeCapability(
                readiness);
        }

        ARRenderGenerationBridge.Suspend(
            Interlocked.Read(ref currentSessionGeneration),
            failedGraphicsGeneration);

        ARCameraTextureBridge.SuspendProcessing(
            TimeSpan.Zero);

        ReleasePendingCameraFrame();
        InvalidatePublishedSessionState();

        IArCameraFrameImporter? failedImporter =
            importer;

        try
        {
            failedImporter?.Dispose();
        }
        catch (Exception cleanupException)
        {
            Log.Error(
                Tag,
                "AR camera importer cleanup failed after terminal error: " +
                $"{cleanupException.GetType().Name}: " +
                $"{cleanupException.Message}");
        }

        if (ReferenceEquals(
                importer,
                failedImporter))
        {
            importer =
                null;
        }

        ARCameraSpatialController.SetRouteRenderingEnabled(
            false,
            "terminal AR camera pipeline failure");

        try
        {
            frameLoopCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A concurrent lifecycle transition already drained the loop.
        }

        Log.Error(
            Tag,
            "ARCORE_CAMERA_PIPELINE_TERMINAL " +
            $"graphicsGeneration={failedGraphicsGeneration}, " +
            $"code={code}, " +
            $"disposition={disposition}, " +
            $"failureType={exception.GetType().Name}, " +
            $"rejectedFrames={cameraFramesRejectedByFailurePolicy}, " +
            $"message='{exception.Message}'.");

        SetLifecycleState(
            ArCoreLifecycleState.Faulted,
            failure);

        TrackTransition(
            PauseAfterTerminalCameraPipelineFailureAsync(
                failure,
                failedGraphicsGeneration),
            "terminal AR camera pipeline failure");
    }

    private async Task<ArCoreLifecycleResult>
        PauseAfterTerminalCameraPipelineFailureAsync(
            ArCoreFailure failure,
            long failedGraphicsGeneration)
    {
        await PauseAsync().ConfigureAwait(false);

        bool failureStillCurrent;

        lock (cameraPipelineFailureLock)
        {
            failureStillCurrent =
                terminalCameraPipelineGraphicsGeneration ==
                    failedGraphicsGeneration;
        }

        if (!failureStillCurrent)
        {
            return CurrentLifecycleResult(
                false);
        }

        SetLifecycleState(
            ArCoreLifecycleState.Faulted,
            failure);

        return CurrentLifecycleResult(
            false);
    }
}
