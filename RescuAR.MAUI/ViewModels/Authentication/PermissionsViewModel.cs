using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Storage;

using RescuAR.App.Services.Authentication;

using RescuAR.MAUI;

namespace RescuAR.App.ViewModels.Authentication
{
    public enum PermissionStep
    {
        Camera = 0,
        Location = 1,
        MotionAndOrientation = 2,
        Notifications = 3
    }

    public partial class PermissionsViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;

        // SVG Icons for Watermarks
        private const string CameraWatermark = "M12,17.5C10.07,17.5 8.5,15.93 8.5,14C8.5,12.07 10.07,10.5 12,10.5C13.93,10.5 15.5,12.07 15.5,14C15.5,15.93 13.93,17.5 12,17.5M9,2L7.17,4H4A2,2 0 0,0 2,6V20A2,2 0 0,0 4,22H20A2,2 0 0,0 22,20V6A2,2 0 0,0 20,4H16.83L15,2H9M12,7A7,7 0 0,1 19,14A7,7 0 0,1 12,21A7,7 0 0,1 5,14A7,7 0 0,1 12,7Z";
        private const string LocationWatermark = "M12,2C8.13,2 5,5.13 5,9C5,14.25 12,22 12,22C12,22 19,14.25 19,9C19,5.13 15.87,2 12,2M12,6.5A2.5,2.5 0 0,1 14.5,9A2.5,2.5 0 0,1 12,11.5A2.5,2.5 0 0,1 9.5,9A2.5,2.5 0 0,1 12,6.5M12,0C6.48,0 2,4.48 2,10C2,17.5 12,24 12,24C12,24 22,17.5 22,10C22,4.48 17.52,0 12,0Z";
        private const string GyroscopeWatermark = "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,4A8,8 0 0,1 19.32,9H16.14C15.42,7.21 13.88,5.77 12,5.21V4M12,6.72C13.23,7.2 14.23,8.19 14.71,9.42H9.29C9.77,8.19 10.77,7.2 12,6.72M4.68,15H7.86C8.58,16.79 10.12,18.23 12,18.79V20A8,8 0 0,1 4.68,15M12,17.28C10.77,16.8 9.77,15.81 9.29,14.58H14.71C14.23,15.81 13.23,16.8 12,17.28M7.86,9H4.68A8,8 0 0,1 12,4V5.21C10.12,5.77 8.58,7.21 7.86,9M12,18.79C13.88,18.23 15.42,16.79 16.14,15H19.32A8,8 0 0,1 12,20V18.79Z";
        private const string NotificationWatermark = "M12,2A2,2 0 0,0 10,4A7,7 0 0,0 5,11V16L3,18V19H21V18L19,16V11A7,7 0 0,0 14,4A2,2 0 0,0 12,2M12,22A2,2 0 0,0 14,20H10A2,2 0 0,0 12,22M12,6A5,5 0 0,1 17,11V17H7V11A5,5 0 0,1 12,6Z";

        [ObservableProperty]
        private PermissionStep _currentStep = PermissionStep.Camera;

        [ObservableProperty]
        private string _subHeader = "RescuAR needs access to your";

        [ObservableProperty]
        private string _title = "Camera";

        [ObservableProperty]
        private string _description = "Augmented Reality uses your camera to guide you to verified evacuation centers during emergencies.";

        [ObservableProperty]
        private string _actionButtonText = "Allow Camera Access";

        [ObservableProperty]
        private string _watermarkPath = CameraWatermark;

        [ObservableProperty]
        private bool _isBusy = false;

        public bool IsNotBusy => !IsBusy;

        partial void OnIsBusyChanged(bool value)
        {
            OnPropertyChanged(nameof(IsNotBusy));
        }

        public PermissionsViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            UpdateStepUI();
        }

        private void UpdateStepUI()
        {
            switch (CurrentStep)
            {
                case PermissionStep.Camera:
                    SubHeader = "RescuAR needs access to your";
                    Title = "Camera";
                    Description = "Augmented Reality uses your camera to guide you to verified evacuation centers during emergencies.";
                    ActionButtonText = "Allow Camera Access";
                    WatermarkPath = CameraWatermark;
                    break;

                case PermissionStep.Location:
                    SubHeader = "RescuAR needs access to your";
                    Title = "Location";
                    Description = "Location is used to determine safe routes and nearby evacuation centers. Required for navigation accuracy.";
                    ActionButtonText = "Allow Location Access";
                    WatermarkPath = LocationWatermark;
                    break;

                case PermissionStep.MotionAndOrientation:
                    SubHeader = "RescuAR needs access to your";
                    Title = "Motion & Orientation";
                    Description = "Motion sensors align AR guidance with your real-world surroundings for accurate directional instructions.";
                    ActionButtonText = "Allow Motion & Orientation Access";
                    WatermarkPath = GyroscopeWatermark;
                    break;

                case PermissionStep.Notifications:
                    SubHeader = "RescuAR needs to enable your";
                    Title = "Notifications";
                    Description = "Receive verified earthquake alerts and safety updates in real time. Internet required.";
                    ActionButtonText = "Enable Notifications";
                    WatermarkPath = NotificationWatermark;
                    break;
            }
        }

        [RelayCommand]
        private async Task RequestCurrentPermissionAsync()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                switch (CurrentStep)
                {
                    case PermissionStep.Camera:
                        await RequestCameraPermissionAsync();
                        break;

                    case PermissionStep.Location:
                        await RequestLocationPermissionAsync();
                        break;

                    case PermissionStep.MotionAndOrientation:
                        await RequestMotionPermissionAsync();
                        break;

                    case PermissionStep.Notifications:
                        await RequestNotificationsPermissionAsync();
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Permissions] Error requesting permission: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                AdvanceToNextStep();
            }
        }

        [RelayCommand]
        private void SkipCurrentPermission()
        {
            AdvanceToNextStep();
        }

        private async Task RequestCameraPermissionAsync()
        {
            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                await Permissions.RequestAsync<Permissions.Camera>();
            }
        }

        private async Task RequestLocationPermissionAsync()
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }

#if ANDROID
            try
            {
                var context = Android.App.Application.Context;
                var locationManager = (Android.Locations.LocationManager?)context.GetSystemService(Android.Content.Context.LocationService);
                bool isGpsEnabled = false;
                if (locationManager != null)
                {
                    if (OperatingSystem.IsAndroidVersionAtLeast(28))
                    {
                        isGpsEnabled = locationManager.IsLocationEnabled;
                    }
                    else
                    {
                        isGpsEnabled = locationManager.IsProviderEnabled(Android.Locations.LocationManager.GpsProvider)
                                    || locationManager.IsProviderEnabled(Android.Locations.LocationManager.NetworkProvider);
                    }
                }

                if (!isGpsEnabled)
                {
                    var intent = new Android.Content.Intent(Android.Provider.Settings.ActionLocationSourceSettings);
                    intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                    context.StartActivity(intent);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Permissions] Failed to launch Location Settings: {ex.Message}");
            }
#endif

            // Warm up Geolocation service
            _ = Task.Run(async () =>
            {
                try
                {
                    await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)));
                }
                catch { }
            });
        }

        private async Task RequestMotionPermissionAsync()
        {
            try
            {
                // Verify and initialize motion & orientation sensors (Compass, Gyroscope/Orientation)
                if (Compass.Default.IsSupported && !Compass.Default.IsMonitoring)
                {
                    Compass.Default.Start(SensorSpeed.UI);
                    await Task.Delay(100);
                    Compass.Default.Stop();
                }

                if (OrientationSensor.Default.IsSupported && !OrientationSensor.Default.IsMonitoring)
                {
                    OrientationSensor.Default.Start(SensorSpeed.UI);
                    await Task.Delay(100);
                    OrientationSensor.Default.Stop();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Permissions] Motion sensor check: {ex.Message}");
            }
        }

        private async Task RequestNotificationsPermissionAsync()
        {
            var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            if (status != PermissionStatus.Granted)
            {
                await Permissions.RequestAsync<Permissions.PostNotifications>();
            }
        }

        private void AdvanceToNextStep()
        {
            if (CurrentStep < PermissionStep.Notifications)
            {
                CurrentStep++;
                UpdateStepUI();
            }
            else
            {
                CompletePermissionsFlow();
            }
        }

        private void CompletePermissionsFlow()
        {
            Preferences.Default.Set("HasCompletedPermissions", true);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Application.Current != null)
                {
                    var shell = new AppShell();
                    if (Application.Current.Windows.Count > 0)
                    {
                        Application.Current.Windows[0].Page = shell;
                    }
                    else
                    {
#pragma warning disable CS0618
                        AuthenticationNavigation.TrySetRootPage(shell);
#pragma warning restore CS0618
                    }
                }
            });
        }
    }
}
