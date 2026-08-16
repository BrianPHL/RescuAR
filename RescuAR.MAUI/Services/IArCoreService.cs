using Google.AR.Core;
using Frame = Google.AR.Core.Frame;

namespace RescuAR.MAUI.Services;

public interface IArCoreService
{
    ArCoreApk.Availability CheckAvailability();

    ArCoreApk.InstallStatus RequestInstall();

    bool Initialize();

    Frame? Update();

    bool IsInitialized { get; }

    Session? Session { get; }
}
