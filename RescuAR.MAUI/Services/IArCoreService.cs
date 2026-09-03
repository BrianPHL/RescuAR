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

    bool IsInitialized { get; }

    Session? Session { get; }

    bool IsFrameLoopRunning { get; }

    bool IsSessionPaused { get; }
}
