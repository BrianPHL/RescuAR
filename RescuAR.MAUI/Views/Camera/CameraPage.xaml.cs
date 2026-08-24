#if ANDROID
using Android.Util;
#endif

using Microsoft.Extensions.DependencyInjection;
using RescuAR;
using RescuAR.MAUI;
using RescuAR.MAUI.Services;

namespace RescuAR.App.Views.Camera
{
    public partial class CameraPage : ContentPage
    {
        private readonly MyApplication evergineApplication;
        private readonly IArCoreService _arCoreService;

        public CameraPage(IArCoreService arCoreService)
        {
            InitializeComponent();
            this.evergineApplication = new MyApplication();
            this.evergineView.Application = this.evergineApplication;
            _arCoreService =
                arCoreService;
        }

        private async void InitializeArCoreClicked(
            object sender,
            EventArgs e)
        {
#if ANDROID

            Log.Debug(
                "RescuAR-ARCore",
                "Initialize ARCore button clicked.");

            var permissionStatus =
                await Permissions.RequestAsync<
                    Permissions.Camera>();

            Log.Debug(
                "RescuAR-ARCore",
                $"Camera permission status: {permissionStatus}");

            if (permissionStatus !=
                PermissionStatus.Granted)
            {
                Log.Error(
                    "RescuAR-ARCore",
                    "Camera permission was not granted.");

                await DisplayAlert(
                    "ARCore",
                    "Camera permission was not granted.",
                    "OK");

                return;
            }

            Log.Debug(
                "RescuAR-ARCore",
                "Camera permission granted.");

            var initialized =
                _arCoreService.Initialize();

            Log.Debug(
                "RescuAR-ARCore",
                $"Initialize() returned: {initialized}");

            if (initialized)
            {
                await DisplayAlert(
                    "ARCore",
                    "ARCore Session initialized successfully.",
                    "OK");
            }
            else
            {
                await DisplayAlert(
                    "ARCore",
                    "ARCore Session was not initialized. Check Logcat.",
                    "OK");
            }

#endif
        }

        private async void UpdateArCoreClicked(
            object sender,
            EventArgs e)
        {
            if (!_arCoreService.IsInitialized)
            {
                await DisplayAlert(
                    "ARCore",
                    "Initialize ARCore first.",
                    "OK");

                return;
            }

            try
            {
                var frame =
                    _arCoreService.Update();

                if (frame == null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[ARCore] Frame is NULL.");

                    return;
                }

                var camera =
                    frame.Camera;

                System.Diagnostics.Debug.WriteLine(
                    $"[ARCore] Frame Timestamp: {frame.Timestamp}");

                System.Diagnostics.Debug.WriteLine(
                    $"[ARCore] Camera Tracking State: {camera.TrackingState}");

                System.Diagnostics.Debug.WriteLine(
                    $"[ARCore] Tracking Failure Reason: {camera.TrackingFailureReason}");

                System.Diagnostics.Debug.WriteLine(
                    $"[ARCore] Camera Texture Name: {frame.CameraTextureName}");

                System.Diagnostics.Debug.WriteLine(
                    $"[ARCore] Hardware Buffer: {frame.HardwareBuffer}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ARCore] Frame Update Exception: {ex}");
            }
        }

        //private async void OnTestARCoreClicked(object sender, EventArgs e)
        //{
        //    var status = await Permissions.RequestAsync<Permissions.Camera>();

        //    if (status != PermissionStatus.Granted)
        //    {
        //        await DisplayAlert(
        //            "ARCore Test",
        //            "Camera permission was not granted.",
        //            "OK");

        //        return;
        //    }

        //    await DisplayAlert(
        //        "ARCore Test",
        //        "Camera permission granted.",
        //        "OK");
        //}
    }
}
