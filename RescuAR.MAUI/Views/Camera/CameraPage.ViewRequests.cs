using RescuAR.MAUI.Services.Navigation;
using RescuAR.Navigation.State;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private bool cameraViewRequestObserved;
    private void ObserveCameraViewRequests()
    {
        if (!cameraViewRequestObserved)
        {
            CameraViewHandoff.Current.Requested += CameraViewRequested;
            cameraViewRequestObserved = true;
        }
        ApplyPendingCameraViewRequest();
    }
    private void UnobserveCameraViewRequests()
    {
        if (cameraViewRequestObserved) CameraViewHandoff.Current.Requested -= CameraViewRequested;
        cameraViewRequestObserved = false;
    }
    private void CameraViewRequested() => Dispatcher.Dispatch(ApplyPendingCameraViewRequest);
    private void ApplyPendingCameraViewRequest()
    {
        if (!pageIsVisible || cameraMapWindowStopped) return;
        if (CameraViewHandoff.Current.TryConsume(NavigationDestinationBridge.Current.Version, out var view))
            ApplyCameraModuleView(view == CameraPresentationView.Map2D ? CameraModuleViewMode.Map2D : CameraModuleViewMode.ArCamera,
                "explicit directory/map presentation request");
    }
}
