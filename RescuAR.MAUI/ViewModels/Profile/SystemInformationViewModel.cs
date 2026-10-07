using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RescuAR.MAUI.Services.Navigation;

namespace RescuAR.App.ViewModels.Profile;

public partial class SystemInformationViewModel : ObservableObject
{
    public string AppVersion => $"{AppInfo.Current.VersionString} (build {AppInfo.Current.BuildString})";
    public string DeviceText => $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}";
    public string SystemText => $"{DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString}";
    [ObservableProperty] private string connectionText = "Checking…";
    [ObservableProperty] private string cameraPermission = "Checking…";
    [ObservableProperty] private string locationPermission = "Checking…";
    [ObservableProperty] private string contactsPermission = "Checking…";
    [ObservableProperty] private string mapStorageText = "Checking…";

    [RelayCommand]
    public async Task RefreshAsync()
    {
        ConnectionText = Connectivity.Current.NetworkAccess.ToString();
        CameraPermission = await AppSettingsViewModel.PermissionTextAsync<Permissions.Camera>();
        LocationPermission = await AppSettingsViewModel.PermissionTextAsync<Permissions.LocationWhenInUse>();
        ContactsPermission = await AppSettingsViewModel.PermissionTextAsync<Permissions.ContactsRead>();
        try { MapStorageText = $"{DetailedOfflineMapService.LocalCopySize / (1024.0 * 1024.0):F1} MB local detailed-map copy"; }
        catch { MapStorageText = "Local map storage unavailable"; }
    }
}
