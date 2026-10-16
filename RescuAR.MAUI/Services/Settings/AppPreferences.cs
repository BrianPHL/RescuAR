namespace RescuAR.MAUI.Services.Settings;

public static class AppPreferences
{
    public static AppSettingsStore Current { get; } = new(new MauiPreferences());

    private sealed class MauiPreferences : IBooleanPreferences
    {
        public bool Get(string key, bool defaultValue) => Preferences.Default.Get(key, defaultValue);
        public void Set(string key, bool value) => Preferences.Default.Set(key, value);
    }

    public static void VibrateForAdvisory()
    {
        if (!Current.Get(AppSetting.HapticFeedback)) return;
        try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(300)); }
        catch (Exception) { /* Hardware feedback is optional; the written advisory remains visible. */ }
    }

    public static void TurnFeedback()
    {
        if (!Current.Get(AppSetting.HapticFeedback)) return;
        try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); }
        catch (Exception) { /* Some devices do not support haptics. */ }
    }
}
