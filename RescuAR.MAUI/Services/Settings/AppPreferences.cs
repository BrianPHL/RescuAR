namespace RescuAR.MAUI.Services.Settings;

public static class AppPreferences
{
    public static AppSettingsStore Current { get; } = new(new MauiPreferences());
    private static readonly Dictionary<AppSetting, bool> lastRead = new();
    private static readonly object sync = new();
    private sealed class MauiPreferences : IBooleanPreferences
    {
        public bool ContainsKey(string key) => Preferences.Default.ContainsKey(key);
        public bool Get(string key, bool defaultValue) => Preferences.Default.Get(key, defaultValue);
        public void Set(string key, bool value) => Preferences.Default.Set(key, value);
    }
    public static bool IsEnabled(AppSetting setting)
    {
        lock (sync)
        {
            try { bool value = Current.Get(setting); lastRead[setting] = value; return value; }
            catch { return lastRead.TryGetValue(setting, out bool value) ? value : setting != AppSetting.NavigationVoice; }
        }
    }
    public static void VibrateForAdvisory()
    {
        if (!IsEnabled(AppSetting.HapticFeedback)) return;
        try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(300)); }
        catch { }
    }
    public static void ObserveWeak<T>(T owner, Action<T, AppSetting> action) where T : class
    {
        var reference = new WeakReference<T>(owner);
        Action<AppSetting>? listener = null;
        listener = setting =>
        {
            if (reference.TryGetTarget(out var current)) action(current, setting);
            else Current.Changed -= listener;
        };
        Current.Changed += listener;
    }
}
