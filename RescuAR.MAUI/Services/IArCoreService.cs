using Google.AR.Core;
using Frame = Google.AR.Core.Frame;

namespace RescuAR.MAUI.Services;

public interface IArCoreService
{
    ArCoreApk.Availability CheckAvailability();

    ArCoreApk.InstallStatus RequestInstall();

    bool Initialize();

    Frame? Update();

    /// <summary>
    /// Pauses the retained ARCore Session and releases the physical camera
    /// when the Camera tab is no longer active.
    ///
    /// The Session and navigation/guidance state remain retained.
    /// </summary>
    void PauseCameraSession();

    /// <summary>
    /// Resumes an already-created ARCore Session and restarts its frame loop.
    ///
    /// Returns false when there is no retained Session or resume fails.
    /// </summary>
    bool ResumeCameraSession();

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
    /// Performs a non-destructive health check of the retained ground anchor.
    ///
    /// When ARCore camera tracking has recovered but the existing anchor
    /// remains non-tracking beyond a short grace period, the stale anchor is
    /// released. The existing ARCore frame loop will then automatically resume
    /// its normal horizontal-floor hit-test acquisition.
    ///
    /// Returns true when ground-anchor reacquisition is/was armed.
    /// </summary>
    bool TryRecoverGroundAnchorIfNeeded();

    /// <summary>
    /// Monotonically increasing generation that changes only after the
    /// recovery service has actually released a stale retained ground anchor
    /// and armed replacement floor-anchor acquisition.
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

    bool IsInitialized { get; }

    Session? Session { get; }

    bool IsFrameLoopRunning { get; }

    bool IsSessionPaused { get; }
}
