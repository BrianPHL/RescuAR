using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Devices.Sensors;
using RescuAR.App.Services.Profile;

namespace RescuAR.App.ViewModels.Profile;

public partial class SystemInformationViewModel : ObservableObject
{
    [ObservableProperty] private string appVersion = "Unavailable";
    [ObservableProperty] private string lastUpdated = "Unavailable";
    [ObservableProperty] private string cameraPermission = "Checking…";
    [ObservableProperty] private string locationPermission = "Checking…";
    [ObservableProperty] private string motionPermission = "Checking…";
    [ObservableProperty] private string notificationsPermission = "Checking…";
    [ObservableProperty] private string statusMessage = string.Empty;

    public async Task LoadPermissionsAsync()
    {
        try { AppVersion = SystemInformationPresentation.Version(AppInfo.Current.VersionString, AppInfo.Current.BuildString); }
        catch { AppVersion = "Unavailable"; }
        LastUpdated = ReadUpdateDate();
        CameraPermission = await ReadPermissionAsync<Permissions.Camera>();
        LocationPermission = await ReadPermissionAsync<Permissions.LocationWhenInUse>();
        try { MotionPermission = Accelerometer.Default.IsSupported && Gyroscope.Default.IsSupported ? "Sensors available" : "Some sensors unavailable"; }
        catch { MotionPermission = "Unavailable"; }
#if ANDROID
        try
        {
            using var manager = global::AndroidX.Core.App.NotificationManagerCompat.From(global::Android.App.Application.Context);
            bool? enabled = manager?.AreNotificationsEnabled();
            NotificationsPermission = enabled is null ? "Unavailable" : enabled.Value ? (OperatingSystem.IsAndroidVersionAtLeast(33)
                ? await ReadPermissionAsync<Permissions.PostNotifications>() : "Allowed") : "Disabled in device settings";
        }
        catch { NotificationsPermission = "Unavailable"; }
#else
        NotificationsPermission = "Unavailable";
#endif
    }

    private static async Task<string> ReadPermissionAsync<T>() where T : Permissions.BasePermission, new()
    {
        try
        {
            return (await Permissions.CheckStatusAsync<T>()) switch
            {
                PermissionStatus.Granted => "Allowed", PermissionStatus.Denied => "Denied", PermissionStatus.Disabled => "Disabled",
                PermissionStatus.Restricted => "Restricted", PermissionStatus.Limited => "Limited", _ => "Unknown"
            };
        }
        catch { return "Unavailable"; }
    }

    private static string ReadUpdateDate()
    {
#if ANDROID
        try
        {
            var context = global::Android.App.Application.Context;
            using var package = context.PackageManager?.GetPackageInfo(context.PackageName!, global::Android.Content.PM.PackageInfoFlags.MetaData);
            return SystemInformationPresentation.Updated(package?.LastUpdateTime);
        }
        catch { }
#endif
        return "Unavailable";
    }

    [RelayCommand]
    private void ManagePermissions()
    {
        try { AppInfo.ShowSettingsUI(); StatusMessage = string.Empty; }
        catch { StatusMessage = "Device settings could not be opened. Open this app's permissions in your device settings."; }
    }
    [RelayCommand] private async Task Back() { if (Shell.Current != null) await Shell.Current.GoToAsync(".."); }
}
