using RescuAR.AR;

namespace RescuAR.MAUI.Services;

public interface IArCoreService : IAsyncDisposable
{
    event Action<ArCoreLifecycleSnapshot>? LifecycleChanged;

    /// <summary>
    /// The latest immutable lifecycle/capability result. ARCore session
    /// objects are intentionally not exposed outside this owner.
    /// </summary>
    ArCoreLifecycleSnapshot LifecycleSnapshot { get; }

    ArCoreCapabilitySnapshot CapabilitySnapshot { get; }

    /// <summary>
    /// The authoritative tracking transition snapshot shared by UI and
    /// render gates. Lifecycle pauses are explicitly separated from active
    /// session tracking degradation.
    /// </summary>
    ARTrackingStateBridge.TrackingSnapshot TrackingSnapshot { get; }

    Task<ArCoreLifecycleResult> EnsureRunningAsync(
        CancellationToken cancellationToken = default);

    Task<ArCoreLifecycleResult> PauseAsync(
        CancellationToken cancellationToken = default);

    Task<ArCoreLifecycleResult> ShutdownAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lifecycle callbacks that cannot be awaited use these request methods.
    /// The service retains and observes the resulting transition task.
    /// </summary>
    void RequestPause(
        string reason);

    void RequestShutdown(
        string reason);

    void NotifyActivityResumed();

    void NotifyActivityPaused();

    /// <summary>
    /// Current synchronized presentation zoom applied to both the ARCore
    /// camera background and virtual-camera projection.
    /// </summary>
    float CameraZoomRatio { get; }

    /// <summary>
    /// Applies a centered AR-safe presentation zoom. The service keeps the
    /// camera feed and AR projection on the same ratio so overlays remain
    /// registered with the physical scene.
    /// </summary>
    void SetCameraZoomRatio(float zoomRatio);

    /// <summary>
    /// True after ARCore successfully configured torch mode for the retained
    /// camera session.
    /// </summary>
    bool IsFlashlightOn { get; }

    /// <summary>
    /// Enables or disables the device torch through ARCore's FlashMode API.
    /// Returns false when the current camera has no flash unit or the ARCore
    /// session is not available.
    /// </summary>
    Task<bool> SetFlashlightAsync(bool enabled);

    /// <summary>
    /// Monotonically increasing generation that changes after the service has
    /// released either a stale anchor or a valid anchor that has moved beyond
    /// the local AR navigation radius, then armed replacement acquisition.
    ///
    /// Temporary anchor PAUSED/unavailable states during the natural
    /// relocalization grace period do not change this value.
    ///
    /// CameraPage uses this durable event to distinguish:
    ///
    ///     temporary natural relocalization
    ///
    /// from:
    ///
    ///     actual stale-anchor replacement
    /// </summary>
    long GroundAnchorReplacementGeneration { get; }

    /// <summary>
    /// True while the spatial bridge is using a short-lived camera-height
    /// floor estimate so guidance can start before ARCore confirms a Plane or
    /// DepthPoint. The service continues searching for verified ground and
    /// clears this flag as soon as a tracked ARCore anchor replaces it.
    /// </summary>
    bool IsGroundAnchorProvisional { get; }

    bool IsInitialized { get; }

    bool IsFrameLoopRunning { get; }

    bool IsSessionPaused { get; }
}
