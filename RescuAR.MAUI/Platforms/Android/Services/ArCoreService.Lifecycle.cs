using Android.App;
using Android.Content.PM;
using Android.Util;
using Google.AR.Core;
using Microsoft.Maui.ApplicationModel;
using RescuAR.AR;
using RescuAR.MAUI.Services;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// Authoritative owner of all ARCore session lifecycle transitions.
/// Installation runs on the MAUI main thread; session calls run off the UI
/// thread and are serialized with frame work through updateGate; Vulkan import
/// remains owned by Evergine's draw thread.
/// </summary>
public sealed partial class ArCoreService
{
    public event Action<ArCoreLifecycleSnapshot>? LifecycleChanged;

    private static readonly TimeSpan LifecycleGateTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan FrameLoopDrainTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan UpdateGateTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CameraProcessorDrainTimeout = TimeSpan.FromSeconds(2);
    private const int AvailabilityRetryDelayMilliseconds = 200;
    private const int AvailabilityRetryLimit = 15;

    private readonly object lifecycleStateLock = new();
    private readonly object trackedTransitionLock = new();
    private Task trackedTransitionTask = Task.CompletedTask;
    private ArCoreLifecycleState lifecycleState = ArCoreLifecycleState.Uninitialized;
    private ArCoreLifecycleTarget desiredLifecycleState = ArCoreLifecycleTarget.Paused;
    private ArCoreFailure lastLifecycleFailure = ArCoreFailure.None;
    private DateTimeOffset lifecycleChangedAtUtc = DateTimeOffset.UtcNow;
    private long lifecycleRequestGeneration;
    private long currentSessionGeneration;
    private long capabilityVersion;
    private long graphicsGeneration;
    private volatile bool installRequestPending;
    private bool resumeAfterActivityPause;
    private bool resumeAfterGraphicsRecreation;
    private volatile bool activityIsResumed = true;
    private ArCoreCapabilitySnapshot capabilitySnapshot = new(
        0,
        DateTimeOffset.MinValue,
        false,
        false,
        false,
        null,
        "Unknown",
        false,
        false,
        "Unknown",
        null,
        false,
        null,
        0,
        "Capabilities have not been evaluated.");

    public ArCoreLifecycleSnapshot LifecycleSnapshot
    {
        get
        {
            lock (lifecycleStateLock)
            {
                return CreateLifecycleSnapshotLocked();
            }
        }
    }

    public ArCoreCapabilitySnapshot CapabilitySnapshot
    {
        get
        {
            lock (lifecycleStateLock)
            {
                return capabilitySnapshot;
            }
        }
    }

    public async Task<ArCoreLifecycleResult> EnsureRunningAsync(
        CancellationToken cancellationToken = default)
    {
        long requestGeneration = BeginLifecycleRequest(
            ArCoreLifecycleTarget.Running,
            "ARCore running requested");
        bool gateEntered = false;

        try
        {
            gateEntered = await EnterGateAsync(
                lifecycleGate,
                LifecycleGateTimeout,
                cancellationToken).ConfigureAwait(false);

            if (!gateEntered)
            {
                return FailLifecycleRequest(
                    ArCoreFailureCode.TimedOut,
                    ArCoreFailureClassification.Recoverable,
                    "Timed out waiting for the ARCore lifecycle owner.");
            }

            if (!IsLatestLifecycleRequest(requestGeneration, ArCoreLifecycleTarget.Running))
            {
                return SupersededLifecycleRequest(requestGeneration);
            }

            if (!activityIsResumed)
            {
                return FailLifecycleRequest(
                    ArCoreFailureCode.ActivityUnavailable,
                    ArCoreFailureClassification.Recoverable,
                    "The Android Activity is not resumed.");
            }

            if (lifecycleState is ArCoreLifecycleState.Disposed or ArCoreLifecycleState.Disposing)
            {
                return FailLifecycleRequest(
                    ArCoreFailureCode.SessionShutdownFailed,
                    ArCoreFailureClassification.Terminal,
                    "The ARCore service has already been disposed.");
            }

            if (session is not null && !sessionPaused && IsFrameLoopRunning)
            {
                SetLifecycleState(ArCoreLifecycleState.Running, ArCoreFailure.None);
                return CurrentLifecycleResult(true);
            }

            SetLifecycleState(
                session is null ? ArCoreLifecycleState.Initializing : ArCoreLifecycleState.Running,
                ArCoreFailure.None);

            ArCoreApk.Availability availability =
                await WaitForStableAvailabilityAsync(cancellationToken).ConfigureAwait(false);

            if (availability.IsTransient || availability.IsUnknown)
            {
                PublishCapabilities(availability, false,
                    "ARCore availability did not stabilize before the bounded retry ended.");
                return FailLifecycleRequest(
                    ArCoreFailureCode.AvailabilityPending,
                    ArCoreFailureClassification.Recoverable,
                    "ARCore availability is still being determined.");
            }

            if (availability.IsUnsupported)
            {
                PublishCapabilities(availability, false,
                    "The device reported ARCore as unsupported.");
                return FailLifecycleRequest(
                    ArCoreFailureCode.UnsupportedDevice,
                    ArCoreFailureClassification.Terminal,
                    "ARCore is unsupported on this device.");
            }

            if (!HasCameraPermission())
            {
                PublishCapabilities(availability, false, "Camera permission is not granted.");
                return FailLifecycleRequest(
                    ArCoreFailureCode.CameraPermissionDenied,
                    ArCoreFailureClassification.Recoverable,
                    "Camera permission is required before ARCore can start.");
            }

            if (graphicsContext is null)
            {
                PublishCapabilities(availability, false,
                    "The Evergine Vulkan graphics context is not ready.");
                return FailLifecycleRequest(
                    ArCoreFailureCode.GraphicsUnavailable,
                    ArCoreFailureClassification.Recoverable,
                    "The AR rendering surface is not ready.");
            }

            if (session is null)
            {
                ArCoreApk.InstallStatus installStatus;
                try
                {
                    installStatus = await RequestInstallOnMainThreadAsync(
                        !installRequestPending,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException exception)
                {
                    return FailLifecycleRequest(
                        ArCoreFailureCode.ActivityUnavailable,
                        ArCoreFailureClassification.Recoverable,
                        exception.Message);
                }
                catch (Exception exception)
                {
                    return FailLifecycleRequest(
                        ClassifyFailureCode(exception, ArCoreFailureCode.InstallationFailed),
                        ArCoreFailureClassification.Recoverable,
                        $"ARCore installation check failed: {exception.Message}");
                }

                if (installStatus == ArCoreApk.InstallStatus.InstallRequested)
                {
                    installRequestPending = true;
                    PublishCapabilities(availability, false,
                        "Google Play Services for AR installation is pending.");
                    SetLifecycleState(
                        ArCoreLifecycleState.Uninitialized,
                        new ArCoreFailure(
                            ArCoreFailureCode.InstallationRequired,
                            ArCoreFailureClassification.Recoverable,
                            "Complete the ARCore installation, then return to RescuAR."));
                    return CurrentLifecycleResult(false);
                }

                if (installStatus != ArCoreApk.InstallStatus.Installed)
                {
                    return FailLifecycleRequest(
                        ArCoreFailureCode.InstallationFailed,
                        ArCoreFailureClassification.Recoverable,
                        $"Unexpected ARCore install status: {installStatus}.");
                }

                installRequestPending = false;
                PublishCapabilities(availability, true,
                    "ARCore prerequisites are ready for session creation.");

                bool initialized = await Task.Run(
                    () => InitializeSessionCore(availability),
                    cancellationToken).ConfigureAwait(false);

                if (!initialized)
                {
                    ArCoreFailureCode code = ClassifyFailureCode(
                        lastSessionOperationException,
                        ArCoreFailureCode.SessionInitializationFailed);
                    return FailLifecycleRequest(
                        code,
                        code == ArCoreFailureCode.UnsupportedDevice
                            ? ArCoreFailureClassification.Terminal
                            : ArCoreFailureClassification.Recoverable,
                        lastSessionOperationException?.Message ??
                            "ARCore session initialization failed.");
                }

                long sessionGeneration = Interlocked.Increment(ref currentSessionGeneration);

                if (!IsLatestLifecycleRequest(requestGeneration, ArCoreLifecycleTarget.Running))
                {
                    return SupersededLifecycleRequest(requestGeneration);
                }

                StartFrameLoop(sessionGeneration);
            }
            else
            {
                bool resumed = await ResumeSessionCoreAsync(cancellationToken).ConfigureAwait(false);
                if (!resumed)
                {
                    return FailLifecycleRequest(
                        ClassifyFailureCode(
                            lastSessionOperationException,
                            ArCoreFailureCode.SessionResumeFailed),
                        ArCoreFailureClassification.Recoverable,
                        lastSessionOperationException?.Message ??
                            "The retained ARCore session could not resume.");
                }
            }

            if (!IsLatestLifecycleRequest(requestGeneration, ArCoreLifecycleTarget.Running))
            {
                return SupersededLifecycleRequest(requestGeneration);
            }

            PublishCapabilities(
                availability,
                true,
                "ARCore session is running.",
                cameraAvailable: true);
            SetLifecycleState(ArCoreLifecycleState.Running, ArCoreFailure.None);
            return CurrentLifecycleResult(true);
        }
        catch (OperationCanceledException)
        {
            return FailLifecycleRequest(
                ArCoreFailureCode.Cancelled,
                ArCoreFailureClassification.Recoverable,
                "The ARCore start request was cancelled.");
        }
        catch (Exception exception)
        {
            Log.Error(Tag, $"ARCore running transition failed: {exception}");
            return FailLifecycleRequest(
                ClassifyFailureCode(exception, ArCoreFailureCode.Unknown),
                ArCoreFailureClassification.Recoverable,
                exception.Message);
        }
        finally
        {
            if (gateEntered)
            {
                lifecycleGate.Release();
            }
        }
    }

    public async Task<ArCoreLifecycleResult> PauseAsync(
        CancellationToken cancellationToken = default)
    {
        long requestGeneration = BeginLifecycleRequest(
            ArCoreLifecycleTarget.Paused,
            "ARCore pause requested");
        bool gateEntered = false;

        try
        {
            gateEntered = await EnterGateAsync(
                lifecycleGate,
                LifecycleGateTimeout,
                cancellationToken).ConfigureAwait(false);

            if (!gateEntered)
            {
                return FailLifecycleRequest(
                    ArCoreFailureCode.TimedOut,
                    ArCoreFailureClassification.Recoverable,
                    "Timed out waiting to pause ARCore.");
            }

            if (!IsLatestLifecycleRequest(requestGeneration, ArCoreLifecycleTarget.Paused))
            {
                return SupersededLifecycleRequest(requestGeneration);
            }

            if (lifecycleState == ArCoreLifecycleState.Disposed)
            {
                return CurrentLifecycleResult(true);
            }

            if (session is null || sessionPaused)
            {
                InvalidatePublishedFrameState();
                SetLifecycleState(
                    session is null ? ArCoreLifecycleState.Uninitialized : ArCoreLifecycleState.Paused,
                    ArCoreFailure.None);
                return CurrentLifecycleResult(true);
            }

            SetLifecycleState(ArCoreLifecycleState.Pausing, ArCoreFailure.None);
            bool paused = await PauseSessionCoreAsync(cancellationToken).ConfigureAwait(false);

            if (!paused)
            {
                return FailLifecycleRequest(
                    ArCoreFailureCode.SessionPauseFailed,
                    ArCoreFailureClassification.Recoverable,
                    lastSessionOperationException?.Message ??
                        "The ARCore session did not pause within its bounded deadline.");
            }

            SetLifecycleState(ArCoreLifecycleState.Paused, ArCoreFailure.None);
            return CurrentLifecycleResult(true);
        }
        catch (OperationCanceledException)
        {
            return FailLifecycleRequest(
                ArCoreFailureCode.Cancelled,
                ArCoreFailureClassification.Recoverable,
                "The ARCore pause request was cancelled.");
        }
        catch (Exception exception)
        {
            Log.Error(Tag, $"ARCore pause transition failed: {exception}");
            return FailLifecycleRequest(
                ArCoreFailureCode.SessionPauseFailed,
                ArCoreFailureClassification.Recoverable,
                exception.Message);
        }
        finally
        {
            if (gateEntered)
            {
                lifecycleGate.Release();
            }
        }
    }

    public async Task<ArCoreLifecycleResult> ShutdownAsync(
        CancellationToken cancellationToken = default)
    {
        BeginLifecycleRequest(ArCoreLifecycleTarget.Disposed, "ARCore shutdown requested");
        bool gateEntered = false;

        try
        {
            gateEntered = await EnterGateAsync(
                lifecycleGate,
                LifecycleGateTimeout,
                cancellationToken).ConfigureAwait(false);

            if (!gateEntered)
            {
                return FailLifecycleRequest(
                    ArCoreFailureCode.TimedOut,
                    ArCoreFailureClassification.Recoverable,
                    "Timed out waiting to shut down ARCore.");
            }

            if (lifecycleState == ArCoreLifecycleState.Disposed)
            {
                return CurrentLifecycleResult(true);
            }

            SetLifecycleState(ArCoreLifecycleState.Disposing, ArCoreFailure.None);
            Interlocked.Increment(ref currentSessionGeneration);

            bool cameraProcessorIdle = await Task.Run(
                () => ARCameraTextureBridge.SuspendProcessing(CameraProcessorDrainTimeout),
                cancellationToken).ConfigureAwait(false);
            if (!cameraProcessorIdle)
            {
                throw new TimeoutException("The camera draw-thread processor did not become idle.");
            }

            CancellationTokenSource? frameCancellation = frameLoopCancellation;
            Task? runningLoop = frameLoopTask;
            frameCancellation?.Cancel();

            if (runningLoop is not null)
            {
                await runningLoop.WaitAsync(
                    FrameLoopDrainTimeout,
                    cancellationToken).ConfigureAwait(false);
            }

            if (ReferenceEquals(frameLoopCancellation, frameCancellation))
            {
                frameLoopCancellation = null;
            }

            if (ReferenceEquals(frameLoopTask, runningLoop))
            {
                frameLoopTask = null;
            }

            frameCancellation?.Dispose();

            await CancelGroundAnchorRecoveryAsync(
                cancellationToken).ConfigureAwait(false);

            bool updateGateEntered = await EnterGateAsync(
                updateGate,
                UpdateGateTimeout,
                cancellationToken).ConfigureAwait(false);
            if (!updateGateEntered)
            {
                throw new TimeoutException(
                    "Timed out waiting for active ARCore session work to finish.");
            }

            try
            {
                ReleasePendingCameraFrame();
                ReleaseSpatialGroundAnchor();
                InvalidatePublishedSessionState();

                Session? currentSession = session;
                if (currentSession is not null)
                {
                    try
                    {
                        if (!sessionPaused)
                        {
                            currentSession.Pause();
                        }
                    }
                    catch (Exception pauseException)
                    {
                        Log.Warn(Tag,
                            "ARCore pause failed during shutdown; continuing with close: " +
                            $"{pauseException.GetType().Name}: {pauseException.Message}");
                    }

                    try
                    {
                        currentSession.Close();
                    }
                    finally
                    {
                        currentSession.Dispose();
                        session = null;
                    }
                }

                sessionPaused = false;
                flashlightEnabled = false;
            }
            finally
            {
                updateGate.Release();
            }

            ARCameraTextureBridge.SetDrawThreadProcessor(null);
            ARCoreVulkanImporter? currentImporter = importer;
            importer = null;
            currentImporter?.Dispose();
            lastProcessedTimestamp = long.MinValue;
            SetLifecycleState(ArCoreLifecycleState.Disposed, ArCoreFailure.None);
            LifecycleChanged = null;
            return CurrentLifecycleResult(true);
        }
        catch (OperationCanceledException)
        {
            return FailLifecycleRequest(
                ArCoreFailureCode.Cancelled,
                ArCoreFailureClassification.Recoverable,
                "The ARCore shutdown request was cancelled.");
        }
        catch (Exception exception)
        {
            Log.Error(Tag, $"ARCore shutdown failed: {exception}");
            return FailLifecycleRequest(
                ArCoreFailureCode.SessionShutdownFailed,
                ArCoreFailureClassification.Recoverable,
                exception.Message);
        }
        finally
        {
            if (gateEntered)
            {
                lifecycleGate.Release();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    public void RequestPause(string reason)
    {
        lock (lifecycleStateLock)
        {
            resumeAfterActivityPause = false;
            resumeAfterGraphicsRecreation = false;
        }

        TrackTransition(PauseAsync(), reason);
    }

    public void RequestShutdown(string reason) =>
        TrackTransition(ShutdownAsync(), reason);

    public void NotifyActivityPaused()
    {
        lock (lifecycleStateLock)
        {
            activityIsResumed = false;
            resumeAfterActivityPause =
                desiredLifecycleState == ArCoreLifecycleTarget.Running ||
                installRequestPending ||
                resumeAfterGraphicsRecreation;
        }

        InvalidateCapabilitySnapshot("Android Activity paused.");
        InvalidatePublishedFrameState();
        TrackTransition(PauseAsync(), "Android Activity paused");
    }

    public void NotifyActivityResumed()
    {
        bool shouldResume;
        lock (lifecycleStateLock)
        {
            activityIsResumed = true;
            shouldResume =
                resumeAfterActivityPause ||
                (resumeAfterGraphicsRecreation &&
                 graphicsContext is not null);
            resumeAfterActivityPause = false;
            if (shouldResume)
            {
                resumeAfterGraphicsRecreation = false;
            }
        }

        InvalidateCapabilitySnapshot(
            "Android Activity resumed; capabilities require a fresh check.");
        if (shouldResume)
        {
            TrackTransition(
                EnsureRunningAsync(),
                installRequestPending
                    ? "Resuming pending ARCore installation"
                    : "Android Activity resumed");
        }
    }

    private async Task<bool> PauseSessionCoreAsync(CancellationToken cancellationToken)
    {
        lastSessionOperationException = null;
        try
        {
            bool cameraProcessorIdle = await Task.Run(
                () => ARCameraTextureBridge.SuspendProcessing(CameraProcessorDrainTimeout),
                cancellationToken).ConfigureAwait(false);
            if (!cameraProcessorIdle)
            {
                throw new TimeoutException(
                    "The camera draw-thread processor did not become idle during pause.");
            }

            CancellationTokenSource? cancellation = frameLoopCancellation;
            Task? runningLoop = frameLoopTask;
            cancellation?.Cancel();

            if (runningLoop is not null)
            {
                await runningLoop.WaitAsync(
                    FrameLoopDrainTimeout,
                    cancellationToken).ConfigureAwait(false);
            }

            if (ReferenceEquals(frameLoopCancellation, cancellation))
            {
                frameLoopCancellation = null;
            }

            if (ReferenceEquals(frameLoopTask, runningLoop))
            {
                frameLoopTask = null;
            }

            cancellation?.Dispose();

            await CancelGroundAnchorRecoveryAsync(
                cancellationToken).ConfigureAwait(false);

            bool updateGateEntered = await EnterGateAsync(
                updateGate,
                UpdateGateTimeout,
                cancellationToken).ConfigureAwait(false);
            if (!updateGateEntered)
            {
                throw new TimeoutException(
                    "Timed out waiting for ARCore frame work during pause.");
            }

            try
            {
                ReleasePendingCameraFrame();
                InvalidatePublishedFrameState();
                Session? currentSession = session;
                if (currentSession is not null && !sessionPaused)
                {
                    currentSession.Pause();
                }

                sessionPaused = currentSession is not null;
                flashlightEnabled = false;
            }
            finally
            {
                updateGate.Release();
            }

            Log.Debug(Tag,
                "ARCore session paused through the authoritative lifecycle owner.");
            return true;
        }
        catch (Exception exception)
        {
            lastSessionOperationException = exception;
            Log.Error(Tag, $"ARCore pause operation failed: {exception}");
            return false;
        }
    }

    private async Task<bool> ResumeSessionCoreAsync(CancellationToken cancellationToken)
    {
        lastSessionOperationException = null;
        Session? currentSession = session;
        if (currentSession is null)
        {
            lastSessionOperationException = new InvalidOperationException(
                "No retained ARCore session is available.");
            return false;
        }

        if (!sessionPaused)
        {
            ARCameraTextureBridge.ResumeProcessing();
            StartFrameLoop(Interlocked.Read(ref currentSessionGeneration));
            return true;
        }

        bool updateGateEntered = await EnterGateAsync(
            updateGate,
            UpdateGateTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!updateGateEntered)
        {
            lastSessionOperationException = new TimeoutException(
                "Timed out waiting to resume the ARCore session.");
            return false;
        }

        try
        {
            ApplyDisplayGeometryIfNeeded(currentSession);
            currentSession.Resume();
            sessionPaused = false;
            InvalidatePendingRecoveryCountdown();
            ResetResumedFrameState();
            ARCameraTextureBridge.ResumeProcessing();
        }
        catch (Exception exception)
        {
            sessionPaused = true;
            lastSessionOperationException = exception;
            Log.Error(Tag, $"ARCore resume operation failed: {exception}");
            return false;
        }
        finally
        {
            updateGate.Release();
        }

        StartFrameLoop(Interlocked.Read(ref currentSessionGeneration));
        return true;
    }

    private void ResetResumedFrameState()
    {
        hasLoggedGroundPlaneSearch = false;
        lastProcessedTimestamp = long.MinValue;
        processedFrameCount = 0;
        fpsWindowStartTimestamp = Environment.TickCount64;
        hasLoggedTransformedUv = false;
        lastSpatialPoseTelemetryLogTimestamp = long.MinValue;
        lastLoggedTrackingState = null;
        lastLoggedTrackingFailureReason = null;
    }

    private async Task<ArCoreApk.Availability> WaitForStableAvailabilityAsync(
        CancellationToken cancellationToken)
    {
        ArCoreApk.Availability availability = default!;
        for (int attempt = 0; attempt < AvailabilityRetryLimit; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            availability = await Task.Run(
                CheckAvailabilityCore,
                cancellationToken).ConfigureAwait(false);
            if (!availability.IsTransient && !availability.IsUnknown)
            {
                return availability;
            }

            await Task.Delay(
                AvailabilityRetryDelayMilliseconds,
                cancellationToken).ConfigureAwait(false);
        }

        return availability;
    }

    private async Task<ArCoreApk.InstallStatus> RequestInstallOnMainThreadAsync(
        bool userRequestedInstall,
        CancellationToken cancellationToken)
    {
        Task<ArCoreApk.InstallStatus> requestTask = MainThread.InvokeOnMainThreadAsync(
            () => ArCoreApk.Instance.RequestInstall(
                GetCurrentActivityOrThrow(),
                userRequestedInstall));
        return await requestTask.WaitAsync(
            LifecycleGateTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    private Activity GetCurrentActivityOrThrow()
    {
        Activity? currentActivity = Platform.CurrentActivity;
        if (currentActivity is null ||
            currentActivity.IsFinishing ||
            currentActivity.IsDestroyed)
        {
            throw new InvalidOperationException(
                "A current, resumed Android Activity is required for ARCore installation.");
        }

        return currentActivity;
    }

    private bool HasCameraPermission() =>
        context.CheckSelfPermission(global::Android.Manifest.Permission.Camera) ==
            Permission.Granted;

    private void PublishCapabilities(
        ArCoreApk.Availability availability,
        bool arCoreInstalled,
        string reason,
        bool? cameraAvailable = null)
    {
        Activity? currentActivity = Platform.CurrentActivity;
        bool activityAvailable = currentActivity is not null &&
            !currentActivity.IsFinishing &&
            !currentActivity.IsDestroyed;
        lock (lifecycleStateLock)
        {
            capabilitySnapshot = new ArCoreCapabilitySnapshot(
                ++capabilityVersion,
                DateTimeOffset.UtcNow,
                true,
                activityAvailable,
                HasCameraPermission(),
                cameraAvailable,
                availability.ToString(),
                availability.IsSupported,
                arCoreInstalled,
                string.Join(
                    ",",
                    global::Android.OS.Build.SupportedAbis ??
                        Array.Empty<string>()),
                session is null
                    ? null
                    : depthModeSupported,
                graphicsContext is not null,
                null,
                graphicsGeneration,
                reason);
        }
    }

    private void InvalidateCapabilitySnapshot(string reason)
    {
        lock (lifecycleStateLock)
        {
            capabilitySnapshot = capabilitySnapshot with
            {
                Version = ++capabilityVersion,
                CapturedAtUtc = DateTimeOffset.UtcNow,
                IsCurrent = false,
                Reason = reason,
            };
        }
    }

    private void RegisterGraphicsContextGeneration()
    {
        lock (lifecycleStateLock)
        {
            graphicsGeneration++;
        }

        InvalidateCapabilitySnapshot(
            "The graphics context changed; capabilities require a fresh check.");
    }

    private static ArCoreFailureCode ClassifyFailureCode(
        Exception? exception,
        ArCoreFailureCode fallback)
    {
        if (exception is null)
        {
            return fallback;
        }

        string typeName = exception.GetType().Name;
        if (typeName.Contains("NotCompatible", StringComparison.OrdinalIgnoreCase))
        {
            return ArCoreFailureCode.UnsupportedDevice;
        }

        if (typeName.Contains("NotInstalled", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("ApkTooOld", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("SdkTooOld", StringComparison.OrdinalIgnoreCase))
        {
            return ArCoreFailureCode.InstallationRequired;
        }

        if (typeName.Contains("CameraNotAvailable", StringComparison.OrdinalIgnoreCase))
        {
            return ArCoreFailureCode.CameraUnavailable;
        }

        if (exception is DllNotFoundException)
        {
            return ArCoreFailureCode.NativeBridgeUnavailable;
        }

        if (exception is TimeoutException)
        {
            return ArCoreFailureCode.TimedOut;
        }

        return fallback;
    }

    private long BeginLifecycleRequest(
        ArCoreLifecycleTarget target,
        string reason)
    {
        long generation = Interlocked.Increment(ref lifecycleRequestGeneration);
        lock (lifecycleStateLock)
        {
            desiredLifecycleState = target;
            lifecycleChangedAtUtc = DateTimeOffset.UtcNow;
        }

        Log.Debug(Tag,
            $"ARCore lifecycle request: generation={generation}, target={target}, reason='{reason}'.");
        return generation;
    }

    private bool IsLatestLifecycleRequest(
        long requestGeneration,
        ArCoreLifecycleTarget target)
    {
        lock (lifecycleStateLock)
        {
            return requestGeneration == lifecycleRequestGeneration &&
                desiredLifecycleState == target;
        }
    }

    private ArCoreLifecycleResult SupersededLifecycleRequest(long requestGeneration)
    {
        Log.Debug(Tag,
            $"Discarding stale ARCore lifecycle request: generation={requestGeneration}, " +
            $"latest={Interlocked.Read(ref lifecycleRequestGeneration)}.");
        lock (lifecycleStateLock)
        {
            ArCoreLifecycleSnapshot snapshot = CreateLifecycleSnapshotLocked() with
            {
                Failure = new ArCoreFailure(
                    ArCoreFailureCode.Superseded,
                    ArCoreFailureClassification.Recoverable,
                    "A newer lifecycle request replaced this request."),
            };
            return new ArCoreLifecycleResult(false, snapshot);
        }
    }

    private ArCoreLifecycleResult FailLifecycleRequest(
        ArCoreFailureCode code,
        ArCoreFailureClassification classification,
        string message)
    {
        if (code is ArCoreFailureCode.CameraUnavailable or
                    ArCoreFailureCode.CameraPermissionDenied)
        {
            lock (lifecycleStateLock)
            {
                capabilitySnapshot = capabilitySnapshot with
                {
                    Version = ++capabilityVersion,
                    CapturedAtUtc = DateTimeOffset.UtcNow,
                    IsCurrent = true,
                    CameraPermissionGranted = HasCameraPermission(),
                    CameraAvailable = false,
                    Reason = message,
                };
            }
        }

        ArCoreFailure failure = new(code, classification, message);
        SetLifecycleState(
            session is not null && sessionPaused
                ? ArCoreLifecycleState.Paused
                : ArCoreLifecycleState.Faulted,
            failure);
        return CurrentLifecycleResult(false);
    }

    private void SetLifecycleState(ArCoreLifecycleState state, ArCoreFailure failure)
    {
        ArCoreLifecycleSnapshot snapshot;

        lock (lifecycleStateLock)
        {
            lifecycleState = state;
            lastLifecycleFailure = failure;
            lifecycleChangedAtUtc = DateTimeOffset.UtcNow;
            snapshot = CreateLifecycleSnapshotLocked();
        }

        Log.Debug(Tag,
            $"ARCore lifecycle state: state={state}, " +
            $"requestGeneration={Interlocked.Read(ref lifecycleRequestGeneration)}, " +
            $"sessionGeneration={Interlocked.Read(ref currentSessionGeneration)}, " +
            $"failure={failure.Code}.");

        Action<ArCoreLifecycleSnapshot>? handlers =
            LifecycleChanged;

        if (handlers is null)
        {
            return;
        }

        foreach (Action<ArCoreLifecycleSnapshot> handler in
                 handlers.GetInvocationList().Cast<Action<ArCoreLifecycleSnapshot>>())
        {
            try
            {
                handler(snapshot);
            }
            catch (Exception exception)
            {
                Log.Warn(Tag,
                    "An ARCore lifecycle observer failed: " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private ArCoreLifecycleResult CurrentLifecycleResult(bool success)
    {
        lock (lifecycleStateLock)
        {
            return new ArCoreLifecycleResult(success, CreateLifecycleSnapshotLocked());
        }
    }

    private ArCoreLifecycleSnapshot CreateLifecycleSnapshotLocked() =>
        new(
            lifecycleState,
            desiredLifecycleState,
            Interlocked.Read(ref lifecycleRequestGeneration),
            Interlocked.Read(ref currentSessionGeneration),
            lifecycleChangedAtUtc,
            capabilitySnapshot,
            lastLifecycleFailure);

    private void TrackTransition(Task<ArCoreLifecycleResult> transition, string reason)
    {
        Task observed = ObserveTransitionAsync(transition, reason);
        lock (trackedTransitionLock)
        {
            trackedTransitionTask = observed;
        }
    }

    private static async Task ObserveTransitionAsync(
        Task<ArCoreLifecycleResult> transition,
        string reason)
    {
        try
        {
            ArCoreLifecycleResult result = await transition.ConfigureAwait(false);
            if (!result.Success &&
                result.Failure.Code is not ArCoreFailureCode.Superseded and
                    not ArCoreFailureCode.Cancelled)
            {
                Log.Warn(Tag,
                    $"Tracked ARCore lifecycle request did not complete: reason='{reason}', " +
                    $"failure={result.Failure.Code}, message='{result.Failure.Message}'.");
            }
        }
        catch (Exception exception)
        {
            Log.Error(Tag,
                $"Tracked ARCore lifecycle request '{reason}' faulted: {exception}");
        }
    }

    private static async Task<bool> EnterGateAsync(
        SemaphoreSlim gate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        long waitStarted = Environment.TickCount64;
        using CancellationTokenSource timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            await gate.WaitAsync(timeoutCancellation.Token).ConfigureAwait(false);

            long waitDuration = Environment.TickCount64 - waitStarted;
            if (waitDuration >= 50)
            {
                Log.Debug(Tag,
                    $"Serialized ARCore gate wait completed in {waitDuration} ms.");
            }

            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Log.Warn(Tag,
                $"Serialized ARCore gate wait exceeded {timeout.TotalMilliseconds:0} ms.");
            return false;
        }
    }

    private static void InvalidatePublishedFrameState()
    {
        ARCameraPoseBridge.Clear();
        ARDepthOcclusionBridge.Clear();
        ARCameraTextureBridge.Clear();
    }

    private static void InvalidatePublishedSessionState()
    {
        InvalidatePublishedFrameState();
        ARFloodDepthBridge.Clear("ARCore session shutdown");
        ARRouteBridge.Clear();
    }
}
