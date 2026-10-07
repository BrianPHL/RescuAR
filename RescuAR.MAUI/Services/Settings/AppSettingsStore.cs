namespace RescuAR.MAUI.Services.Settings;

public enum AppSetting { ForegroundAdvisories, EmergencySiren, HapticFeedback, DetailedOfflineMap, NavigationVoice }

public interface IBooleanPreferences
{
    bool Get(string key, bool defaultValue);
    void Set(string key, bool value);
}

/// <summary>Device preferences shared by settings and their behavior consumers.</summary>
public sealed class AppSettingsStore(IBooleanPreferences preferences)
{
    public event Action<AppSetting>? Changed;

    public bool Get(AppSetting setting) => preferences.Get(Key(setting), setting != AppSetting.NavigationVoice);

    public void Set(AppSetting setting, bool value)
    {
        if (Get(setting) == value) return;
        preferences.Set(Key(setting), value);
        Changed?.Invoke(setting);
    }

    private static string Key(AppSetting setting) => setting switch
    {
        AppSetting.ForegroundAdvisories => "ForegroundAdvisoriesEnabled",
        AppSetting.EmergencySiren => "EmergencySirenEnabled",
        AppSetting.HapticFeedback => "HapticFeedbackEnabled",
        AppSetting.DetailedOfflineMap => "DetailedOfflineMapEnabled",
        AppSetting.NavigationVoice => "NavigationVoiceEnabled",
        _ => throw new ArgumentOutOfRangeException(nameof(setting))
    };
}
