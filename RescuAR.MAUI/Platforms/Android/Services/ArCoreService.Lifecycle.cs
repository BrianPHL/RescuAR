using Android.App;
using Android.Content.PM;
using Android.Util;
using Evergine.Vulkan;
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
    private static readonly TimeSpan GraphicsTeardownTimeout = TimeSpan.FromSeconds(6);
    private const int AvailabilityRetryDelayMilliseconds = 200;
    private const int AvailabilityRetryLimit = 15;

    private readonly object lifecycleStateLock = new();
    private readonly object trackedTransitionLock = new();
    private Task trackedTransitionTask = Task.CompletedTask;
    private Task<ArCoreLifecycleResult>? shutdownTask;
    private ArCoreLifecycleState lifecycleState = ArCoreLifecycleState.Uninitialized;
    private ArCoreLifecycleTarget desiredLifecycleState = ArCoreLifecycleTarget.Paused;
    private ArCoreFailure lastLifecycleFailure = ArCoreFailure.None;
    private DateTimeOffset lifecycleChangedAtUtc = DateTimeOffset.UtcNow;
    private long lifecycleRequestGeneration;
    private long currentSessionGeneration;
    private long capabilityVersion;
    private long graphicsGeneration;
    private volatile bool installRequestPending;
    private readonly ArCoreRunIntent runIntent = new();
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

    public ARTrackingStateBridge.TrackingSnapshot TrackingSnapshot =>
        ARTrackingStateBridge.Current;

    public Task<ArCoreLifecycleResult> EnsureRunningAsync(
        CancellationToken cancellationToken = default)
    {
        lock (lifecycleStateLock)
        {
            if (runIntent.IsShutdown)
                return Task.FromResult(new ArCoreLifecycleResult(false,
                    CreateLifecycleSnapshotLocked() with
                    {
                        Failure = new ArCoreFailure(ArCoreFailureCode.SessionShutdownFailed,
                            ArCoreFailureClassification.Terminal, "The ARCore service has been shut down.")
                    }));
            runIntent.Request(true);
            return EnsureRunningCoreAsync(
                BeginLifecycleRequest(ArCoreLifecycleTarget.Running,
                    "ARCore running requested"), cancellationToken);
        }
    }

    private async Task<ArCoreLifecycleResult> EnsureRunningCoreAsync(
        long requestGeneration, CancellationToken cancellationToken)
    {
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
                    requestGeneration,
                    ArCoreFailureCode.TimedOut,
                    ArCoreFailureClassification.Recoverable,
                    "Timed out waiting for the ARCore lifecycle owner.");
            }

            if (!IsLatestLifecycleRequest(requestGeneration, ArCoreLifecycleTarget.Running))
            {
                return SupersededLifecycleRequest(requestGeneration);
            }

            if (!runIntent.ActivityResumed)
            {
                return FailLifecycleRequest(
                    requestGeneration,
                    ArCoreFailureCode.ActivityUnavailable,
                    ArCoreFailureClassification.Recoverable,
                    "The Android Activity is not resumed.");
            }

            if (!runIntent.SurfaceReady)
            {
                return FailLifecycleRequest(
                    requestGeneration,
                    ArCoreFailureCode.GraphicsUnavailable,
                    ArCoreFailureClassification.Recoverable,
                    "The Android AR surface is not ready yet.");
            }

            if (runIntent.IsShutdown)
            {
                return FailLifecycleRequest(
                    requestGeneration,
                    ArCoreFailureCode.SessionShutdownFailed,
                    ArCoreFailureClassification.Terminal,
                    "The ARCore service has already been disposed.");
            }

            if (TryGetTerminalCameraPipelineFailure(
                    out ArCoreFailure terminalCameraFailure))
            {
                return FailLifecycleRequest(
                    requestGeneration,
                    terminalCameraFailure.Code,
                    terminalCameraFailure.Classification,
                    terminalCameraFailure.Message);
            }

            if (session is not null && !sessionPaused && IsFrameLoopRunning)
            {
                ActivateRenderGenerationIfReady();
                if (!SetLifecycleState(ArCoreLifecycleState.Running, ArCoreFailure.None, requestGeneration))
                    return SupersededLifecycleRequest(requestGeneration);
                return CurrentLifecycleResult(true);
            }

            // An existing Session already passed installation, support, and
            // native-bridge checks. Resume it without repeating first-start work.
            if (session is not null)
            {
                if (!HasCameraPermission())
                {
                    return FailLifecycleRequest(
                        requestGeneration,
                        ArCoreFailureCode.CameraPermissionDenied,
                        ArCoreFailureClassification.Recoverable,
                        "Camera permission is required before ARCore can resume.");
                }

                bool resumed = await ResumeSessionCoreAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!IsLatestLifecycleRequest(requestGeneration,
                        ArCoreLifecycleTarget.Running))
                    return SupersededLifecycleRequest(requestGeneration);
                if (!resumed)
                    return FailLifecycleRequest(
                        requestGeneration,
                        ArCoreLifecyclePolicy.ClassifyFailureCode(
                            lastSessionOperationException,
                            ArCoreFailureCode.SessionResumeFailed),
                        ArCoreFailureClassification.Recoverable,
                        lastSessionOperationException?.Message ??
                            "The retained ARCore session could not resume.");

                lock (lifecycleStateLock)
                {
                    capabilitySnapshot = capabilitySnapshot with
                    {
                        Version = ++capabilityVersion,
                        CapturedAtUtc = DateTimeOffset.UtcNow,
                        IsCurrent = true,
                        ActivityAvailable = true,
                        CameraPermissionGranted = true,
                        CameraAvailable = true,
                        GraphicsReady = true,
                        GraphicsGeneration = graphicsGeneration,
                        Reason = "Retained ARCore session resumed.",
                    };
                }
                if (!SetLifecycleState(ArCoreLifecycleState.Running, ArCoreFailure.None, requestGeneration))
                    return SupersededLifecycleRequest(requestGeneration);
                return CurrentLifecycleResult(true);
            }

            SetLifecycleState(
                session is null ? ArCoreLifecycleState.Initializing : ArCoreLifecycleState.Running,
                ArCoreFailure.None, requestGeneration);

            ArCoreApk.Availability availability =
                await WaitForStableAvailabilityAsync(cancellationToken).ConfigureAwait(false);

            if (!IsLatestLifecycleRequest(requestGeneration, ArCoreLifecycleTarget.Running))
                return SupersededLifecycleRequest(requestGeneration);

            if (availability.IsTransient || availability.IsUnknown)
            {
                PublishCapabilities(availability, false,
                    "ARCore availability did not stabilize before the bounded retry ended.");
                return FailLifecycleRequest(
                    requestGeneration,
                    ArCoreFailureCode.AvailabilityPending,
                    ArCoreFailureClassification.Recoverable,
                    "ARCore availability is still being determined.");
            }

            if (availability.IsUnsupported)
            {
                PublishCapabilities(availability, false,
                    "The device reported ARCore as unsupported.");
                return FailLifecycleRequest(
                    requestGeneration,
                    ArCoreFailureCode.UnsupportedDevice,
                    ArCoreFailureClassification.Terminal,
                    "ARCore is unsupported on this device.");
            }

            if (!HasCameraPermission())
            {
                PublishCapabilities(availability, false, "Camera permission is not granted.");
                return FailLifecycleRequest(
                    requestGeneration,
                    ArCoreFailureCode.CameraPermissionDenied,
                    ArCoreFailureClassification.Recoverable,
                    "Camera permission is required before ARCore can start.");
            }

            if (graphicsContext is null)
            {
                PublishCapabilities(availability, false,
                    "The Evergine Vulkan graphics context is not ready.");
                return FailLifecycleRequest(
                    requestGeneration,
                    ArCoreFailureCode.GraphicsUnavailable,
                    ArCoreFailureClassification.Recoverable,
                    "The AR rendering surface is not ready.");
            }

            if (!EnsureNativeBridgeReadyForStart(
                    out ArCoreFailure nativeBridgeFailure))
            {
                return FailLifecycleRequest(
                    requestGeneration,
                    nativeBridgeFailure.Code,
                    nativeBridgeFailure.Classification,
                    nativeBridgeFailure.Message);
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
                        requestGeneration,
                        ArCoreFailureCode.ActivityUnavailable,
                        ArCoreFailureClassification.Recoverable,
                        exception.Message);
                }
                catch (Exception exception)
                {
                    return FailLifecycleRequest(
                        requestGeneration,
                        ArCoreLifecyclePolicy.ClassifyFailureCode(exception, ArCoreFailureCode.InstallationFailed),
                        ArCoreFailureClassification.Recoverable,
                        $"ARCore installation check failed: {exception.Message}");
                }

                if (!IsLatestLifecycleRequest(requestGeneration, ArCoreLifecycleTarget.Running))
                    return SupersededLifecycleRequest(requestGeneration);

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
                            "Complete the ARCore installation, then return to RescuAR."), requestGeneration);
                    return CurrentLifecycleResult(false);
                }

                if (installStatus != ArCoreApk.InstallStatus.Installed)
                {
                    return FailLifecycleRequest(
                        requestGeneration,
                        ArCoreFailureCode.InstallationFailed,
                        ArCoreFailureClassification.Recoverable,
                        $"Unexpected ARCore install status: {installStatus}.");
                }

                installRequestPending = false;
                if (!IsLatestLifecycleRequest(requestGeneration, ArCoreLifecycleTarget.Running))
                    return SupersededLifecycleRequest(requestGeneration);
                PublishCapabilities(availability, true,
                    "ARCore prerequisites are ready for session creation.");

                bool initialized = await Task.Run(
                    () => InitializeSessionCore(availability),
                    cancellationToken).ConfigureAwait(false);

                if (!initialized)
                {
                    ArCoreFailureCode code = ArCoreLifecyclePolicy.ClassifyFailureCode(
                        lastSessionOperationException,
                        ArCoreFailureCode.SessionInitializationFailed);
                    return FailLifecycleRequest(
                        requestGeneration,
                        code,
                        code == ArCoreFailureCode.UnsupportedDevice
                            ? ArCoreFailureClassification.Terminal
                            : ArCoreFailureClassification.Recoverable,
                        lastSessionOperationException?.Message ??
                            "ARCore session initialization failed.");
                }

                long sessionGeneration = Interlocked.Increment(ref currentSessionGeneration);

                ActivateRenderGenerationIfReady();

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
                        requestGeneration,
                        ArCoreLifecyclePolicy.ClassifyFailureCode(
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
            if (!SetLifecycleState(ArCoreLifecycleState.Running, ArCoreFailure.None, requestGeneration))
                return SupersededLifecycleRequest(requestGeneration);
            return CurrentLifecycleResult(true);
        }
        catch (OperationCanceledException)
        {
            if (!IsLatestLifecycleRequest(requestGeneration, ArCoreLifecycleTarget.Running))
                return SupersededLifecycleRequest(requestGeneration);
            return FailLifecycleRequest(
                requestGeneration,
                ArCoreFailureCode.Cancelled,
                ArCoreFailureClassification.Recoverable,
                "The ARCore start request was cancelled.");
        }
        catch (Exception exception)
        {
            if (!IsLatestLifecycleRequest(requestGeneration, ArCoreLifecycleTarget.Running))
                return SupersededLifecycleRequest(requestGeneration);
            Log.Error(Tag, $"ARCore running transition failed: {exception}");
            return FailLifecycleRequest(
                requestGeneration,
                ArCoreLifecyclePolicy.ClassifyFailureCode(exception, ArCoreFailureCode.Unknown),
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

    public Task<ArCoreLifecycleResult> PauseAsync(
        CancellationToken cancellationToken = default)
    {
        lock (lifecycleStateLock)
        {
            if (runIntent.IsShutdown)
                return Task.FromResult(CurrentLifecycleResult(lifecycleState == ArCoreLifecycleState.Disposed));
            runIntent.Request(false);
            return PauseForLifecycleAsync(cancellationToken);
        }
    }

    private async Task<ArCoreLifecycleResult> PauseForLifecycleAsync(
        CancellationToken cancellationToken = default)
    {
        long requestGeneration;
        lock (lifecycleStateLock)
        {
            if (runIntent.IsShutdown)
                return CurrentLifecycleResult(lifecycleState == ArCoreLifecycleState.Disposed);
            requestGeneration = BeginLifecycleRequest(
                ArCoreLifecycleTarget.Paused, "ARCore pause requested");
        }

        ARRenderGenerationBridge.Suspend(
            Interlocked.Read(ref currentSessionGeneration),
            Interlocked.Read(ref graphicsGeneration));

        ARCameraTextureBridge.SuspendProcessing(TimeSpan.Zero);
        InvalidatePublishedPausedState(
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
                    requestGeneration,
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
                InvalidatePublishedPausedState(
                    "ARCore session is paused");
                SetLifecycleState(
                    session is null ? ArCoreLifecycleState.Uninitialized : ArCoreLifecycleState.Paused,
                    ArCoreFailure.None, requestGeneration);
                return CurrentLifecycleResult(true);
            }

            SetLifecycleState(ArCoreLifecycleState.Pausing, ArCoreFailure.None, requestGeneration);
            bool paused = await PauseSessionCoreAsync(cancellationToken).ConfigureAwait(false);

            if (!IsLatestLifecycleRequest(requestGeneration, ArCoreLifecycleTarget.Paused))
                return SupersededLifecycleRequest(requestGeneration);

            if (!paused)
            {
                return FailLifecycleRequest(
                    requestGeneration,
                    ArCoreFailureCode.SessionPauseFailed,
                    ArCoreFailureClassification.Recoverable,
                    lastSessionOperationException?.Message ??
                        "The ARCore session did not pause within its bounded deadline.");
            }

            if (!SetLifecycleState(ArCoreLifecycleState.Paused, ArCoreFailure.None, requestGeneration))
                return SupersededLifecycleRequest(requestGeneration);
            return CurrentLifecycleResult(true);
        }
        catch (OperationCanceledException)
        {
            if (!IsLatestLifecycleRequest(requestGeneration, ArCoreLifecycleTarget.Paused))
                return SupersededLifecycleRequest(requestGeneration);
            return FailLifecycleRequest(
                requestGeneration,
                ArCoreFailureCode.Cancelled,
                ArCoreFailureClassification.Recoverable,
                "The ARCore pause request was cancelled.");
        }
        catch (Exception exception)
        {
            if (!IsLatestLifecycleRequest(requestGeneration, ArCoreLifecycleTarget.Paused))
                return SupersededLifecycleRequest(requestGeneration);
            Log.Error(Tag, $"ARCore pause transition failed: {exception}");
            return FailLifecycleRequest(
                requestGeneration,
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

    public Task<ArCoreLifecycleResult> ShutdownAsync(
        CancellationToken cancellationToken = default)
    {
        lock (lifecycleStateLock)
        {
            if (shutdownTask is { IsCompleted: false })
                return shutdownTask.WaitAsync(cancellationToken);
            if (lifecycleState == ArCoreLifecycleState.Disposed)
                return Task.FromResult(CurrentLifecycleResult(true));
            runIntent.Shutdown();
            long requestGeneration = BeginLifecycleRequest(ArCoreLifecycleTarget.Disposed, "ARCore shutdown requested");
            return shutdownTask = ShutdownCoreAsync(requestGeneration, cancellationToken);
        }
    }

    private async Task<ArCoreLifecycleResult> ShutdownCoreAsync(long requestGeneration,
        CancellationToken cancellationToken)
    {
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
                    requestGeneration,
                    ArCoreFailureCode.TimedOut,
                    ArCoreFailureClassification.Recoverable,
                    "Timed out waiting to shut down ARCore.");
            }

            if (lifecycleState == ArCoreLifecycleState.Disposed)
            {
                return CurrentLifecycleResult(true);
            }

            SetLifecycleState(ArCoreLifecycleState.Disposing, ArCoreFailure.None, requestGeneration);
            Interlocked.Increment(ref currentSessionGeneration);

            VKGraphicsContext? shutdownGraphicsContext;
            long shutdownGraphicsGeneration;

            lock (graphicsTeardownLock)
            {
                shutdownGraphicsContext =
                    graphicsContext ?? graphicsTeardownContext;

                shutdownGraphicsGeneration =
                    Interlocked.Read(ref graphicsGeneration);

                graphicsContext = null;

                if (shutdownGraphicsContext is not null)
                {
                    graphicsTeardownContext = shutdownGraphicsContext;
                    graphicsTeardownGeneration =
                        shutdownGraphicsGeneration;
                }
            }

            ARRenderGenerationBridge.Invalidate();

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

            if (shutdownGraphicsContext is not null)
            {
                Task drawThreadTeardown =
                    ARCameraTextureBridge.RequestDrawThreadTeardownAsync(
                        shutdownGraphicsGeneration,
                        () => TeardownGraphicsResourcesOnDrawThread(
                            shutdownGraphicsContext,
                            shutdownGraphicsGeneration));

                await drawThreadTeardown.WaitAsync(
                    GraphicsTeardownTimeout,
                    cancellationToken).ConfigureAwait(false);
            }
            else if (importer is not null)
            {
                throw new InvalidOperationException(
                    "The ARCore Vulkan importer still exists after its " +
                    "graphics context became unavailable. Refusing an " +
                    "off-thread resource teardown.");
            }

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

            lastProcessedTimestamp = long.MinValue;
            ARRenderGenerationBridge.Invalidate();

            lock (graphicsTeardownLock)
            {
                graphicsTeardownContext = null;
                graphicsTeardownTask = null;
            }

            SetLifecycleState(ArCoreLifecycleState.Disposed, ArCoreFailure.None, requestGeneration);
            LifecycleChanged = null;
            return CurrentLifecycleResult(true);
        }
        catch (OperationCanceledException)
        {
            return FailLifecycleRequest(
                requestGeneration,
                ArCoreFailureCode.Cancelled,
                ArCoreFailureClassification.Recoverable,
                "The ARCore shutdown request was cancelled.");
        }
        catch (Exception exception)
        {
            Log.Error(Tag, $"ARCore shutdown failed: {exception}");
            return FailLifecycleRequest(
                requestGeneration,
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

    public void RequestPause(string reason) =>
        TrackTransition(PauseAsync(), reason);

    /// <summary>Transient surface loss retains the Session and view intent.</summary>
    public void NotifyGraphicsSurfaceLost()
    {
        Task<ArCoreLifecycleResult> transition;
        lock (lifecycleStateLock)
        {
            runIntent.SetSurfaceReady(false);
            transition = PauseForLifecycleAsync();
        }
        TrackTransition(transition, "Android AR surface lost");
    }

    /// <summary>Called after the graphics owner restores the swap chain.</summary>
    public void NotifyGraphicsSurfaceReady()
    {
        lock (lifecycleStateLock)
        {
            runIntent.SetSurfaceReady(true);
            ResumeWhenReadyLocked("Android AR surface resized and ready");
        }
    }

    // Keep registration and request generation under the same lock so a
    // late automatic resume cannot supersede an explicit Camera-tab exit.
    private void ResumeWhenReadyLocked(string reason)
    {
        if (!runIntent.ShouldRun || graphicsContext is null ||
            lifecycleState is ArCoreLifecycleState.Disposing or ArCoreLifecycleState.Disposed)
            return;

        TrackTransition(
            EnsureRunningCoreAsync(
                BeginLifecycleRequest(ArCoreLifecycleTarget.Running, reason),
                CancellationToken.None),
            reason);
    }

    public void RequestShutdown(string reason) =>
        TrackTransition(ShutdownAsync(), reason);

    public void NotifyActivityPaused()
    {
        Task<ArCoreLifecycleResult> transition;
        lock (lifecycleStateLock)
        {
            runIntent.SetActivityResumed(false);
            InvalidateCapabilitySnapshot("Android Activity paused.");
            transition = PauseForLifecycleAsync();
        }
        TrackTransition(transition, "Android Activity paused");
    }

    public void NotifyActivityResumed()
    {
        lock (lifecycleStateLock)
        {
            runIntent.SetActivityResumed(true);
            InvalidateCapabilitySnapshot(
                "Android Activity resumed; capabilities require a fresh check.");
            ResumeWhenReadyLocked(
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

            ARRenderGenerationBridge.Suspend(
                Interlocked.Read(ref currentSessionGeneration),
                Interlocked.Read(ref graphicsGeneration));

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
                InvalidatePublishedPausedState(
                    "ARCore session paused");
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

    [System.Diagnostics.Conditional("RESCUAR_DIAGNOSTICS")]
    private void LogNavigationLifecycleFrame(ArCoreLifecycleState state)
    {
        var frame = ARCameraPoseBridge.CurrentFrame;
        Log.Info(Tag, $"NAV_REFERENCE_LIFECYCLE state={state}; session={frame.Generation.SessionGeneration}; " +
            $"frameNs={frame.FrameTimestamp}; fresh={frame.IsFresh}; tracking={frame.IsTracking}; " +
            $"trackingEpoch={frame.TrackingEpoch}; worldFrame={frame.WorldFrame}; " +
            $"ground={frame.Anchor.ReferenceGeneration}; anchorHasRotation={frame.Anchor.HasRotation}; anchorQ={frame.Anchor.Rotation}.");
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
            ActivateRenderGenerationIfReady();
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
            if (!runIntent.ShouldRun)
            {
                lastSessionOperationException = new OperationCanceledException(
                    "The camera view or Android surface is no longer active.");
                return false;
            }
            currentSession.Resume();
            sessionPaused = false;
            InvalidatePendingRecoveryCountdown();
            ResetResumedFrameState();
            ActivateRenderGenerationIfReady();
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
                AHardwareBufferInterop.CurrentReadiness.WasTested
                    ? AHardwareBufferInterop.CurrentReadiness.IsReady
                    : null,
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

    private long RegisterGraphicsContextGeneration()
    {
        long generation =
            Interlocked.Increment(ref graphicsGeneration);

        InvalidateCapabilitySnapshot(
            "The graphics context changed; capabilities require a fresh check.");

        return generation;
    }

    private long BeginLifecycleRequest(
        ArCoreLifecycleTarget target,
        string reason)
    {
        long generation;
        lock (lifecycleStateLock)
        {
            generation = ++lifecycleRequestGeneration;
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
            return ArCoreLifecyclePolicy.IsLatestRequest(
                requestGeneration,
                target,
                lifecycleRequestGeneration,
                desiredLifecycleState);
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
        long requestGeneration,
        ArCoreFailureCode code,
        ArCoreFailureClassification classification,
        string message)
    {
        ArCoreFailure failure = new(code, classification, message);
        bool applied = SetLifecycleState(
            ArCoreLifecyclePolicy.GetFailureState(session is not null, sessionPaused),
            failure, requestGeneration, () =>
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
            else if (code is ArCoreFailureCode.NativeBridgeUnavailable or
                             ArCoreFailureCode.CameraPassthroughUnavailable or
                             ArCoreFailureCode.RendererUnavailable)
            {
                lock (lifecycleStateLock)
                {
                    capabilitySnapshot = capabilitySnapshot with
                    {
                        Version = ++capabilityVersion,
                        CapturedAtUtc = DateTimeOffset.UtcNow,
                        IsCurrent = true,
                        NativeBridgeReady =
                            code == ArCoreFailureCode.NativeBridgeUnavailable
                                ? false
                                : capabilitySnapshot.NativeBridgeReady,
                        Reason = message,
                    };
                }
            }

        });
        return applied ? CurrentLifecycleResult(false) : SupersededLifecycleRequest(requestGeneration);
    }

    private bool SetLifecycleState(ArCoreLifecycleState state, ArCoreFailure failure,
        long? requestGeneration = null, Action? updateCapabilitiesLocked = null)
    {
        ArCoreLifecycleSnapshot snapshot;
        ArCoreLifecycleState previousState;

        lock (lifecycleStateLock)
        {
            if (!ArCoreLifecyclePolicy.CanApplyState(lifecycleState, state,
                    requestGeneration, lifecycleRequestGeneration)) return false;
            updateCapabilitiesLocked?.Invoke();
            previousState = lifecycleState;
            lifecycleState = state;
            lastLifecycleFailure = failure;
            lifecycleChangedAtUtc = DateTimeOffset.UtcNow;
            snapshot = CreateLifecycleSnapshotLocked();
        }

        if (!ArCoreLifecyclePolicy.IsLegalTransition(previousState, state))
        {
            Log.Warn(
                Tag,
                "ARCORE_LIFECYCLE_ILLEGAL_TRANSITION " +
                $"from={previousState}; to={state}; failure={failure.Code}.");
        }

        Log.Debug(Tag,
            $"ARCore lifecycle state: state={state}, " +
            $"requestGeneration={Interlocked.Read(ref lifecycleRequestGeneration)}, " +
            $"sessionGeneration={Interlocked.Read(ref currentSessionGeneration)}, " +
            $"failure={failure.Code}.");
        LogNavigationLifecycleFrame(state);

        Action<ArCoreLifecycleSnapshot>? handlers =
            LifecycleChanged;

        if (handlers is null)
        {
            return true;
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
        return true;
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

    private void InvalidatePublishedFrameState()
    {
        ARCameraPoseBridge.Clear();
        ARDepthOcclusionBridge.Clear();
        ARCameraTextureBridge.Clear();
    }

    private void InvalidatePublishedPausedState(
        string reason)
    {
        ARTrackingStateBridge.PublishLifecycleTransition(
            Interlocked.Read(ref currentSessionGeneration),
            lifecycleState.ToString(),
            reason,
            depthModeEnabled);

        ARGroundStateBridge.Suspend(
            ARRenderGenerationBridge.Current,
            reason);
        InvalidatePublishedFrameState();
        ARFloodDepthBridge.Clear(reason);
    }

    private void InvalidatePublishedSessionState()
    {
        ARTrackingStateBridge.PublishLifecycleTransition(
            Interlocked.Read(ref currentSessionGeneration),
            ArCoreLifecycleState.Disposed.ToString(),
            "ARCore session shutdown",
            depthModeEnabled);

        ARGroundStateBridge.Clear("ARCore session shutdown");
        InvalidatePublishedFrameState();
        ARFloodDepthBridge.Clear("ARCore session shutdown");
        ARRouteBridge.Clear();
    }
}
