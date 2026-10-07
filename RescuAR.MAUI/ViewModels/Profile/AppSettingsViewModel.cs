using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.MAUI.Services.Settings;

namespace RescuAR.App.ViewModels.Profile;

public partial class AppSettingsViewModel : ObservableObject
{
    private bool initializing;
    [ObservableProperty] private bool advisoryPopupsEnabled;
    [ObservableProperty] private bool emergencySirenEnabled;
    [ObservableProperty] private bool hapticFeedbackEnabled;
    [ObservableProperty] private bool detailedOfflineMapEnabled;
    [ObservableProperty] private bool navigationVoiceEnabled;
    [ObservableProperty] private string cameraPermission = "Checking…";
    [ObservableProperty] private string locationPermission = "Checking…";
    [ObservableProperty] private string mapStorageText = "Checking…";
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsNotBusy))] private bool isBusy;
    public bool IsNotBusy => !IsBusy;

    public async Task InitializeAsync()
    {
        initializing = true;
        try
        {
            AdvisoryPopupsEnabled = AppPreferences.Current.Get(AppSetting.ForegroundAdvisories);
            EmergencySirenEnabled = AppPreferences.Current.Get(AppSetting.EmergencySiren);
            HapticFeedbackEnabled = AppPreferences.Current.Get(AppSetting.HapticFeedback);
            DetailedOfflineMapEnabled = AppPreferences.Current.Get(AppSetting.DetailedOfflineMap);
            NavigationVoiceEnabled = AppPreferences.Current.Get(AppSetting.NavigationVoice);
        }
        finally { initializing = false; }
        CameraPermission = await PermissionTextAsync<Permissions.Camera>();
        LocationPermission = await PermissionTextAsync<Permissions.LocationWhenInUse>();
        RefreshStorage();
    }

    internal static async Task<string> PermissionTextAsync<T>() where T : Permissions.BasePermission, new()
    {
        try { return (await Permissions.CheckStatusAsync<T>()).ToString(); }
        catch { return "Unavailable"; }
    }

    private void Save(AppSetting setting, bool value)
    {
        if (initializing) return;
        try { AppPreferences.Current.Set(setting, value); StatusText = "Saved on this device."; }
        catch { StatusText = "Could not save this preference. Reopen settings to retry."; }
    }
    partial void OnAdvisoryPopupsEnabledChanged(bool value) => Save(AppSetting.ForegroundAdvisories, value);
    partial void OnEmergencySirenEnabledChanged(bool value) => Save(AppSetting.EmergencySiren, value);
    partial void OnHapticFeedbackEnabledChanged(bool value) => Save(AppSetting.HapticFeedback, value);
    partial void OnDetailedOfflineMapEnabledChanged(bool value) => Save(AppSetting.DetailedOfflineMap, value);
    partial void OnNavigationVoiceEnabledChanged(bool value) => Save(AppSetting.NavigationVoice, value);

    private void RefreshStorage()
    {
        try
        {
            long size = DetailedOfflineMapService.LocalCopySize;
            MapStorageText = size == 0 ? "No local copy. Loads from the app package when a detailed map opens."
                : $"{size / (1024.0 * 1024.0):F1} MB local copy • available without internet";
        }
        catch { MapStorageText = "Local map storage unavailable."; }
    }

    [RelayCommand]
    private async Task RemoveMapCopyAsync()
    {
        if (IsBusy || Shell.Current is null || !await Shell.Current.DisplayAlert("Remove local map copy?",
                "Detailed maps will be turned off. The bundled map can reload when you turn them on again. Routes, contacts and pending circle messages are kept.", "Remove", "Cancel")) return;
        IsBusy = true;
        try
        {
            AppPreferences.Current.Set(AppSetting.DetailedOfflineMap, false);
            DetailedOfflineMapEnabled = false;
            await DetailedOfflineMapService.RemoveLocalCopyAsync();
            StatusText = "Local map copy removed. Turn on detailed maps to reload it.";
        }
        catch { StatusText = "Could not remove the local map copy. Try again."; }
        finally { RefreshStorage(); IsBusy = false; }
    }

    [RelayCommand]
    private void OpenDeviceSettings()
    {
        try { AppInfo.ShowSettingsUI(); }
        catch { StatusText = "Open your device's Settings app to manage RescuAR permissions."; }
    }
}
