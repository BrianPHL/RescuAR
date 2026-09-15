using Android.App;
using Android.Content;
using Android.Hardware;
using Android.Util;
using Evergine.Vulkan;
using Google.AR.Core;
using RescuAR.AR;
using RescuAR.MAUI.Services;
using RescuAR.Navigation.Projection;
using Frame = Google.AR.Core.Frame;
using ArCoreCamera = Google.AR.Core.Camera;
using ArCorePlane = Google.AR.Core.Plane;

namespace RescuAR.MAUI.Platforms.Android.Services;

public sealed partial class ArCoreService : IArCoreService
{
    private const string Tag =
        "RescuAR-ARCore";

    private readonly Context context;
    private readonly Activity activity;

    /*
     * Prevents the automatic frame loop and the existing manual Update()
     * button path from calling Session.Update() at the same time.
     *
     * Display-geometry changes are also applied while this gate is held so
     * Session.SetDisplayGeometry() and Session.Update() do not race.
     */
    private readonly SemaphoreSlim updateGate =
        new(1, 1);

    /*
     * Serializes Camera-tab pause/resume transitions.
     *
     * MAUI page lifecycle callbacks can overlap during fast tab changes. Only
     * one ARCore lifecycle transition may run at a time.
     */
    private readonly SemaphoreSlim lifecycleGate =
        new(1, 1);

    /*
     * Protects display-geometry values supplied by the Android/Evergine
     * presentation layer.
     *
     * The UI thread may change these values while the ARCore frame loop is
     * running on its worker thread.
     */
    private readonly object displayGeometryLock =
        new();

    private Session? session;
    private VKGraphicsContext? graphicsContext;
    private ARCoreVulkanImporter? importer;

    /*
     * Latest-frame handoff between the ARCore worker and Evergine's draw
     * thread. The ARCore worker never submits Vulkan work directly.
     *
     * Only one camera frame is retained. If ARCore produces a newer frame
     * before the draw thread consumes the previous one, the older pending
     * HardwareBuffer is closed immediately.
     */
    private readonly object pendingCameraFrameLock =
        new();

    private PendingCameraFrame? pendingCameraFrame;

    private CancellationTokenSource? frameLoopCancellation;
    private Task? frameLoopTask;

    private bool hasInspectedHardwareBuffer;

    private volatile bool captureCpuDiagnosticRequested;

    /*
     * ARCore BLOCKING mode can return the most recent frame after its
     * built-in timeout when no new camera image arrived. Tracking the
     * timestamp lets us avoid importing/redrawing the same HardwareBuffer
     * unnecessarily.
     */
    private long lastProcessedTimestamp =
        long.MinValue;

    /*
     * Android/ARCore display geometry.
     *
     * Rotation values follow Android Surface rotation:
     *
     * 0 = ROTATION_0
     * 1 = ROTATION_90
     * 2 = ROTATION_180
     * 3 = ROTATION_270
     *
     * Width and height must describe the viewport displaying the AR camera,
     * not the 1920x1080 camera HardwareBuffer itself.
     */
    private int requestedDisplayRotation;
    private int requestedDisplayWidth;
    private int requestedDisplayHeight;

    /*
     * Geometry currently applied to the ARCore Session.
     */
    private int appliedDisplayRotation =
        int.MinValue;

    private int appliedDisplayWidth =
        -1;

    private int appliedDisplayHeight =
        -1;

    private bool displayGeometryAvailable;
    private bool displayGeometryDirty;

    /*
     * VIEW_NORMALIZED coordinates for the four corners used by our Vulkan
     * fullscreen camera pass.
     *
     * Order:
     *
     * TL ---- TR
     * |       |
     * |       |
     * BL ---- BR
     *
     * This order must match the UV-corner interpretation used by the
     * modified ARCoreVulkanImporter / camera fragment shader.
     */
    private static readonly float[] ViewNormalizedCameraUv =
    {
        // Top-left
        0.0f, 0.0f,

        // Top-right
        1.0f, 0.0f,

        // Bottom-left
        0.0f, 1.0f,

        // Bottom-right
        1.0f, 1.0f
    };

    private bool hasLoggedTransformedUv;

    private static bool textureIntrinsicsLogged;

    /*
     * V7 FLOOD DEPTH OCCLUSION
     * -------------------------
     * Smoothed ARCore depth is copied only while a local flood visualization
     * is active or nearby route geometry can benefit from obstacle occlusion.
     * Flood copies are capped at 10 Hz; route-only copies use 5 Hz. The camera
     * and ARCore tracking remain at their normal cadence.
     */
    private long lastDepthOcclusionPublishTimestamp =
        long.MinValue;

    private const long DepthOcclusionPublishIntervalNanoseconds =
        100_000_000L;

    private const long RouteDepthOcclusionPublishIntervalNanoseconds =
        200_000_000L;

    private bool depthOcclusionAvailabilityLogged;

    private long processedFrameCount;
    private long fpsWindowStartTimestamp =
        Environment.TickCount64;

    private const string SpatialPoseTag =
        "RescuAR-ARPose";

    /*
     * GROUND ACQUISITION V4
     * ---------------------
     * Prefer a real upward-facing ARCore Plane whenever one is available.
     * On Depth-capable devices, a lower-center DepthPoint acts as a fallback
     * for low-texture floors (for example glossy or repetitive tiles) where
     * plane-polygon growth can otherwise take many seconds.
     *
     * Depth fallback is intentionally conservative: the candidate must be
     * below the camera, approximately horizontal, and supported by a rolling
     * confidence window before an Anchor is created.
     */
    private static readonly (float XOffset, float ZOffset)[]
        GroundPlaneWorldDownSearchPattern =
    {
        // Sample the same ARCore frame at the camera and four nearby points.
        (0.00f, 0.00f),
        (0.45f, 0.00f),
        (-0.45f, 0.00f),
        (0.00f, 0.45f),
        (0.00f, -0.45f)
    };

    private static readonly float[] GroundPlaneWorldDownDirection =
    {
        0.0f,
        -1.0f,
        0.0f
    };

    private static readonly (float X, float Y)[] GroundPlaneSearchPattern =
    {
        // First sample is also the DepthPoint confidence sample.
        (0.50f, 0.80f),

        // Additional lower-view samples are Plane-only fallbacks.
        (0.34f, 0.76f),
        (0.66f, 0.76f),
        (0.50f, 0.90f)
    };

    private const int GroundDepthCandidateSampleIndex =
        0;

    private const long GroundPlaneSearchIntervalMilliseconds =
        125;

    private const long GroundPlaneSearchProgressLogIntervalMilliseconds =
        2000;

    private const int GroundDepthConfidenceWindowSweeps =
        5;

    private const int GroundDepthValidSweepsRequired =
        3;

    private const float GroundDepthMinimumNormalY =
        0.70f;

    private const float GroundDepthMaximumYDeltaMeters =
        0.08f;

    private const float GroundDepthMaximumHorizontalDeltaMeters =
        0.35f;

    private const int GroundPlaneRequiredSpatialSamples =
        2;

    private const float GroundPlaneMaximumYDeltaMeters =
        0.12f;

    private const float GroundPlaneMinimumSampleSeparationMeters =
        0.30f;

    private long nextGroundPlaneSearchTimestamp =
        long.MinValue;

    private long groundPlaneSearchStartedTimestamp =
        long.MinValue;

    private long lastGroundPlaneSearchProgressLogTimestamp =
        long.MinValue;

    private int groundPlaneSearchSweepCount;

    private int groundPlaneSearchHitTestCount;

    private bool depthModeEnabled;

    private bool hasGroundDepthCandidate;

    private float groundDepthCandidateX;
    private float groundDepthCandidateY;
    private float groundDepthCandidateZ;

    private readonly bool[] groundDepthConfidenceWindow =
        new bool[GroundDepthConfidenceWindowSweeps];

    private int groundDepthConfidenceWindowCount;
    private int groundDepthConfidenceWindowIndex;
    private int groundDepthConfidenceValidSweepCount;

    private bool hasGroundPlaneSweepCandidate;
    private float groundPlaneSweepCandidateX;
    private float groundPlaneSweepCandidateY;
    private float groundPlaneSweepCandidateZ;
    private int groundPlaneSweepCandidateSupportCount;

    /*
     * Match the Camera3D clipping planes serialized in MyScene.wescene.
     * These values are supplied to ARCore when generating the projection.
     */
    private const float SpatialProjectionNearPlane =
        0.1f;

    private const float SpatialProjectionFarPlane =
        1000.0f;

    private Google.AR.Core.Anchor? spatialGroundAnchor;

    /*
     * Ground-anchor recovery is owned exclusively by
     * ArCoreService.AnchorRecovery.cs. Do not add a second resume-time
     * deadline here: overlapping recovery policies previously detached
     * anchors too aggressively after brief tracking interruptions.
     */

    private bool hasLoggedGroundPlaneSearch;

    private long lastSpatialPoseTelemetryLogTimestamp =
        long.MinValue;

    private const long SpatialPoseTelemetryLogIntervalMilliseconds =
        1000;

    /*
     * Tracking diagnostics are transition-based so Logcat records the exact
     * moment ARCore enters/leaves TRACKING without flooding every frame.
     */
    private string? lastLoggedTrackingState;
    private string? lastLoggedTrackingFailureReason;

    private volatile bool sessionPaused;

    public bool IsSessionPaused =>
        sessionPaused;

    public Session? Session =>
        session;

    public bool IsInitialized =>
        session is not null;

    public bool IsFrameLoopRunning =>
        frameLoopTask is not null &&
        !frameLoopTask.IsCompleted;

    public ArCoreService()
    {
        context =
            global::Android.App.Application.Context;

        activity =
            Platform.CurrentActivity
            ?? throw new InvalidOperationException(
                "Current Android Activity is unavailable.");

        Log.Debug(
            Tag,
            "ArCoreService constructed.");
    }

    public ArCoreApk.Availability CheckAvailability()
    {
        Log.Debug(
            Tag,
            "Checking ARCore availability...");

        ArCoreApk.Availability availability =
            ArCoreApk.Instance
                .CheckAvailability(
                    context);

        Log.Debug(
            Tag,
            $"ARCore Availability: {availability}");

        Log.Debug(
            Tag,
            $"IsSupported: {availability.IsSupported}");

        Log.Debug(
            Tag,
            $"IsUnsupported: {availability.IsUnsupported}");

        Log.Debug(
            Tag,
            $"IsTransient: {availability.IsTransient}");

        Log.Debug(
            Tag,
            $"IsUnknown: {availability.IsUnknown}");

        return availability;
    }

    public ArCoreApk.InstallStatus RequestInstall()
    {
        Log.Debug(
            Tag,
            "Requesting ARCore installation...");

        ArCoreApk.InstallStatus installStatus =
            ArCoreApk.Instance
                .RequestInstall(
                    activity,
                    true);

        Log.Debug(
            Tag,
            $"ARCore Install Status: {installStatus}");

        return installStatus;
    }

    public bool Initialize()
    {
        Log.Debug(
            Tag,
            "========================================");

        Log.Debug(
            Tag,
            "ARCore initialization started.");

        Log.Debug(
            Tag,
            "========================================");

        if (session is not null)
        {
            Log.Debug(
                Tag,
                "ARCore Session already exists.");

            return true;
        }

        try
        {
            Log.Debug(
                Tag,
                "STEP 1: Checking ARCore availability.");

            ArCoreApk.Availability availability =
                CheckAvailability();

            if (availability.IsUnsupported)
            {
                Log.Error(
                    Tag,
                    "ARCore is unsupported on this device.");

                return false;
            }

            Log.Debug(
                Tag,
                "STEP 2: Requesting ARCore installation.");

            ArCoreApk.InstallStatus installStatus =
                RequestInstall();

            if (installStatus ==
                ArCoreApk.InstallStatus.InstallRequested)
            {
                Log.Warn(
                    Tag,
                    "ARCore installation was requested.");

                Log.Warn(
                    Tag,
                    "Initialization must be attempted again " +
                    "after installation.");

                return false;
            }

            if (installStatus !=
                ArCoreApk.InstallStatus.Installed)
            {
                Log.Error(
                    Tag,
                    $"Unexpected ARCore install status: " +
                    $"{installStatus}");

                return false;
            }

            Log.Debug(
                Tag,
                "ARCore installation status: INSTALLED.");

            Log.Debug(
                Tag,
                "STEP 3: Creating ARCore Session.");

            session =
                new Session(
                    context);

            Log.Debug(
                Tag,
                "ARCore Session created successfully.");

            InspectSupportedCameraConfigurations(
                session);

            SelectThirtyFpsCameraConfig(
                session);

            /*
             * The high-resolution CPU camera configuration was only needed
             * for the completed one-frame JPEG diagnostic. Leave ARCore on
             * its normal camera configuration for the real-time passthrough.
             */

            Log.Debug(
                Tag,
                "STEP 4: Creating ARCore Config.");

            using Google.AR.Core.Config config =
                new(
                    session);

            Log.Debug(
                Tag,
                "ARCore Config created.");

            Log.Debug(
                Tag,
                "STEP 5: Configuring TextureUpdateMode.");

            config.SetTextureUpdateMode(
                Google.AR.Core.Config
                    .TextureUpdateMode
                    .ExposeHardwareBuffer);

            Log.Debug(
                Tag,
                "TextureUpdateMode = EXPOSE_HARDWARE_BUFFER.");

            Log.Debug(
                Tag,
                "STEP 6: Configuring UpdateMode.");

            config.SetUpdateMode(
                Google.AR.Core.Config
                    .UpdateMode
                    .Blocking);

            Log.Debug(
                Tag,
                "UpdateMode = BLOCKING.");

            Log.Debug(
                Tag,
                "STEP 7: Configuring PlaneFindingMode.");

            config.SetPlaneFindingMode(
                Google.AR.Core.Config
                    .PlaneFindingMode
                    .Horizontal);

            Log.Debug(
                Tag,
                "PlaneFindingMode = HORIZONTAL.");

            Log.Debug(
                Tag,
                "STEP 8: Configuring DepthMode when supported.");

            depthModeEnabled =
                TryConfigureAutomaticDepth(
                    session,
                    config);

            Log.Debug(
                Tag,
                "STEP 9: Applying ARCore configuration.");

            session.Configure(
                config);

            Log.Debug(
                Tag,
                "ARCore configuration applied successfully.");

            Log.Debug(
                Tag,
                "STEP 10: Applying initial display geometry.");

            TryCaptureInitialDisplayGeometry();

            ApplyDisplayGeometryIfNeeded(
                session);

            Log.Debug(
                Tag,
                "STEP 11: Resuming ARCore Session.");

            session.Resume();

            sessionPaused =
                false;

            Log.Debug(
                Tag,
                "ARCore Session resumed successfully.");

            /*
             * CPU JPEG diagnostics are disabled during normal passthrough.
             * They can still be requested explicitly through
             * RequestCpuDiagnosticCapture() when needed.
             */
            captureCpuDiagnosticRequested =
                false;

            lastProcessedTimestamp =
                long.MinValue;

            hasLoggedTransformedUv =
                false;

            ReleaseSpatialGroundAnchor();

            hasLoggedGroundPlaneSearch =
                false;

            ResetGroundPlaneSearchState();

            lastSpatialPoseTelemetryLogTimestamp =
                long.MinValue;

            lastLoggedTrackingState =
                null;

            lastLoggedTrackingFailureReason =
                null;

            ARCameraPoseBridge.Clear();

            // A new ARCore Session creates a new arbitrary world frame. Never
            // carry a depth image/ground baseline from the previous Session
            // into the new camera feed.
            ARDepthOcclusionBridge.Clear();

            lastDepthOcclusionPublishTimestamp =
                long.MinValue;

            depthOcclusionAvailabilityLogged =
                false;

            Log.Debug(
                Tag,
                "STEP 11: Starting automatic ARCore frame loop.");

            StartFrameLoop();

            Log.Debug(
                Tag,
                "========================================");

            Log.Debug(
                Tag,
                "ARCore initialization COMPLETE.");

            Log.Debug(
                Tag,
                "========================================");

            return true;
        }
        catch (Exception exception)
        {
            Log.Error(
                Tag,
                $"ARCore initialization FAILED: {exception}");

            StopFrameLoop();

            CloseSessionAfterFailure();

            return false;
        }
    }

    /// <summary>
    /// Temporarily releases the physical ARCore camera when the Camera tab is
    /// hidden while preserving the ARCore Session, current spatial anchor,
    /// Vulkan importer, and navigation/guidance state.
    /// </summary>
    public async Task PauseCameraSessionAsync()
    {
        await lifecycleGate
            .WaitAsync()
            .ConfigureAwait(false);

        try
        {
            Session? currentSession =
                session;

            if (currentSession is null)
            {
                return;
            }

            if (sessionPaused)
            {
                Log.Debug(
                    Tag,
                    "ARCore camera session is already paused.");

                return;
            }

            Log.Debug(
                Tag,
                "Pausing ARCore camera session...");

            /*
             * Cancel new automatic updates first. Session.Update() itself is
             * blocking, so wait for the worker to finish its current update
             * before calling Session.Pause().
             */
            CancellationTokenSource? cancellation =
                frameLoopCancellation;

            Task? runningTask =
                frameLoopTask;

            if (cancellation is not null)
            {
                try
                {
                    cancellation.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Already being cleaned up.
                }
            }

            if (runningTask is not null)
            {
                try
                {
                    await runningTask
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected during normal Camera-tab shutdown.
                }
            }

            frameLoopTask =
                null;

            frameLoopCancellation =
                null;

            cancellation?.Dispose();

            /*
             * Final barrier against the manual Update() path. The spatial
             * anchor is intentionally retained; only the pending camera frame
             * is released.
             */
            await updateGate
                .WaitAsync()
                .ConfigureAwait(false);

            try
            {
                ReleasePendingCameraFrame();

                ARCameraTextureBridge.Clear();

                currentSession.Pause();

                sessionPaused =
                    true;

                /*
                 * The anchor is intentionally retained across the pause.
                 * A recovery grace period is started only after Session.Resume().
                 */
            }
            finally
            {
                updateGate.Release();
            }

            Log.Debug(
                Tag,
                "ARCore camera session paused. Physical camera released; " +
                "ARCore Session and guidance state retained.");
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Reopens the physical camera and restarts ARCore frame production when
    /// the Camera tab becomes visible again. The existing Session and spatial
    /// anchor are reused so ongoing guidance is not cancelled.
    /// </summary>
    public async Task<bool> ResumeCameraSessionAsync()
    {
        await lifecycleGate
            .WaitAsync()
            .ConfigureAwait(false);

        try
        {
            Session? currentSession =
                session;

            if (currentSession is null)
            {
                Log.Warn(
                    Tag,
                    "Cannot resume ARCore camera because Session is null.");

                return false;
            }

            if (!sessionPaused)
            {
                StartFrameLoop();

                Log.Debug(
                    Tag,
                    "ARCore camera session is already resumed.");

                return true;
            }

            Log.Debug(
                Tag,
                "Resuming ARCore camera session...");

            await updateGate
                .WaitAsync()
                .ConfigureAwait(false);

            try
            {
                /*
                 * The Evergine viewport may have changed while Camera was
                 * hidden. Apply any pending geometry before reopening camera
                 * frame production.
                 */
                ApplyDisplayGeometryIfNeeded(
                    currentSession);

                currentSession.Resume();

                sessionPaused =
                    false;

                /*
                 * Retain the current AR anchor across Session.Resume(). The
                 * queued recovery component gives PAUSED anchors a generous
                 * relocalization grace period and only replaces a truly stale
                 * anchor.
                 */
                InvalidatePendingRecoveryCountdown();

                hasLoggedGroundPlaneSearch =
                    false;

                /*
                 * Treat the next frame as the start of a fresh camera stream.
                 */
                lastProcessedTimestamp =
                    long.MinValue;

                processedFrameCount =
                    0;

                fpsWindowStartTimestamp =
                    Environment.TickCount64;

                hasLoggedTransformedUv =
                    false;

                lastSpatialPoseTelemetryLogTimestamp =
                    long.MinValue;

                lastLoggedTrackingState =
                    null;

                lastLoggedTrackingFailureReason =
                    null;
            }
            catch (Exception exception)
            {
                sessionPaused =
                    true;

                Log.Error(
                    Tag,
                    $"Unable to resume ARCore camera session: {exception}");

                return false;
            }
            finally
            {
                updateGate.Release();
            }

            StartFrameLoop();

            Log.Debug(
                Tag,
                "ARCore camera session resumed. Existing AR guidance state " +
                "was retained.");

            return true;
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Supplies ARCore with the geometry of the viewport in which the camera
    /// image is being rendered.
    ///
    /// rotation:
    ///     0 = ROTATION_0
    ///     1 = ROTATION_90
    ///     2 = ROTATION_180
    ///     3 = ROTATION_270
    /// </summary>
    public void SetDisplayGeometry(
        int rotation,
        int width,
        int height)
    {
        if (rotation < 0 ||
            rotation > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rotation),
                "Android display rotation must be between 0 and 3.");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Display width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                "Display height must be greater than zero.");
        }

        bool changed;

        lock (displayGeometryLock)
        {
            changed =
                !displayGeometryAvailable ||
                requestedDisplayRotation != rotation ||
                requestedDisplayWidth != width ||
                requestedDisplayHeight != height;

            if (!changed)
            {
                return;
            }

            requestedDisplayRotation =
                rotation;

            requestedDisplayWidth =
                width;

            requestedDisplayHeight =
                height;

            displayGeometryAvailable =
                true;

            displayGeometryDirty =
                true;
        }

        hasLoggedTransformedUv =
            false;

        Log.Debug(
            Tag,
            "ARCore display geometry requested: " +
            $"rotation={rotation}, " +
            $"width={width}, " +
            $"height={height}");
    }

    public Frame? Update()
    {
        if (sessionPaused)
        {
            Log.Debug(
                Tag,
                "Manual Update() ignored because the ARCore Session is paused.");

            return null;
        }

        if (IsFrameLoopRunning)
        {
            Log.Debug(
                Tag,
                "Manual Update() ignored because the automatic " +
                "ARCore frame loop is running.");

            return null;
        }

        return UpdateFrameSerialized(
            fromAutomaticLoop: false,
            CancellationToken.None);
    }

    public void SetGraphicsContext(
        VKGraphicsContext graphicsContext)
    {
        ArgumentNullException.ThrowIfNull(
            graphicsContext);

        this.graphicsContext =
            graphicsContext;

        ARCameraTextureBridge.SetDrawThreadProcessor(
            ProcessPendingCameraFrameOnDrawThread);

        Log.Debug(
            Tag,
            "VKGraphicsContext received.");

        Log.Debug(
            Tag,
            $"VkInstance = 0x{graphicsContext.VkInstance.Handle:X}");

        Log.Debug(
            Tag,
            $"VkPhysicalDevice = 0x{graphicsContext.VkPhysicalDevice.Handle:X}");

        Log.Debug(
            Tag,
            $"VkDevice = 0x{graphicsContext.VkDevice.Handle:X}");
    }

    private void StartFrameLoop()
    {
        if (session is null)
        {
            Log.Warn(
                Tag,
                "Cannot start ARCore frame loop because Session is null.");

            return;
        }

        if (sessionPaused)
        {
            Log.Debug(
                Tag,
                "Automatic ARCore frame loop not started because Session is paused.");

            return;
        }

        if (frameLoopTask is not null &&
            !frameLoopTask.IsCompleted)
        {
            Log.Debug(
                Tag,
                "Automatic ARCore frame loop is already running.");

            return;
        }

        frameLoopCancellation?.Dispose();

        frameLoopCancellation =
            new CancellationTokenSource();

        CancellationToken cancellationToken =
            frameLoopCancellation.Token;

        frameLoopTask =
            Task.Run(
                () => RunFrameLoop(
                    cancellationToken),
                cancellationToken);

        Log.Debug(
            Tag,
            "Automatic ARCore frame loop started.");
    }

    private void RunFrameLoop(
        CancellationToken cancellationToken)
    {
        Log.Debug(
            Tag,
            "ARCore frame loop worker entered.");

        try
        {
            RefreshPowerThermalDecisionIfNeeded(
                force: true);

            while (!cancellationToken.IsCancellationRequested)
            {
                long iterationStartedTimestamp =
                    Environment.TickCount64;

                UpdateFrameSerialized(
                    fromAutomaticLoop: true,
                    cancellationToken);

                RefreshPowerThermalDecisionIfNeeded(
                    force: false);

                ApplyFrameWorkloadDelay(
                    iterationStartedTimestamp,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during normal shutdown.
        }
        catch (Exception exception)
        {
            Log.Error(
                Tag,
                $"Automatic ARCore frame loop terminated: {exception}");
        }
        finally
        {
            Log.Debug(
                Tag,
                "ARCore frame loop worker exited.");
        }
    }

    private Frame? UpdateFrameSerialized(
        bool fromAutomaticLoop,
        CancellationToken cancellationToken)
    {
        if (session is null)
        {
            if (!fromAutomaticLoop)
            {
                Log.Warn(
                    Tag,
                    "Update() called but ARCore Session is null.");
            }

            return null;
        }

        if (sessionPaused)
        {
            return null;
        }

        bool gateEntered =
            false;

        try
        {
            if (fromAutomaticLoop)
            {
                updateGate.Wait(
                    cancellationToken);
            }
            else
            {
                updateGate.Wait();
            }

            gateEntered =
                true;

            /*
             * Re-check after entering the gate. A manual Update() may have
             * started immediately before PauseCameraSessionAsync() changed
             * sessionPaused and then waited for this gate.
             */
            if (sessionPaused)
            {
                return null;
            }

            return UpdateFrameInternal(
                fromAutomaticLoop);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (gateEntered)
            {
                updateGate.Release();
            }
        }
    }

    private Frame? UpdateFrameInternal(
        bool fromAutomaticLoop)
    {
        Session? currentSession =
            session;

        if (currentSession is null)
        {
            return null;
        }

        try
        {
            ApplyDisplayGeometryIfNeeded(
                currentSession);

            if (!fromAutomaticLoop)
            {
                Log.Debug(
                    Tag,
                    "Calling ARCore Session.Update()...");
            }

            Frame? frame =
                currentSession.Update();

            if (frame is null)
            {
                Log.Warn(
                    Tag,
                    "ARCore Session.Update() returned null.");

                return null;
            }

            long timestamp =
                frame.Timestamp;

            if (timestamp ==
                lastProcessedTimestamp)
            {
                return frame;
            }

            lastProcessedTimestamp =
                timestamp;

            RecordProcessedFrame();

            ArCoreCamera camera =
                frame.Camera;

            /*
             * Capture user zoom exactly once for this ARCore frame. Camera
             * UVs, projection, depth mapping, and screen-space hit tests must
             * all use the same ratio to preserve AR registration.
             */
            float frameZoomRatio =
                cameraZoomRatio;

            PublishSpatialPose(
                frame,
                camera,
                timestamp,
                frameZoomRatio);

            LogTextureIntrinsicsOnce(
                camera);

            TryPublishDepthOcclusionFrame(
                frame,
                camera,
                timestamp,
                frameZoomRatio);

            if (captureCpuDiagnosticRequested)
            {
                bool captureFinished =
                    TryCaptureCpuDiagnosticImage(
                        frame);

                if (captureFinished)
                {
                    captureCpuDiagnosticRequested =
                        false;
                }
            }

            if (!fromAutomaticLoop)
            {
                Log.Debug(
                    Tag,
                    $"Frame Timestamp: {timestamp}");

                Log.Debug(
                    Tag,
                    $"Camera Tracking State: {camera.TrackingState}");

                Log.Debug(
                    Tag,
                    $"Tracking Failure Reason: {camera.TrackingFailureReason}");

                Log.Debug(
                    Tag,
                    $"Camera Texture Name: {frame.CameraTextureName}");
            }

            HardwareBuffer? hardwareBuffer =
                frame.HardwareBuffer;

            if (hardwareBuffer is null)
            {
                Log.Warn(
                    Tag,
                    "ARCore Frame returned a null HardwareBuffer.");

                return frame;
            }

            bool ownershipTransferred =
                false;

            try
            {
                if (!fromAutomaticLoop)
                {
                    Log.Debug(
                        Tag,
                        $"Hardware Buffer: {hardwareBuffer}");
                }

                if (!hasInspectedHardwareBuffer)
                {
                    hasInspectedHardwareBuffer =
                        true;

                    InspectHardwareBuffer(
                        hardwareBuffer);
                }

                if (graphicsContext is null)
                {
                    if (!fromAutomaticLoop)
                    {
                        Log.Warn(
                            Tag,
                            "HardwareBuffer is available, but " +
                            "VKGraphicsContext is unavailable.");
                    }

                    return frame;
                }

                float[] cameraUv =
                    TransformCameraUv(
                        frame,
                        frameZoomRatio);

                GetAppliedDisplaySize(
                    out uint outputWidth,
                    out uint outputHeight);

                QueuePendingCameraFrame(
                    hardwareBuffer,
                    cameraUv,
                    outputWidth,
                    outputHeight,
                    timestamp);

                ownershipTransferred =
                    true;

                return frame;
            }
            finally
            {
                if (!ownershipTransferred)
                {
                    CloseHardwareBuffer(
                        hardwareBuffer);
                }
            }
        }
        catch (Exception exception)
        {
            Log.Error(
                Tag,
                $"ARCore Frame Update FAILED: {exception}");

            return null;
        }
    }

    private void QueuePendingCameraFrame(
        HardwareBuffer hardwareBuffer,
        float[] cameraUv,
        uint outputWidth,
        uint outputHeight,
        long timestamp)
    {
        PendingCameraFrame replacement =
            new(
                hardwareBuffer,
                cameraUv,
                outputWidth,
                outputHeight,
                timestamp);

        PendingCameraFrame? replacedFrame;

        lock (pendingCameraFrameLock)
        {
            replacedFrame =
                pendingCameraFrame;

            pendingCameraFrame =
                replacement;
        }

        /*
         * Never allow a backlog of HardwareBuffers. If the draw thread is
         * slower than ARCore, only the newest frame is useful for passthrough.
         */
        replacedFrame?.Dispose();
    }

    private void ProcessPendingCameraFrameOnDrawThread()
    {
        PendingCameraFrame? pendingFrame;

        lock (pendingCameraFrameLock)
        {
            pendingFrame =
                pendingCameraFrame;

            pendingCameraFrame =
                null;
        }

        if (pendingFrame is null)
        {
            return;
        }

        try
        {
            VKGraphicsContext? currentGraphicsContext =
                graphicsContext;

            if (currentGraphicsContext is null)
            {
                return;
            }

            importer ??=
                CreateImporter(
                    currentGraphicsContext);

            try
            {
                var texture =
                    importer.ImportHardwareBuffer(
                        pendingFrame.HardwareBuffer,
                        pendingFrame.CameraUv,
                        pendingFrame.OutputWidth,
                        pendingFrame.OutputHeight);

                ARCameraTextureBridge.Publish(
                    texture);
            }
            catch (NotSupportedException exception)
            {
                Log.Error(
                    Tag,
                    $"Unsupported HardwareBuffer format: " +
                    $"{exception.Message}");
            }
            catch (Exception exception)
            {
                Log.Error(
                    Tag,
                    $"HardwareBuffer import failed on Evergine " +
                    $"draw thread: {exception}");
            }
        }
        finally
        {
            pendingFrame.Dispose();
        }
    }

    private void ReleasePendingCameraFrame()
    {
        PendingCameraFrame? pendingFrame;

        lock (pendingCameraFrameLock)
        {
            pendingFrame =
                pendingCameraFrame;

            pendingCameraFrame =
                null;
        }

        pendingFrame?.Dispose();
    }

    private static void CloseHardwareBuffer(
        HardwareBuffer hardwareBuffer)
    {
        try
        {
            hardwareBuffer.Close();
        }
        catch (Exception exception)
        {
            Log.Warn(
                Tag,
                "HardwareBuffer.Close() failed: " +
                $"{exception.GetType().Name}: " +
                $"{exception.Message}");
        }

        hardwareBuffer.Dispose();
    }

    private sealed class PendingCameraFrame : IDisposable
    {
        private HardwareBuffer? hardwareBuffer;

        public PendingCameraFrame(
            HardwareBuffer hardwareBuffer,
            float[] cameraUv,
            uint outputWidth,
            uint outputHeight,
            long timestamp)
        {
            this.hardwareBuffer =
                hardwareBuffer
                ?? throw new ArgumentNullException(
                    nameof(hardwareBuffer));

            CameraUv =
                cameraUv
                ?? throw new ArgumentNullException(
                    nameof(cameraUv));

            OutputWidth =
                outputWidth;

            OutputHeight =
                outputHeight;

            Timestamp =
                timestamp;
        }

        public HardwareBuffer HardwareBuffer =>
            hardwareBuffer
            ?? throw new ObjectDisposedException(
                nameof(PendingCameraFrame));

        public float[] CameraUv { get; }

        public uint OutputWidth { get; }

        public uint OutputHeight { get; }

        public long Timestamp { get; }

        public void Dispose()
        {
            HardwareBuffer? buffer =
                Interlocked.Exchange(
                    ref hardwareBuffer,
                    null);

            if (buffer is not null)
            {
                CloseHardwareBuffer(
                    buffer);
            }
        }
    }

    /// <summary>
    /// Publishes the newest valid ARCore display-oriented camera pose and,
    /// once ARCore has a usable horizontal upward-facing plane, places the
    /// diagnostic capsule on that detected ground surface.
    /// </summary>
    private void PublishSpatialPose(
        Frame frame,
        ArCoreCamera camera,
        long timestamp,
        float zoomRatio)
    {
        string trackingState =
            camera.TrackingState.ToString();

        string trackingFailureReason =
            camera.TrackingFailureReason.ToString();

        LogTrackingTransitionIfNeeded(
            trackingState,
            trackingFailureReason,
            timestamp);

        if (!trackingState.Equals(
                "Tracking",
                StringComparison.OrdinalIgnoreCase))
        {
            /*
             * Cancel any stale-anchor release countdown as soon as camera
             * tracking itself becomes unavailable. A PAUSED camera cannot
             * provide evidence that the retained anchor is permanently stale.
             */
            InvalidatePendingRecoveryCountdown();

            ARCameraPoseBridge.PublishTrackingUnavailable(
                trackingFailureReason,
                timestamp);

            LogSpatialPoseTelemetryIfNeeded(
                trackingState,
                trackingFailureReason);

            return;
        }

        using Google.AR.Core.Pose? pose =
            camera.DisplayOrientedPose;

        if (pose is null)
        {
            Log.Warn(
                SpatialPoseTag,
                "DisplayOrientedPose returned null while ARCore reported TRACKING. " +
                "Holding the last valid Evergine camera pose.");

            InvalidatePendingRecoveryCountdown();

            ARCameraPoseBridge.PublishTrackingUnavailable(
                trackingFailureReason,
                timestamp);

            LogSpatialPoseTelemetryIfNeeded(
                trackingState,
                trackingFailureReason);

            return;
        }

        float[] translation =
            new float[3];

        float[] rotation =
            new float[4];

        pose.GetTranslation(
            translation,
            0);

        pose.GetRotationQuaternion(
            rotation,
            0);

        float[] projection =
            new float[16];

        camera.GetProjectionMatrix(
            projection,
            0,
            SpatialProjectionNearPlane,
            SpatialProjectionFarPlane);

        ApplyCameraZoomToProjection(
            projection,
            zoomRatio);

        /*
         * MOVING LOCAL AR FRAME
         * ---------------------
         * A ground anchor is a nearby spatial reference, not a permanent
         * city-scale origin. Retire it after the pedestrian has moved beyond
         * the configured local radius or its sustained height becomes
         * implausible. The existing frame loop then acquires a replacement
         * floor anchor close to the current camera.
         */
        TryRetireGroundAnchorBeyondLocalWindow(
            translation[0],
            translation[1],
            translation[2]);

        /*
         * Keep searching until a real upward-facing horizontal floor plane
         * is acquired. This also runs after a short non-tracking-anchor delay
         * so a validated replacement can be prepared before the old anchor is
         * detached. The fast path uses world-space downward rays around the
         * tracked camera; the visible-floor screen sweep remains as a fallback.
         */
        bool shouldSearchForGroundAnchor =
            spatialGroundAnchor is null ||
            ShouldSearchForProactiveGroundAnchorReplacement();

        if (shouldSearchForGroundAnchor)
        {
            TryCreateSpatialGroundAnchor(
                frame,
                translation,
                zoomRatio);
        }

        bool anchorAvailable =
            TryGetSpatialGroundAnchorPose(
                out float anchorX,
                out float anchorY,
                out float anchorZ);

        /*
         * Publish camera, projection, anchor, tracking, and timestamp as one
         * coherent snapshot.
         */
        ARCameraPoseBridge.PublishFrame(
            true,
            trackingFailureReason,
            translation[0],
            translation[1],
            translation[2],
            rotation[0],
            rotation[1],
            rotation[2],
            rotation[3],
            projection,
            SpatialProjectionNearPlane,
            SpatialProjectionFarPlane,
            anchorAvailable,
            anchorX,
            anchorY,
            anchorZ,
            timestamp);

        /*
         * Start/clear retained-anchor recovery from the ARCore frame cadence
         * rather than waiting for CameraPage's one-second diagnostics tick.
         * The method is non-blocking here; any delayed release queues on the
         * update gate asynchronously.
         */
        if (spatialGroundAnchor is not null)
        {
            TryRecoverGroundAnchorIfNeeded();
        }

        LogSpatialPoseTelemetryIfNeeded(
            trackingState,
            trackingFailureReason);
    }

    private void TryCreateSpatialGroundAnchor(
        Frame frame,
        float[] cameraTranslation,
        float zoomRatio)
    {
        long now =
            Environment.TickCount64;

        if (nextGroundPlaneSearchTimestamp !=
                long.MinValue &&
            now <
                nextGroundPlaneSearchTimestamp)
        {
            return;
        }

        nextGroundPlaneSearchTimestamp =
            now +
            GroundPlaneSearchIntervalMilliseconds;

        if (groundPlaneSearchStartedTimestamp ==
            long.MinValue)
        {
            groundPlaneSearchStartedTimestamp =
                now;
        }

        int viewportWidth;
        int viewportHeight;

        lock (displayGeometryLock)
        {
            viewportWidth =
                requestedDisplayWidth;

            viewportHeight =
                requestedDisplayHeight;
        }

        if (viewportWidth <= 0 ||
            viewportHeight <= 0 ||
            cameraTranslation is null ||
            cameraTranslation.Length < 3)
        {
            return;
        }

        groundPlaneSearchSweepCount++;

        ResetGroundPlaneSweepCandidate();

        if (!hasLoggedGroundPlaneSearch)
        {
            hasLoggedGroundPlaneSearch =
                true;

            Log.Debug(
                SpatialPoseTag,
                "Searching for ARCore ground with V4 acquisition: " +
                $"depthEnabled={depthModeEnabled}, " +
                $"{GroundPlaneWorldDownSearchPattern.Length} world-down Plane ray + " +
                $"{GroundPlaneSearchPattern.Length} lower-view screen rays, " +
                $"viewport={viewportWidth}x{viewportHeight}, " +
                $"sweepInterval={GroundPlaneSearchIntervalMilliseconds}ms. " +
                $"planeSpatialSupport={GroundPlaneRequiredSpatialSamples}, " +
                $"depthConfidence={GroundDepthValidSweepsRequired}/" +
                $"{GroundDepthConfidenceWindowSweeps}. " +
                "Preference=spatially supported upward Plane; " +
                "fallback=rolling-confidence upward DepthPoint.");
        }

        /*
         * Plane fast path. All probes run in the same ARCore frame. Two
         * separated, height-consistent hits can therefore acquire a valid
         * floor Plane in one sweep without trusting one raised surface hit.
         */
        float[] worldRayOrigin =
            new float[3];

        for (int sampleIndex = 0;
             sampleIndex <
                GroundPlaneWorldDownSearchPattern.Length;
             sampleIndex++)
        {
            (float xOffset,
             float zOffset) =
                GroundPlaneWorldDownSearchPattern[
                    sampleIndex];

            worldRayOrigin[0] =
                cameraTranslation[0] +
                xOffset;

            worldRayOrigin[1] =
                cameraTranslation[1];

            worldRayOrigin[2] =
                cameraTranslation[2] +
                zOffset;

            var hitResults =
                frame.HitTest(
                    worldRayOrigin,
                    0,
                    GroundPlaneWorldDownDirection,
                    0);

            groundPlaneSearchHitTestCount++;

            if (TryCreatePlaneGroundAnchorFromHits(
                    hitResults,
                    now,
                    cameraTranslation[1],
                    "PLANE_WORLD_DOWN",
                    sampleIndex,
                    GroundPlaneWorldDownSearchPattern.Length,
                    $"origin=({worldRayOrigin[0]:F2},{worldRayOrigin[1]:F2},{worldRayOrigin[2]:F2}), " +
                    "direction=(0.00,-1.00,0.00)"))
            {
                return;
            }
        }

        /*
         * Lower-view samples. Every sample may acquire a real Plane. Only the
         * first/central-lower sample is used for Depth confidence so successive
         * depth candidates describe approximately the same physical patch.
         */
        for (int sampleIndex = 0;
             sampleIndex <
                GroundPlaneSearchPattern.Length;
             sampleIndex++)
        {
            (float normalizedX,
             float normalizedY) =
                GroundPlaneSearchPattern[
                    sampleIndex];

            float arCoreNormalizedX =
                MapZoomedViewCoordinateToArCoreView(
                    normalizedX,
                    zoomRatio);

            float arCoreNormalizedY =
                MapZoomedViewCoordinateToArCoreView(
                    normalizedY,
                    zoomRatio);

            float hitX =
                viewportWidth *
                arCoreNormalizedX;

            float hitY =
                viewportHeight *
                arCoreNormalizedY;

            var hitResults =
                frame.HitTest(
                    hitX,
                    hitY);

            groundPlaneSearchHitTestCount++;

            string sampleDescription =
                $"displayNormalized=({normalizedX:F2},{normalizedY:F2}), " +
                $"arCoreNormalized=({arCoreNormalizedX:F2},{arCoreNormalizedY:F2}), " +
                $"zoom={zoomRatio:0.#}x, screen=({hitX:F1},{hitY:F1})";

            if (TryCreatePlaneGroundAnchorFromHits(
                    hitResults,
                    now,
                    cameraTranslation[1],
                    "PLANE_SCREEN",
                    sampleIndex,
                    GroundPlaneSearchPattern.Length,
                    sampleDescription))
            {
                return;
            }

            if (depthModeEnabled &&
                sampleIndex ==
                    GroundDepthCandidateSampleIndex &&
                TryCreateDepthGroundAnchorFromHits(
                    hitResults,
                    now,
                    cameraTranslation[1],
                    sampleIndex,
                    GroundPlaneSearchPattern.Length,
                    sampleDescription))
            {
                return;
            }
        }

        if (lastGroundPlaneSearchProgressLogTimestamp ==
                long.MinValue ||
            now -
                lastGroundPlaneSearchProgressLogTimestamp >=
                GroundPlaneSearchProgressLogIntervalMilliseconds)
        {
            lastGroundPlaneSearchProgressLogTimestamp =
                now;

            long elapsedMilliseconds =
                Math.Max(
                    0,
                    now -
                    groundPlaneSearchStartedTimestamp);

            Log.Debug(
                SpatialPoseTag,
                "Ground acquisition still active: " +
                $"depthEnabled={depthModeEnabled}, " +
                $"depthConfidence={groundDepthConfidenceValidSweepCount}/" +
                $"{GroundDepthConfidenceWindowSweeps}, " +
                $"depthSamples={groundDepthConfidenceWindowCount}, " +
                $"sweeps={groundPlaneSearchSweepCount}, " +
                $"hitTests={groundPlaneSearchHitTestCount}, " +
                $"elapsed={elapsedMilliseconds}ms. " +
                (depthModeEnabled
                    ? "Aim the lower-center camera region at the floor and move slowly."
                    : "Depth unavailable; keep textured floor visible while ARCore expands its Plane polygon."));
        }
    }

    private bool TryCreatePlaneGroundAnchorFromHits(
        IEnumerable<Google.AR.Core.HitResult> hitResults,
        long acquisitionTimestamp,
        float cameraY,
        string method,
        int sampleIndex,
        int sampleCount,
        string sampleDescription)
    {
        foreach (Google.AR.Core.HitResult hit in hitResults)
        {
            if (hit.Trackable is not ArCorePlane plane)
            {
                continue;
            }

            using Google.AR.Core.Pose? hitPose =
                hit.HitPose;

            if (hitPose is null ||
                !plane.IsPoseInPolygon(
                    hitPose))
            {
                continue;
            }

            using Google.AR.Core.Pose? planeCenterPose =
                plane.CenterPose;

            if (planeCenterPose is null)
            {
                continue;
            }

            float[]? planeNormal =
                planeCenterPose.GetTransformedAxis(
                    1,
                    1.0f);

            if (planeNormal is null ||
                planeNormal.Length < 3 ||
                planeNormal[1] < 0.75f)
            {
                continue;
            }

            float[] hitTranslation =
                new float[3];

            hitPose.GetTranslation(
                hitTranslation,
                0);

            if (!LocalArNavigationPolicy
                    .IsPreferredGroundCandidateHeight(
                        cameraY,
                        hitTranslation[1],
                        out float cameraHeightAboveGroundMeters))
            {
                continue;
            }

            if (!TryAccumulateGroundPlaneSweepCandidate(
                    hitTranslation))
            {
                continue;
            }

            if (!TryAssignGroundAnchorFromHit(
                    hit,
                    "Plane"))
            {
                continue;
            }

            long elapsedMilliseconds =
                Math.Max(
                    0,
                    acquisitionTimestamp -
                    groundPlaneSearchStartedTimestamp);

            Log.Debug(
                SpatialPoseTag,
                "ARCore GROUND anchor created from detected floor Plane.");

            Log.Debug(
                SpatialPoseTag,
                "Ground acquisition = " +
                $"method={method}, " +
                $"depthEnabled={depthModeEnabled}, " +
                $"sample={sampleIndex + 1}/{sampleCount}, " +
                $"{sampleDescription}, " +
                $"sweeps={groundPlaneSearchSweepCount}, " +
                $"hitTests={groundPlaneSearchHitTestCount}, " +
                $"elapsed={elapsedMilliseconds}ms, " +
                $"spatialSupport={groundPlaneSweepCandidateSupportCount}/" +
                $"{GroundPlaneRequiredSpatialSamples}, " +
                $"hit=({hitTranslation[0]:F2},{hitTranslation[1]:F2},{hitTranslation[2]:F2}), " +
                $"cameraHeight={cameraHeightAboveGroundMeters:F2}m, " +
                $"normalY={planeNormal[1]:F2}.");

            ResetGroundPlaneSearchState();

            return true;
        }

        return false;
    }

    private bool TryAccumulateGroundPlaneSweepCandidate(
        float[] hitTranslation)
    {
        if (!hasGroundPlaneSweepCandidate)
        {
            hasGroundPlaneSweepCandidate =
                true;

            groundPlaneSweepCandidateX =
                hitTranslation[0];

            groundPlaneSweepCandidateY =
                hitTranslation[1];

            groundPlaneSweepCandidateZ =
                hitTranslation[2];

            groundPlaneSweepCandidateSupportCount =
                1;

            return false;
        }

        float yDelta =
            MathF.Abs(
                hitTranslation[1] -
                groundPlaneSweepCandidateY);

        if (yDelta >
            GroundPlaneMaximumYDeltaMeters)
        {
            /*
             * Prefer the lower plausible surface. If the center ray lands on
             * a bench and a nearby ray reaches the floor, later floor samples
             * can still form the required spatial agreement in this sweep.
             */
            if (hitTranslation[1] <
                groundPlaneSweepCandidateY)
            {
                groundPlaneSweepCandidateX =
                    hitTranslation[0];

                groundPlaneSweepCandidateY =
                    hitTranslation[1];

                groundPlaneSweepCandidateZ =
                    hitTranslation[2];

                groundPlaneSweepCandidateSupportCount =
                    1;
            }

            return false;
        }

        float horizontalSeparation =
            LocalArNavigationPolicy.GetHorizontalDistanceMeters(
                hitTranslation[0] -
                    groundPlaneSweepCandidateX,
                hitTranslation[2] -
                    groundPlaneSweepCandidateZ);

        if (!float.IsFinite(
                horizontalSeparation) ||
            horizontalSeparation <
                GroundPlaneMinimumSampleSeparationMeters)
        {
            return false;
        }

        groundPlaneSweepCandidateSupportCount++;

        return groundPlaneSweepCandidateSupportCount >=
            GroundPlaneRequiredSpatialSamples;
    }

    private bool TryCreateDepthGroundAnchorFromHits(
        IEnumerable<Google.AR.Core.HitResult> hitResults,
        long acquisitionTimestamp,
        float cameraY,
        int sampleIndex,
        int sampleCount,
        string sampleDescription)
    {
        foreach (Google.AR.Core.HitResult hit in hitResults)
        {
            if (hit.Trackable is not Google.AR.Core.DepthPoint)
            {
                continue;
            }

            using Google.AR.Core.Pose? hitPose =
                hit.HitPose;

            if (hitPose is null)
            {
                continue;
            }

            float[]? surfaceNormal =
                hitPose.GetTransformedAxis(
                    1,
                    1.0f);

            if (surfaceNormal is null ||
                surfaceNormal.Length < 3 ||
                surfaceNormal[1] <
                    GroundDepthMinimumNormalY)
            {
                continue;
            }

            float[] hitTranslation =
                new float[3];

            hitPose.GetTranslation(
                hitTranslation,
                0);

            if (!LocalArNavigationPolicy
                    .IsPreferredGroundCandidateHeight(
                        cameraY,
                        hitTranslation[1],
                        out float cameraHeight))
            {
                continue;
            }

            bool stableWithPrevious =
                false;

            if (hasGroundDepthCandidate)
            {
                float deltaY =
                    MathF.Abs(
                        hitTranslation[1] -
                        groundDepthCandidateY);

                float deltaX =
                    hitTranslation[0] -
                    groundDepthCandidateX;

                float deltaZ =
                    hitTranslation[2] -
                    groundDepthCandidateZ;

                float horizontalDelta =
                    MathF.Sqrt(
                        deltaX * deltaX +
                        deltaZ * deltaZ);

                stableWithPrevious =
                    deltaY <=
                        GroundDepthMaximumYDeltaMeters &&
                    horizontalDelta <=
                        GroundDepthMaximumHorizontalDeltaMeters;
            }

            if (hasGroundDepthCandidate &&
                !stableWithPrevious)
            {
                continue;
            }

            if (!hasGroundDepthCandidate)
            {
                hasGroundDepthCandidate =
                    true;
            }

            groundDepthCandidateX =
                hitTranslation[0];

            groundDepthCandidateY =
                hitTranslation[1];

            groundDepthCandidateZ =
                hitTranslation[2];

            RecordGroundDepthConfidenceSample(
                valid: true);

            if (groundDepthConfidenceValidSweepCount <
                GroundDepthValidSweepsRequired)
            {
                return false;
            }

            if (!TryAssignGroundAnchorFromHit(
                    hit,
                    "DepthPoint"))
            {
                ResetGroundDepthCandidate();
                return false;
            }

            long elapsedMilliseconds =
                Math.Max(
                    0,
                    acquisitionTimestamp -
                    groundPlaneSearchStartedTimestamp);

            Log.Debug(
                SpatialPoseTag,
                "ARCore GROUND anchor created from rolling-confidence DepthPoint fallback.");

            Log.Debug(
                SpatialPoseTag,
                "Ground acquisition = " +
                "method=DEPTH_ROLLING, " +
                $"depthEnabled={depthModeEnabled}, " +
                $"sample={sampleIndex + 1}/{sampleCount}, " +
                $"{sampleDescription}, " +
                $"depthConfidence={groundDepthConfidenceValidSweepCount}/" +
                $"{GroundDepthConfidenceWindowSweeps}, " +
                $"depthSamples={groundDepthConfidenceWindowCount}, " +
                $"sweeps={groundPlaneSearchSweepCount}, " +
                $"hitTests={groundPlaneSearchHitTestCount}, " +
                $"elapsed={elapsedMilliseconds}ms, " +
                $"hit=({hitTranslation[0]:F2},{hitTranslation[1]:F2},{hitTranslation[2]:F2}), " +
                $"normalY={surfaceNormal[1]:F2}, " +
                $"cameraToGroundVertical={cameraHeight:F2}m.");

            ResetGroundPlaneSearchState();

            return true;
        }

        /*
         * The designated lower-center sample yielded no acceptable DepthPoint
         * this sweep. Record one miss without discarding earlier consistent
         * evidence; old evidence naturally expires from the rolling window.
         */
        RecordGroundDepthConfidenceSample(
            valid: false);

        return false;
    }

    private void RecordGroundDepthConfidenceSample(
        bool valid)
    {
        if (groundDepthConfidenceWindowCount ==
            GroundDepthConfidenceWindowSweeps)
        {
            if (groundDepthConfidenceWindow[
                    groundDepthConfidenceWindowIndex])
            {
                groundDepthConfidenceValidSweepCount--;
            }
        }
        else
        {
            groundDepthConfidenceWindowCount++;
        }

        groundDepthConfidenceWindow[
            groundDepthConfidenceWindowIndex] =
                valid;

        if (valid)
        {
            groundDepthConfidenceValidSweepCount++;
        }

        groundDepthConfidenceWindowIndex =
            (groundDepthConfidenceWindowIndex + 1) %
            GroundDepthConfidenceWindowSweeps;

        if (groundDepthConfidenceValidSweepCount ==
                0 &&
            !valid)
        {
            hasGroundDepthCandidate =
                false;

            groundDepthCandidateX =
                0.0f;

            groundDepthCandidateY =
                0.0f;

            groundDepthCandidateZ =
                0.0f;
        }
    }

    private bool TryAssignGroundAnchorFromHit(
        Google.AR.Core.HitResult hit,
        string source)
    {
        Google.AR.Core.Anchor? newAnchor;

        try
        {
            newAnchor =
                hit.CreateAnchor();
        }
        catch (Exception exception)
        {
            Log.Debug(
                SpatialPoseTag,
                $"Ground {source} hit could not create an anchor: " +
                $"{exception.GetType().Name}: {exception.Message}");

            return false;
        }

        if (newAnchor is null)
        {
            return false;
        }

        Google.AR.Core.Anchor? previousAnchor =
            Interlocked.Exchange(
                ref spatialGroundAnchor,
                newAnchor);

        if (previousAnchor is not null &&
            !ReferenceEquals(
                previousAnchor,
                newAnchor))
        {
            RegisterProactiveGroundAnchorHandoff(
                previousAnchor,
                source);

            DisposeGroundAnchor(
                previousAnchor);
        }

        return true;
    }

    private void ResetGroundDepthCandidate()
    {
        hasGroundDepthCandidate =
            false;

        groundDepthCandidateX =
            0.0f;

        groundDepthCandidateY =
            0.0f;

        groundDepthCandidateZ =
            0.0f;

        Array.Clear(
            groundDepthConfidenceWindow,
            0,
            groundDepthConfidenceWindow.Length);

        groundDepthConfidenceWindowCount =
            0;

        groundDepthConfidenceWindowIndex =
            0;

        groundDepthConfidenceValidSweepCount =
            0;
    }

    private void ResetGroundPlaneSweepCandidate()
    {
        hasGroundPlaneSweepCandidate =
            false;

        groundPlaneSweepCandidateX =
            0.0f;

        groundPlaneSweepCandidateY =
            0.0f;

        groundPlaneSweepCandidateZ =
            0.0f;

        groundPlaneSweepCandidateSupportCount =
            0;
    }

    private void ResetGroundPlaneSearchState()
    {
        nextGroundPlaneSearchTimestamp =
            long.MinValue;

        groundPlaneSearchStartedTimestamp =
            long.MinValue;

        lastGroundPlaneSearchProgressLogTimestamp =
            long.MinValue;

        groundPlaneSearchSweepCount =
            0;

        groundPlaneSearchHitTestCount =
            0;

        ResetGroundPlaneSweepCandidate();

        ResetGroundDepthCandidate();
    }

    private bool TryGetSpatialGroundAnchorPose(
        out float anchorX,
        out float anchorY,
        out float anchorZ)
    {
        anchorX = 0;
        anchorY = 0;
        anchorZ = 0;

        Google.AR.Core.Anchor? anchor =
            spatialGroundAnchor;

        if (anchor is null)
        {
            return false;
        }

        if (!anchor.TrackingState
                .ToString()
                .Equals(
                    "Tracking",
                    StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        using Google.AR.Core.Pose? anchorPose =
            anchor.Pose;

        if (anchorPose is null)
        {
            return false;
        }

        float[] anchorTranslation =
            new float[3];

        anchorPose.GetTranslation(
            anchorTranslation,
            0);

        anchorX =
            anchorTranslation[0];

        // Publish the actual ARCore ground-anchor Y. Visual/debug offsets
        // belong to the Evergine renderer, not to spatial truth.
        anchorY =
            anchorTranslation[1];

        anchorZ =
            anchorTranslation[2];

        return true;
    }

    private void ReleaseSpatialGroundAnchor()
    {
        Google.AR.Core.Anchor? anchor =
            Interlocked.Exchange(
                ref spatialGroundAnchor,
                null);

        ResetGroundPlaneSearchState();

        if (anchor is null)
        {
            return;
        }

        DisposeGroundAnchor(
            anchor);
    }

    private static void DisposeGroundAnchor(
        Google.AR.Core.Anchor anchor)
    {
        try
        {
            anchor.Detach();
        }
        catch
        {
            // Best-effort diagnostic anchor cleanup.
        }

        anchor.Dispose();
    }

    private void LogTrackingTransitionIfNeeded(
        string trackingState,
        string trackingFailureReason,
        long timestamp)
    {
        bool stateChanged =
            !string.Equals(
                lastLoggedTrackingState,
                trackingState,
                StringComparison.Ordinal);

        bool failureReasonChanged =
            !string.Equals(
                lastLoggedTrackingFailureReason,
                trackingFailureReason,
                StringComparison.Ordinal);

        if (!stateChanged &&
            !failureReasonChanged)
        {
            return;
        }

        string previousState =
            lastLoggedTrackingState ??
            "<none>";

        string previousFailureReason =
            lastLoggedTrackingFailureReason ??
            "<none>";

        Log.Debug(
            SpatialPoseTag,
            "ARCore tracking transition: " +
            $"{previousState} -> {trackingState}; " +
            $"failure {previousFailureReason} -> {trackingFailureReason}; " +
            $"frameTimestamp={timestamp}");

        if (trackingState.Equals(
                "Tracking",
                StringComparison.OrdinalIgnoreCase))
        {
            Log.Debug(
                SpatialPoseTag,
                "ARCore tracking acquired/reacquired. " +
                "Evergine camera pose updates will resume.");
        }
        else
        {
            Log.Warn(
                SpatialPoseTag,
                "ARCore tracking unavailable. " +
                "Evergine will hold the last valid camera pose until tracking returns.");
        }

        lastLoggedTrackingState =
            trackingState;

        lastLoggedTrackingFailureReason =
            trackingFailureReason;
    }

    private void LogSpatialPoseTelemetryIfNeeded(
        string trackingState,
        string trackingFailureReason)
    {
        long now =
            Environment.TickCount64;

        if (lastSpatialPoseTelemetryLogTimestamp !=
                long.MinValue &&
            now - lastSpatialPoseTelemetryLogTimestamp <
                SpatialPoseTelemetryLogIntervalMilliseconds)
        {
            return;
        }

        lastSpatialPoseTelemetryLogTimestamp =
            now;

        ARCameraPoseBridge.PoseSnapshot pose =
            ARCameraPoseBridge.Current;

        ARCameraPoseBridge.EngineTelemetry telemetry =
            ARCameraPoseBridge.CurrentEngineTelemetry;

        Log.Debug(
            SpatialPoseTag,
            "========== AR Pose / Evergine Diagnostic ==========");

        ARCameraPoseBridge.SpatialSnapshot spatialFrame =
            ARCameraPoseBridge.CurrentFrame;

        Log.Debug(
            SpatialPoseTag,
            $"Spatial Snapshot Version = {spatialFrame.Version}, " +
            $"FrameTimestamp={spatialFrame.FrameTimestamp}");

        Log.Debug(
            SpatialPoseTag,
            $"ARCore Tracking = {pose.IsTracking}");

        Log.Debug(
            SpatialPoseTag,
            $"ARCore Tracking State = {trackingState}");

        Log.Debug(
            SpatialPoseTag,
            $"ARCore Tracking Failure Reason = {trackingFailureReason}");

        if (pose.IsTracking)
        {
            Log.Debug(
                SpatialPoseTag,
                "ARCore Camera Position (m) = " +
                $"X={pose.PositionX:F4}, " +
                $"Y={pose.PositionY:F4}, " +
                $"Z={pose.PositionZ:F4}");

            Log.Debug(
                SpatialPoseTag,
                "ARCore Quaternion = " +
                $"X={pose.RotationX:F5}, " +
                $"Y={pose.RotationY:F5}, " +
                $"Z={pose.RotationZ:F5}, " +
                $"W={pose.RotationW:F5}");
        }

        ARCameraPoseBridge.ProjectionSnapshot projection =
            ARCameraPoseBridge.CurrentProjection;

        if (projection.IsAvailable)
        {
            Log.Debug(
                SpatialPoseTag,
                "ARCore Projection = " +
                $"M11={projection.M11:F5}, " +
                $"M22={projection.M22:F5}, " +
                $"M13={projection.M13:F5}, " +
                $"M23={projection.M23:F5}, " +
                $"M33={projection.M33:F5}, " +
                $"M34={projection.M34:F5}, " +
                $"M43={projection.M43:F5}");

            ARCameraPoseBridge.ProjectionTelemetry projectionTelemetry =
                ARCameraPoseBridge.CurrentProjectionTelemetry;

            if (projectionTelemetry.IsAvailable)
            {
                Log.Debug(
                    SpatialPoseTag,
                    "Evergine Projection Applied = " +
                    $"True, ClipDepthZeroToOne=" +
                    $"{projectionTelemetry.IsClipDepthZeroToOne}, " +
                    $"FlipYProjection=" +
                    $"{projectionTelemetry.FlipYProjection}");
            }
        }

        ARCameraPoseBridge.AnchorSnapshot anchor =
            ARCameraPoseBridge.CurrentAnchor;

        if (anchor.IsAvailable)
        {
            Log.Debug(
                SpatialPoseTag,
                "Current ARCore Ground Anchor Pose (m) = " +
                $"X={anchor.PositionX:F4}, " +
                $"Y={anchor.PositionY:F4}, " +
                $"Z={anchor.PositionZ:F4}");
        }

        if (telemetry.IsAvailable)
        {
            Log.Debug(
                SpatialPoseTag,
                $"Evergine Applied Snapshot Version = " +
                $"{telemetry.AppliedSpatialVersion}, " +
                $"FrameTimestamp={telemetry.AppliedFrameTimestamp}");

            Log.Debug(
                SpatialPoseTag,
                "Evergine Camera Position (m) = " +
                $"X={telemetry.CameraX:F4}, " +
                $"Y={telemetry.CameraY:F4}, " +
                $"Z={telemetry.CameraZ:F4}");

            Log.Debug(
                SpatialPoseTag,
                "Evergine Camera Quaternion = " +
                $"X={telemetry.RotationX:F5}, " +
                $"Y={telemetry.RotationY:F5}, " +
                $"Z={telemetry.RotationZ:F5}, " +
                $"W={telemetry.RotationW:F5}");

            Log.Debug(
                SpatialPoseTag,
                "Evergine Capsule Position (m) = " +
                $"X={telemetry.TargetX:F4}, " +
                $"Y={telemetry.TargetY:F4}, " +
                $"Z={telemetry.TargetZ:F4}");

            Log.Debug(
                SpatialPoseTag,
                $"Camera-to-Capsule Distance = " +
                $"{telemetry.CameraToTargetDistance:F4} m");
        }
        else
        {
            Log.Debug(
                SpatialPoseTag,
                "Evergine telemetry not available yet.");
        }

        Log.Debug(
            SpatialPoseTag,
            "====================================================");
    }

    /// <summary>
    /// Copies ARCore's smoothed 16-bit depth map together with every value
    /// needed to reconstruct depth pixels in the ARCore world frame.
    ///
    /// The depth map is intentionally acquired from the CURRENT Frame and is
    /// immediately closed after copying. Google documents the image as one
    /// little-endian 16-bit plane whose values are millimeters along the
    /// camera principal axis.
    /// </summary>
    private void TryPublishDepthOcclusionFrame(
        Frame frame,
        ArCoreCamera camera,
        long timestamp,
        float zoomRatio)
    {
        ARFloodDepthBridge.FloodDepthSnapshot flood =
            ARFloodDepthBridge.Current;

        ARRouteBridge.RouteSnapshot route =
            ARRouteBridge.Current;

        bool nearbyRouteAvailable =
            ARRouteRenderer.DepthOcclusionRequested &&
            powerThermalDecision.RouteDepthAllowed &&
            route.IsAvailable &&
            ARRouteVisualPolicy.HasNearbyRoute(
                route.Points);

        if (!flood.IsAvailable &&
            !nearbyRouteAvailable)
        {
            if (ARDepthOcclusionBridge.Current.IsAvailable)
            {
                ARDepthOcclusionBridge.Clear();
            }

            lastDepthOcclusionPublishTimestamp =
                long.MinValue;

            return;
        }

        if (!camera.TrackingState.ToString().Equals(
                "Tracking",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        long publishIntervalNanoseconds =
            flood.IsAvailable
                ? DepthOcclusionPublishIntervalNanoseconds
                : RouteDepthOcclusionPublishIntervalNanoseconds;

        publishIntervalNanoseconds =
            ARPowerThermalPolicy.AdjustDepthIntervalNanoseconds(
                publishIntervalNanoseconds,
                powerThermalDecision);

        if (lastDepthOcclusionPublishTimestamp != long.MinValue &&
            timestamp > lastDepthOcclusionPublishTimestamp &&
            timestamp - lastDepthOcclusionPublishTimestamp <
                publishIntervalNanoseconds)
        {
            return;
        }

        try
        {
            using global::Android.Media.Image depthImage =
                frame.AcquireDepthImage16Bits();

            int width =
                depthImage.Width;

            int height =
                depthImage.Height;

            if (width <= 0 ||
                height <= 0)
            {
                return;
            }

            global::Android.Media.Image.Plane[]? planes =
                depthImage.GetPlanes();

            if (planes is null ||
                planes.Length < 1)
            {
                return;
            }

            global::Android.Media.Image.Plane plane =
                planes[0];

            Java.Nio.ByteBuffer? buffer =
                plane.Buffer;

            if (buffer is null)
            {
                return;
            }

            int rowStride =
                plane.RowStride;

            int pixelStride =
                plane.PixelStride;

            if (rowStride <= 0 ||
                pixelStride <= 0)
            {
                return;
            }

            ushort[] depthMillimeters =
                new ushort[width * height];

            for (int y = 0;
                 y < height;
                 y++)
            {
                int rowOffset =
                    y * rowStride;

                int destinationRow =
                    y * width;

                for (int x = 0;
                     x < width;
                     x++)
                {
                    int byteIndex =
                        rowOffset +
                        x * pixelStride;

                    // D_16 is explicitly little-endian. Read the bytes
                    // directly so this does not depend on ByteBuffer order.
                    byte low =
                        unchecked((byte)buffer.Get(byteIndex));

                    byte high =
                        unchecked((byte)buffer.Get(byteIndex + 1));

                    depthMillimeters[
                        destinationRow + x] =
                        (ushort)(low | (high << 8));
                }
            }

            using CameraIntrinsics? intrinsics =
                camera.TextureIntrinsics;

            if (intrinsics is null)
            {
                return;
            }

            float[]? focalLength =
                intrinsics.GetFocalLength();

            float[]? principalPoint =
                intrinsics.GetPrincipalPoint();

            int[]? dimensions =
                intrinsics.GetImageDimensions();

            if (focalLength is null ||
                focalLength.Length < 2 ||
                principalPoint is null ||
                principalPoint.Length < 2 ||
                dimensions is null ||
                dimensions.Length < 2)
            {
                return;
            }

            using Google.AR.Core.Pose? physicalPose =
                camera.Pose;

            if (physicalPose is null)
            {
                return;
            }

            float[] translation =
                new float[3];

            float[] rotation =
                new float[4];

            physicalPose.GetTranslation(
                translation,
                0);

            physicalPose.GetRotationQuaternion(
                rotation,
                0);

            float[] viewToTextureUv =
                TransformCameraUv(
                    frame,
                    zoomRatio);

            ARCameraPoseBridge.SpatialSnapshot spatial =
                ARCameraPoseBridge.CurrentFrame;

            bool groundAvailable =
                spatial.FrameTimestamp == timestamp &&
                spatial.Anchor.IsAvailable;

            float groundWorldY =
                groundAvailable
                    ? spatial.Anchor.PositionY
                    : 0.0f;

            ARDepthOcclusionBridge.Publish(
                timestamp,
                width,
                height,
                depthMillimeters,
                viewToTextureUv,
                translation[0],
                translation[1],
                translation[2],
                rotation[0],
                rotation[1],
                rotation[2],
                rotation[3],
                focalLength[0],
                focalLength[1],
                principalPoint[0],
                principalPoint[1],
                dimensions[0],
                dimensions[1],
                groundAvailable,
                groundWorldY);

            lastDepthOcclusionPublishTimestamp =
                timestamp;

            if (!depthOcclusionAvailabilityLogged)
            {
                depthOcclusionAvailabilityLogged =
                    true;

                double refreshCapHz =
                    1_000_000_000.0 /
                    publishIntervalNanoseconds;

                Log.Info(
                    "RescuAR-FloodDepth",
                    "ARCore depth occlusion ACTIVE: " +
                    $"consumer={(flood.IsAvailable ? "flood" : "route")}, " +
                    $"refreshCap={refreshCapHz:0.#}Hz, " +
                    $"powerMode={powerThermalDecision.Mode}, " +
                    $"depthImage={width}x{height}, " +
                    $"textureIntrinsics={dimensions[0]}x{dimensions[1]}, " +
                    $"pixelStride={pixelStride}, rowStride={rowStride}.");
            }
        }
        catch (Exception exception)
        {
            /*
             * NotYetAvailableException is expected while depth is warming up,
             * and other ARCore depth exceptions can occur transiently during
             * tracking loss. Keep the last good depth frame instead of
             * tearing down active flood or route occlusion.
             */
            if (!depthOcclusionAvailabilityLogged)
            {
                Log.Debug(
                    "RescuAR-FloodDepth",
                    "ARCore depth occlusion waiting for a usable depth frame: " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// Uses ARCore to transform viewport corners from normalized view
    /// coordinates into normalized camera-texture coordinates.
    /// </summary>
    private float[] TransformCameraUv(
        Frame frame,
        float zoomRatio)
    {
        float safeZoomRatio =
            Math.Clamp(
                zoomRatio,
                MinimumCameraZoomRatio,
                MaximumCameraZoomRatio);

        float[] inputViewUv;

        if (Math.Abs(
                safeZoomRatio - 1.0f) < 0.001f)
        {
            inputViewUv =
                ViewNormalizedCameraUv;
        }
        else
        {
            float halfSpan =
                0.5f /
                safeZoomRatio;

            float minimum =
                0.5f -
                halfSpan;

            float maximum =
                0.5f +
                halfSpan;

            inputViewUv =
            [
                // Top-left
                minimum, minimum,

                // Top-right
                maximum, minimum,

                // Bottom-left
                minimum, maximum,

                // Bottom-right
                maximum, maximum
            ];
        }

        float[] transformedUv =
            new float[8];

        frame.TransformCoordinates2d(
            Coordinates2d.ViewNormalized,
            inputViewUv,
            Coordinates2d.TextureNormalized,
            transformedUv);

        if (!hasLoggedTransformedUv)
        {
            hasLoggedTransformedUv =
                true;

            Log.Debug(
                Tag,
                $"========== ARCore Camera UV ({safeZoomRatio:0.#}x) ==========");

            Log.Debug(
                Tag,
                $"TL = ({transformedUv[0]:F6}, {transformedUv[1]:F6})");

            Log.Debug(
                Tag,
                $"TR = ({transformedUv[2]:F6}, {transformedUv[3]:F6})");

            Log.Debug(
                Tag,
                $"BL = ({transformedUv[4]:F6}, {transformedUv[5]:F6})");

            Log.Debug(
                Tag,
                $"BR = ({transformedUv[6]:F6}, {transformedUv[7]:F6})");

            Log.Debug(
                Tag,
                "======================================");
        }

        return transformedUv;
    }

    private void GetAppliedDisplaySize(
        out uint width,
        out uint height)
    {
        int currentWidth;
        int currentHeight;

        lock (displayGeometryLock)
        {
            currentWidth =
                appliedDisplayWidth;

            currentHeight =
                appliedDisplayHeight;
        }

        if (currentWidth <= 0 ||
            currentHeight <= 0)
        {
            throw new InvalidOperationException(
                "ARCore display geometry has not been applied yet. " +
                "A valid Evergine viewport size is required before " +
                "converting the camera frame.");
        }

        width =
            checked((uint)currentWidth);

        height =
            checked((uint)currentHeight);
    }

    private void ApplyDisplayGeometryIfNeeded(
        Session currentSession)
    {
        int rotation;
        int width;
        int height;
        bool shouldApply;

        lock (displayGeometryLock)
        {
            shouldApply =
                displayGeometryAvailable &&
                (displayGeometryDirty ||
                 appliedDisplayRotation != requestedDisplayRotation ||
                 appliedDisplayWidth != requestedDisplayWidth ||
                 appliedDisplayHeight != requestedDisplayHeight);

            if (!shouldApply)
            {
                return;
            }

            rotation =
                requestedDisplayRotation;

            width =
                requestedDisplayWidth;

            height =
                requestedDisplayHeight;
        }

        currentSession.SetDisplayGeometry(
            rotation,
            width,
            height);

        lock (displayGeometryLock)
        {
            appliedDisplayRotation =
                rotation;

            appliedDisplayWidth =
                width;

            appliedDisplayHeight =
                height;

            displayGeometryDirty =
                requestedDisplayRotation != rotation ||
                requestedDisplayWidth != width ||
                requestedDisplayHeight != height;
        }

        hasLoggedTransformedUv =
            false;

        Log.Debug(
            Tag,
            "ARCore display geometry applied: " +
            $"rotation={rotation}, " +
            $"width={width}, " +
            $"height={height}");
    }

    private void TryCaptureInitialDisplayGeometry()
    {
        lock (displayGeometryLock)
        {
            if (displayGeometryAvailable)
            {
                Log.Debug(
                    Tag,
                    "Skipping DecorView display geometry fallback because " +
                    "Evergine viewport geometry is already available.");

                return;
            }
        }

        Activity? currentActivity =
            Platform.CurrentActivity;

        if (currentActivity is null)
        {
            Log.Warn(
                Tag,
                "Unable to capture initial display geometry because " +
                "Platform.CurrentActivity is null.");

            return;
        }

        var display =
            currentActivity.WindowManager?.DefaultDisplay;

        var decorView =
            currentActivity.Window?.DecorView;

        if (display is null ||
            decorView is null)
        {
            Log.Warn(
                Tag,
                "Unable to capture initial ARCore display geometry.");

            return;
        }

        int width =
            decorView.Width;

        int height =
            decorView.Height;

        if (width <= 0 ||
            height <= 0)
        {
            Log.Warn(
                Tag,
                $"Invalid DecorView size: {width}x{height}");

            return;
        }

        SetDisplayGeometry(
            (int)display.Rotation,
            width,
            height);

        Log.Debug(
            Tag,
            "Initial ARCore display geometry captured from Android DecorView.");
    }

    /*
     * Full shutdown helper retained for initialization failure/application
     * destruction paths. Do not use this method for normal Camera-tab exit,
     * because it intentionally releases the spatial anchor.
     */
    private void StopFrameLoop()
    {
        CancellationTokenSource? cancellation =
            frameLoopCancellation;

        frameLoopCancellation =
            null;

        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch
        {
            // Best-effort shutdown.
        }

        cancellation.Dispose();

        ReleasePendingCameraFrame();

        ReleaseSpatialGroundAnchor();

        frameLoopTask =
            null;

        Log.Debug(
            Tag,
            "Automatic ARCore frame loop stop requested.");
    }

    private static ARCoreVulkanImporter CreateImporter(
        VKGraphicsContext graphicsContext)
    {
        Log.Debug(
            Tag,
            "Creating ARCore Vulkan importer...");

        return new ARCoreVulkanImporter(
            graphicsContext);
    }

    private static void InspectHardwareBuffer(
        HardwareBuffer hardwareBuffer)
    {
        Log.Debug(
            Tag,
            "========== HardwareBuffer Inspection ==========");

        Log.Debug(
            Tag,
            $"HardwareBuffer: {hardwareBuffer}");

        Log.Debug(
            Tag,
            $"Width: {hardwareBuffer.Width}");

        Log.Debug(
            Tag,
            $"Height: {hardwareBuffer.Height}");

        Log.Debug(
            Tag,
            $"Format: {hardwareBuffer.Format}");

        Log.Debug(
            Tag,
            $"Usage: {hardwareBuffer.Usage}");

        Log.Debug(
            Tag,
            $"IsClosed: {hardwareBuffer.IsClosed}");

        Log.Debug(
            Tag,
            "===============================================");
    }

    private void CloseSessionAfterFailure()
    {
        ReleaseSpatialGroundAnchor();

        try
        {
            session?.Close();
        }
        catch (Exception exception)
        {
            Log.Error(
                Tag,
                $"Error closing ARCore Session: {exception}");
        }

        session =
            null;

        sessionPaused =
            false;

        lastProcessedTimestamp =
            long.MinValue;
    }

    private void RecordProcessedFrame()
    {
        processedFrameCount++;

        long now =
            Environment.TickCount64;

        long elapsedMilliseconds =
            now - fpsWindowStartTimestamp;

        if (elapsedMilliseconds <
            powerThermalDecision.DiagnosticLogIntervalMilliseconds)
        {
            return;
        }

        double fps =
            processedFrameCount *
            1000.0 /
            elapsedMilliseconds;

        string temperatureText =
            float.IsFinite(
                powerThermalDecision.BatteryTemperatureCelsius)
                ? $"{powerThermalDecision.BatteryTemperatureCelsius:F1}C"
                : "unavailable";

        Log.Debug(
            Tag,
            "Camera pipeline telemetry: " +
            $"fps={fps:F1}, " +
            $"targetMaxFps={powerThermalDecision.TargetMaximumFramesPerSecond}, " +
            $"powerMode={powerThermalDecision.Mode}, " +
            $"batteryTemperature={temperatureText}, " +
            $"powerSaver={powerThermalDecision.PowerSaveMode}.");

        processedFrameCount =
            0;

        fpsWindowStartTimestamp =
            now;
    }

    private bool TryConfigureAutomaticDepth(
        Session currentSession,
        Google.AR.Core.Config config)
    {
        try
        {
            Google.AR.Core.Config.DepthMode automaticDepthMode =
                Google.AR.Core.Config.DepthMode.Automatic!;

            bool isSupported =
                currentSession.IsDepthModeSupported(
                    automaticDepthMode);

            config.SetDepthMode(
                isSupported
                    ? automaticDepthMode
                    : Google.AR.Core.Config.DepthMode.Disabled);

            Log.Debug(
                Tag,
                "ARCore Depth API configuration: " +
                $"supported={isSupported}, " +
                $"requested={(isSupported ? "AUTOMATIC" : "DISABLED")}. " +
                "Ground acquisition uses spatially supported Plane hits first and rolling-confidence DepthPoint fallback when enabled.");

            return isSupported;
        }
        catch (Exception exception)
        {
            /*
             * Depth is an optimization, not a hard dependency. Keep the
             * validated Plane path available on devices/configurations where
             * ARCore refuses depth or a vendor implementation behaves
             * unexpectedly.
             */
            try
            {
                config.SetDepthMode(
                    Google.AR.Core.Config.DepthMode.Disabled);
            }
            catch
            {
                // Best effort: the default ARCore depth mode is disabled.
            }

            Log.Warn(
                Tag,
                "ARCore Depth API could not be enabled; continuing with " +
                "Plane-only ground acquisition. " +
                $"{exception.GetType().Name}: {exception.Message}");

            return false;
        }
    }

    private static void InspectSupportedCameraConfigurations(
        Session currentSession)
    {
        Log.Debug(
            Tag,
            "========================================");

        Log.Debug(
            Tag,
            "ARCore supported camera configurations");

        Log.Debug(
            Tag,
            "========================================");

        try
        {
            using CameraConfigFilter cameraConfigFilter =
                new(
                    currentSession);

            var cameraConfigs =
                currentSession.GetSupportedCameraConfigs(
                    cameraConfigFilter);

            if (cameraConfigs is null ||
                cameraConfigs.Count == 0)
            {
                Log.Warn(
                    Tag,
                    "ARCore returned no camera configurations.");

                Log.Debug(
                    Tag,
                    "The default ARCore camera configuration will be used.");

                return;
            }

            Log.Debug(
                Tag,
                $"Supported camera configuration count = {cameraConfigs.Count}");

            for (int index = 0;
                 index < cameraConfigs.Count;
                 index++)
            {
                CameraConfig? cameraConfig =
                    cameraConfigs[index];

                if (cameraConfig is null)
                {
                    Log.Warn(
                        Tag,
                        $"CameraConfig[{index}] is null.");

                    continue;
                }

                Log.Debug(
                    Tag,
                    $"----- CameraConfig[{index}] -----");

                try
                {
                    Log.Debug(
                        Tag,
                        $"CameraId = {cameraConfig.CameraId ?? "<null>"}");
                }
                catch (Exception exception)
                {
                    Log.Debug(
                        Tag,
                        $"CameraId = <unavailable: {exception.GetType().Name}>");
                }

                try
                {
                    CameraConfig.FacingDirection facingDirection =
                        cameraConfig.GetFacingDirection();

                    Log.Debug(
                        Tag,
                        $"FacingDirection = {facingDirection}");
                }
                catch (Exception exception)
                {
                    Log.Debug(
                        Tag,
                        $"FacingDirection = <unavailable: {exception.GetType().Name}>");
                }

                try
                {
                    global::Android.Util.Range fpsRange =
                        cameraConfig.FpsRange;

                    Log.Debug(
                        Tag,
                        $"FPS = {fpsRange}");
                }
                catch (Exception exception)
                {
                    Log.Debug(
                        Tag,
                        $"FPS = <unavailable: {exception.GetType().Name}>");
                }

                try
                {
                    global::Android.Util.Size textureSize =
                        cameraConfig.TextureSize;

                    Log.Debug(
                        Tag,
                        $"GPU Texture Size = {textureSize.Width}x{textureSize.Height}");
                }
                catch (Exception exception)
                {
                    Log.Debug(
                        Tag,
                        $"GPU Texture Size = <unavailable: {exception.GetType().Name}>");
                }

                try
                {
                    global::Android.Util.Size imageSize =
                        cameraConfig.ImageSize;

                    Log.Debug(
                        Tag,
                        $"CPU Image Size = {imageSize.Width}x{imageSize.Height}");
                }
                catch (Exception exception)
                {
                    Log.Debug(
                        Tag,
                        $"CPU Image Size = <unavailable: {exception.GetType().Name}>");
                }

                try
                {
                    CameraConfig.DepthSensorUsage depthSensorUsage =
                        cameraConfig.GetDepthSensorUsage();

                    Log.Debug(
                        Tag,
                        $"DepthSensorUsage = {depthSensorUsage}");
                }
                catch (Exception exception)
                {
                    Log.Debug(
                        Tag,
                        $"DepthSensorUsage = <unavailable: {exception.GetType().Name}>");
                }

                try
                {
                    CameraConfig.StereoCameraUsage stereoCameraUsage =
                        cameraConfig.GetStereoCameraUsage();

                    Log.Debug(
                        Tag,
                        $"StereoCameraUsage = {stereoCameraUsage}");
                }
                catch (Exception exception)
                {
                    Log.Debug(
                        Tag,
                        $"StereoCameraUsage = <unavailable: {exception.GetType().Name}>");
                }
            }

            Log.Debug(
                Tag,
                "========================================");
        }
        catch (Exception exception)
        {
            Log.Warn(
                Tag,
                "Unable to inspect ARCore camera configurations. " +
                "The default camera configuration will be preserved. " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void LogTextureIntrinsicsOnce(
        ArCoreCamera camera)
    {
        if (textureIntrinsicsLogged)
        {
            return;
        }

        try
        {
            CameraIntrinsics? intrinsics =
                camera.TextureIntrinsics;

            if (intrinsics is null)
            {
                Log.Warn(
                    Tag,
                    "ARCore TextureIntrinsics returned null.");

                return;
            }

            using (intrinsics)
            {
                float[]? focalLength =
                    intrinsics.GetFocalLength();

                float[]? principalPoint =
                    intrinsics.GetPrincipalPoint();

                int[]? dimensions =
                    intrinsics.GetImageDimensions();

                if (focalLength is null ||
                    focalLength.Length < 2 ||
                    principalPoint is null ||
                    principalPoint.Length < 2 ||
                    dimensions is null ||
                    dimensions.Length < 2)
                {
                    Log.Warn(
                        Tag,
                        "ARCore texture intrinsics returned incomplete data.");

                    return;
                }

                Log.Debug(
                    Tag,
                    "========== ARCore GPU Texture Intrinsics ==========");

                Log.Debug(
                    Tag,
                    $"Dimensions = {dimensions[0]}x{dimensions[1]}");

                Log.Debug(
                    Tag,
                    $"FocalLength = fx={focalLength[0]:F3}, fy={focalLength[1]:F3}");

                Log.Debug(
                    Tag,
                    $"PrincipalPoint = cx={principalPoint[0]:F3}, cy={principalPoint[1]:F3}");

                Log.Debug(
                    Tag,
                    "==================================================");
            }

            textureIntrinsicsLogged =
                true;
        }
        catch (Exception exception)
        {
            Log.Warn(
                Tag,
                "Unable to read ARCore texture intrinsics. " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void SelectDiagnosticCpuCameraConfig(
        Session currentSession)
    {
        try
        {
            using CameraConfigFilter cameraConfigFilter =
                new(currentSession);

            IList<CameraConfig> cameraConfigs =
                currentSession.GetSupportedCameraConfigs(
                    cameraConfigFilter);

            if (cameraConfigs.Count == 0)
            {
                Log.Warn(
                    Tag,
                    "No ARCore camera configs available for CPU diagnostic.");

                return;
            }

            CameraConfig? selectedConfig =
                null;

            long bestCpuArea =
                0;

            for (int index = 0;
                 index < cameraConfigs.Count;
                 index++)
            {
                CameraConfig candidate =
                    cameraConfigs[index];

                CameraConfig.FacingDirection facingDirection =
                    candidate.GetFacingDirection();

                if (facingDirection.ToString() != "BACK")
                {
                    continue;
                }

                global::Android.Util.Size gpuSize =
                    candidate.TextureSize;

                global::Android.Util.Size cpuSize =
                    candidate.ImageSize;

                if (gpuSize.Width != 1920 ||
                    gpuSize.Height != 1080)
                {
                    continue;
                }

                long cpuArea =
                    (long)cpuSize.Width *
                    cpuSize.Height;

                if (cpuArea <= bestCpuArea)
                {
                    continue;
                }

                bestCpuArea =
                    cpuArea;

                selectedConfig =
                    candidate;
            }

            if (selectedConfig is null)
            {
                Log.Warn(
                    Tag,
                    "No suitable diagnostic CPU camera config found.");

                return;
            }

            currentSession.CameraConfig =
                selectedConfig;

            Log.Debug(
                Tag,
                "========================================");

            Log.Debug(
                Tag,
                "Diagnostic CPU camera config SELECTED:");

            Log.Debug(
                Tag,
                $"GPU Texture Size = " +
                $"{selectedConfig.TextureSize.Width}x" +
                $"{selectedConfig.TextureSize.Height}");

            Log.Debug(
                Tag,
                $"CPU Image Size = " +
                $"{selectedConfig.ImageSize.Width}x" +
                $"{selectedConfig.ImageSize.Height}");

            Log.Debug(
                Tag,
                $"FPS = {selectedConfig.FpsRange}");

            Log.Debug(
                Tag,
                "========================================");
        }
        catch (Exception exception)
        {
            Log.Warn(
                Tag,
                "Unable to select diagnostic CPU camera config. " +
                $"{exception.GetType().Name}: " +
                $"{exception.Message}");
        }
    }

    public void RequestCpuDiagnosticCapture()
    {
        captureCpuDiagnosticRequested =
            true;

        Log.Debug(
            Tag,
            "ARCore CPU diagnostic capture requested.");
    }

    private static bool TryCaptureCpuDiagnosticImage(
        Frame frame)
    {
        try
        {
            using global::Android.Media.Image image =
                frame.AcquireCameraImage();

            Log.Debug(
                Tag,
                "========== ARCore CPU Image ==========");

            Log.Debug(
                Tag,
                $"Size = {image.Width}x{image.Height}");

            Log.Debug(
                Tag,
                $"Format = {image.Format}");

            global::Android.Media.Image.Plane[]? planes =
                image.GetPlanes();

            if (planes is null ||
                planes.Length < 3)
            {
                Log.Warn(
                    Tag,
                    "ARCore CPU image does not contain three YUV planes.");

                return true;
            }

            Log.Debug(
                Tag,
                $"Plane count = {planes.Length}");

            for (int index = 0;
                 index < planes.Length;
                 index++)
            {
                global::Android.Media.Image.Plane plane =
                    planes[index];

                Log.Debug(
                    Tag,
                    $"Plane[{index}] " +
                    $"RowStride={plane.RowStride}, " +
                    $"PixelStride={plane.PixelStride}, " +
                    $"Remaining={plane.Buffer?.Remaining() ?? 0}");
            }

            string? savedPath =
                SaveYuv420888AsJpeg(
                    image,
                    planes);

            if (savedPath is not null)
            {
                Log.Debug(
                    Tag,
                    "ARCore CPU diagnostic JPEG saved:");

                Log.Debug(
                    Tag,
                    savedPath);
            }
            else
            {
                Log.Warn(
                    Tag,
                    "ARCore CPU diagnostic JPEG was not saved.");
            }

            Log.Debug(
                Tag,
                "======================================");

            return true;
        }
        catch (
            Google.AR.Core.Exceptions.NotYetAvailableException)
        {
            return false;
        }
        catch (Exception exception)
        {
            Log.Warn(
                Tag,
                "ARCore CPU image diagnostic failed. " +
                $"{exception.GetType().Name}: " +
                $"{exception.Message}");

            return true;
        }
    }

    private static string? SaveYuv420888AsJpeg(
        global::Android.Media.Image image,
        global::Android.Media.Image.Plane[] planes)
    {
        int width =
            image.Width;

        int height =
            image.Height;

        global::Android.Media.Image.Plane yPlane =
            planes[0];

        global::Android.Media.Image.Plane uPlane =
            planes[1];

        global::Android.Media.Image.Plane vPlane =
            planes[2];

        Java.Nio.ByteBuffer? yBuffer =
            yPlane.Buffer;

        Java.Nio.ByteBuffer? uBuffer =
            uPlane.Buffer;

        Java.Nio.ByteBuffer? vBuffer =
            vPlane.Buffer;

        if (yBuffer is null ||
            uBuffer is null ||
            vBuffer is null)
        {
            Log.Warn(
                Tag,
                "One or more YUV plane buffers are null.");

            return null;
        }

        byte[] nv21 =
            new byte[
                width *
                height *
                3 /
                2];

        int destinationIndex =
            0;

        int yRowStride =
            yPlane.RowStride;

        int yPixelStride =
            yPlane.PixelStride;

        int yBufferPosition =
            yBuffer.Position();

        for (int row = 0;
             row < height;
             row++)
        {
            int rowOffset =
                yBufferPosition +
                row *
                yRowStride;

            for (int column = 0;
                 column < width;
                 column++)
            {
                int sourceIndex =
                    rowOffset +
                    column *
                    yPixelStride;

                nv21[destinationIndex++] =
                    unchecked(
                        (byte)yBuffer.Get(
                            sourceIndex));
            }
        }

        int chromaWidth =
            width / 2;

        int chromaHeight =
            height / 2;

        int uRowStride =
            uPlane.RowStride;

        int uPixelStride =
            uPlane.PixelStride;

        int vRowStride =
            vPlane.RowStride;

        int vPixelStride =
            vPlane.PixelStride;

        int uBufferPosition =
            uBuffer.Position();

        int vBufferPosition =
            vBuffer.Position();

        for (int row = 0;
             row < chromaHeight;
             row++)
        {
            int uRowOffset =
                uBufferPosition +
                row *
                uRowStride;

            int vRowOffset =
                vBufferPosition +
                row *
                vRowStride;

            for (int column = 0;
                 column < chromaWidth;
                 column++)
            {
                int uIndex =
                    uRowOffset +
                    column *
                    uPixelStride;

                int vIndex =
                    vRowOffset +
                    column *
                    vPixelStride;

                byte v =
                    unchecked(
                        (byte)vBuffer.Get(
                            vIndex));

                byte u =
                    unchecked(
                        (byte)uBuffer.Get(
                            uIndex));

                nv21[destinationIndex++] =
                    v;

                nv21[destinationIndex++] =
                    u;
            }
        }

        using global::Android.Graphics.YuvImage yuvImage =
            new(
                nv21,
                global::Android.Graphics.ImageFormatType.Nv21,
                width,
                height,
                null);

        string fileName =
            $"arcore_cpu_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";

        Java.IO.File directory =
            global::Android.App.Application.Context
                .GetExternalFilesDir(
                    global::Android.OS.Environment.DirectoryPictures)
            ??
            global::Android.App.Application.Context.FilesDir!;

        if (!directory.Exists())
        {
            directory.Mkdirs();
        }

        string filePath =
            System.IO.Path.Combine(
                directory.AbsolutePath,
                fileName);

        using System.IO.FileStream outputStream =
            new(
                filePath,
                System.IO.FileMode.Create,
                System.IO.FileAccess.Write,
                System.IO.FileShare.None);

        global::Android.Graphics.Rect cropRect =
            new(
                0,
                0,
                width,
                height);

        bool compressed =
            yuvImage.CompressToJpeg(
                cropRect,
                100,
                outputStream);

        outputStream.Flush();

        if (!compressed)
        {
            Log.Warn(
                Tag,
                "YuvImage.CompressToJpeg() returned false.");

            return null;
        }

        return filePath;
    }

    private static void SelectThirtyFpsCameraConfig(
        Session currentSession)
    {
        try
        {
            CameraConfig? currentConfig =
                currentSession.CameraConfig;

            if (currentConfig is null)
            {
                Log.Warn(
                    Tag,
                    "Current ARCore camera config unavailable.");

                return;
            }

            global::Android.Util.Size currentGpuSize =
                currentConfig.TextureSize;

            using CameraConfigFilter cameraConfigFilter =
                new(currentSession);

            IList<CameraConfig> cameraConfigs =
                currentSession.GetSupportedCameraConfigs(
                    cameraConfigFilter);

            CameraConfig? selectedConfig =
                null;

            foreach (CameraConfig candidate in cameraConfigs)
            {
                if (candidate.GetFacingDirection()
                        .ToString() != "BACK")
                {
                    continue;
                }

                global::Android.Util.Size gpuSize =
                    candidate.TextureSize;

                /*
                 * Preserve the GPU texture dimensions already proven to work
                 * with the Vulkan camera pipeline.
                 */
                if (gpuSize.Width != currentGpuSize.Width ||
                    gpuSize.Height != currentGpuSize.Height)
                {
                    continue;
                }

                global::Android.Util.Range fpsRange =
                    candidate.FpsRange;

                int minimumFps =
                    Convert.ToInt32(
                        fpsRange.Lower?.ToString());

                int maximumFps =
                    Convert.ToInt32(
                        fpsRange.Upper?.ToString());

                if (minimumFps == 30 &&
                    maximumFps == 30)
                {
                    selectedConfig =
                        candidate;

                    break;
                }
            }

            if (selectedConfig is null)
            {
                Log.Warn(
                    Tag,
                    "No matching 30 FPS ARCore camera config found. " +
                    "Default configuration retained.");

                return;
            }

            /*
             * Camera configuration is selected once while the Session is
             * initially paused. Pause/resume tab transitions reuse it.
             */
            currentSession.CameraConfig =
                selectedConfig;

            Log.Debug(
                Tag,
                "========================================");

            Log.Debug(
                Tag,
                "ARCore camera capped at 30 FPS.");

            Log.Debug(
                Tag,
                $"FPS = {selectedConfig.FpsRange}");

            Log.Debug(
                Tag,
                $"GPU Texture = " +
                $"{selectedConfig.TextureSize.Width}x" +
                $"{selectedConfig.TextureSize.Height}");

            Log.Debug(
                Tag,
                "========================================");
        }
        catch (Exception exception)
        {
            Log.Warn(
                Tag,
                "Unable to select 30 FPS ARCore camera config. " +
                $"{exception.GetType().Name}: " +
                $"{exception.Message}");
        }
    }
}
