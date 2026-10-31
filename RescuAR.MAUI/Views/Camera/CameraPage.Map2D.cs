using Microsoft.Maui.Controls;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private Window? cameraMapWindow;
    private bool cameraMapWindowStopped;

    private void ObserveCameraMapWindow()
    {
        if (cameraMapWindow != Window)
        {
            UnobserveCameraMapWindow();
            cameraMapWindow = Window;
            cameraMapWindowStopped = false;
            if (cameraMapWindow is not null)
            {
                cameraMapWindow.Stopped += CameraMapWindowStopped;
                cameraMapWindow.Resumed += CameraMapWindowResumed;
            }
        }
        ObserveCameraViewRequests();
        RefreshCameraStartupPresentation();
    }

    private void UnobserveCameraMapWindow()
    {
        UnobserveCameraViewRequests();
        if (cameraMapWindow is null) return;
        cameraMapWindow.Stopped -= CameraMapWindowStopped;
        cameraMapWindow.Resumed -= CameraMapWindowResumed;
        cameraMapWindow = null;
    }

    private void CameraMapWindowStopped(object? sender, EventArgs args)
    {
        cameraMapWindowStopped = true;
        StopCameraMapDisplay();
    }

    private void CameraMapWindowResumed(object? sender, EventArgs args)
    {
        cameraMapWindowStopped = false;
        ApplyPendingCameraViewRequest();
        RefreshCameraModuleDynamicUi();
    }

    private void StopCameraMapDisplay() => RefreshCameraStartupPresentation();
}
