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

    bool IsInitialized { get; }

    Session? Session { get; }

    bool IsFrameLoopRunning { get; }

    bool IsSessionPaused { get; }
}
