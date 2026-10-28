using Microsoft.Maui.Dispatching;
using RescuAR.AR;
using RescuAR.MAUI.Services;
using RescuAR.MAUI.Services.Navigation;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private IDispatcherTimer? cameraStartupTimer;
    private long? cameraStartupWaitingSince;

    private void RefreshCameraStartupPresentation()
    {
        if (Dispatcher.IsDispatchRequired) { Dispatcher.Dispatch(RefreshCameraStartupPresentation); return; }
        bool active = pageIsVisible && !cameraMapWindowStopped && currentCameraModuleView != CameraModuleViewMode.Map2D;
        long now = Environment.TickCount64;
        if (active) cameraStartupWaitingSince ??= now;

        var lifecycle = _arCoreService.LifecycleSnapshot;
        string? failure = lifecycle.State == ArCoreLifecycleState.Faulted || cameraPipelineTerminalFailureVisible
            ? (string.IsNullOrWhiteSpace(lifecycle.Failure.Message) ? "The camera could not start. Check camera access or switch to 2D Map." : lifecycle.Failure.Message)
            : null;
        var presentation = CameraStartupPresentation.Resolve(active,
            lifecycle.State == ArCoreLifecycleState.Running && !_arCoreService.IsSessionPaused,
            ARCameraTextureBridge.Current.Texture is not null,
            TimeSpan.FromMilliseconds(cameraStartupWaitingSince.HasValue ? Math.Max(0, now - cameraStartupWaitingSince.Value) : 0), failure);

        cameraStartupOverlay.IsVisible = presentation.Visible;
        cameraStartupSpinner.IsRunning = presentation.Busy;
        cameraStartupTitle.Text = presentation.Title ?? string.Empty;
        cameraStartupMessage.Text = presentation.Message ?? string.Empty;
        if (!presentation.Visible)
        {
            cameraStartupTimer?.Stop();
            cameraStartupWaitingSince = null;
            return;
        }

        if (cameraStartupTimer is null)
        {
            cameraStartupTimer = Dispatcher.CreateTimer();
            cameraStartupTimer.IsRepeating = true;
            cameraStartupTimer.Tick += (_, _) => RefreshCameraStartupPresentation();
        }
        cameraStartupTimer.Interval = TimeSpan.FromMilliseconds(presentation.Busy ? 200 : 1000);
        if (!cameraStartupTimer.IsRunning) cameraStartupTimer.Start();
    }
}
