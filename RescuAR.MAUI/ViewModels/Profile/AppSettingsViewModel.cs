using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RescuAR.MAUI.Services.Settings;
using RescuAR.MAUI.Services.Navigation;

namespace RescuAR.App.ViewModels.Profile;

public partial class AppSettingsViewModel : ObservableObject
{
    private readonly AppSettingsStore settings = AppPreferences.Current;
    private readonly DerivedMapCache maps = new(FileSystem.CacheDirectory);
    private bool initializing;
    [ObservableProperty] private bool pushNotificationsEnabled;
    [ObservableProperty] private bool emergencySirenEnabled;
    [ObservableProperty] private bool hapticFeedbackEnabled;
    [ObservableProperty] private bool detailedOfflineMapEnabled = true;
    [ObservableProperty] private bool navigationVoiceEnabled;
    [ObservableProperty] private bool canClearCache;
    [ObservableProperty] private string cacheSizeText = "Unavailable";
    [ObservableProperty] private string statusMessage = string.Empty;
    public SystemInformationViewModel DeviceInfo { get; } = new();
    public async Task InitializeSettingsAsync()
    {
        initializing = true;
        try
        {
            PushNotificationsEnabled = settings.Get(AppSetting.ForegroundAdvisories);
            EmergencySirenEnabled = settings.Get(AppSetting.EmergencySiren);
            HapticFeedbackEnabled = settings.Get(AppSetting.HapticFeedback);
            DetailedOfflineMapEnabled = settings.Get(AppSetting.DetailedOfflineMap);
            NavigationVoiceEnabled = settings.Get(AppSetting.NavigationVoice);
            StatusMessage = string.Empty;
        }
        catch { StatusMessage = "Saved settings could not be loaded. Changes may not be available until device storage is working."; }
        finally { initializing = false; }
        RefreshCacheSize();
        await DeviceInfo.LoadPermissionsAsync();
    }
    partial void OnPushNotificationsEnabledChanged(bool value) => Save(AppSetting.ForegroundAdvisories, value);
    partial void OnEmergencySirenEnabledChanged(bool value) => Save(AppSetting.EmergencySiren, value);
    partial void OnHapticFeedbackEnabledChanged(bool value) => Save(AppSetting.HapticFeedback, value);
    partial void OnDetailedOfflineMapEnabledChanged(bool value) => Save(AppSetting.DetailedOfflineMap, value);
    partial void OnNavigationVoiceEnabledChanged(bool value) => Save(AppSetting.NavigationVoice, value);
    private void Save(AppSetting setting, bool value)
    {
        if (initializing) return;
        try { settings.Set(setting, value); StatusMessage = "Setting saved on this device."; }
        catch
        {
            initializing = true;
            bool previous = AppPreferences.IsEnabled(setting);
            if (setting == AppSetting.ForegroundAdvisories) PushNotificationsEnabled = previous;
            else if (setting == AppSetting.EmergencySiren) EmergencySirenEnabled = previous;
            else if (setting == AppSetting.HapticFeedback) HapticFeedbackEnabled = previous;
            else if (setting == AppSetting.DetailedOfflineMap) DetailedOfflineMapEnabled = previous;
            else if (setting == AppSetting.NavigationVoice) NavigationVoiceEnabled = previous;
            initializing = false;
            StatusMessage = "The setting was not saved. Please retry.";
        }
    }
    private void RefreshCacheSize()
    {
        try
        {
            var cache = maps.Inspect();
            CacheSizeText = Size(cache.Bytes) + (cache.UnavailableFiles > 0 ? " (some files unavailable)" : string.Empty);
            CanClearCache = cache.Bytes > 0;
        }
        catch { CacheSizeText = "Unavailable"; CanClearCache = false; }
    }
    private static string Size(long bytes) => bytes < 1024 * 1024 ? $"{bytes / 1024.0:F1} KB" : $"{bytes / (1024.0 * 1024):F2} MB";
    [RelayCommand]
    private async Task ClearCache()
    {
        if (!CanClearCache || Shell.Current is not { } page) return;
        bool confirm = await page.DisplayAlert("Clear map cache", "Remove replaceable map-cache copies? Routing data, account details, photos, messages and pending files are kept.", "Clear map cache", "Cancel");
        if (!confirm) return;
        try
        {
            var result = await OfflineMapRuntime.ClearAsync();
            RefreshCacheSize();
            StatusMessage = result.Remaining.Bytes == 0 && result.Remaining.UnavailableFiles == 0
                ? $"Removed {Size(result.DeletedBytes)} of replaceable map cache. Copies return on the next enabled map use."
                : $"Removed {Size(result.DeletedBytes)}. Some map-cache files remain or are unavailable; retry later.";
        }
        catch { RefreshCacheSize(); StatusMessage = "Map cache could not be cleared. Please retry."; }
    }
    [RelayCommand] private void OpenDeviceSettings() => DeviceInfo.ManagePermissionsCommand.Execute(null);
    [RelayCommand] private async Task Back() { if (Shell.Current != null) await Shell.Current.GoToAsync(".."); }
}
