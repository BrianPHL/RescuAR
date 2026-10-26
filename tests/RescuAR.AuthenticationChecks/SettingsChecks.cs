using RescuAR.MAUI.Services.Settings;

internal static class SettingsChecks
{
    public static void Run(Action<bool, string> check)
    {
        var preferences = new FakePreferences();
        var settings = new AppSettingsStore(preferences);
        check(settings.Get(AppSetting.ForegroundAdvisories) && settings.Get(AppSetting.EmergencySiren) && settings.Get(AppSetting.HapticFeedback),
            "optional advisory controls retain their enabled defaults");
        preferences.Values["PushNotificationsEnabled"] = false;
        check(!settings.Get(AppSetting.ForegroundAdvisories) && !preferences.Values["ForegroundAdvisoriesEnabled"],
            "the legacy popup preference migrates without losing a disabled choice");
        preferences.Values["ForegroundAdvisoriesEnabled"] = true;
        check(settings.Get(AppSetting.ForegroundAdvisories), "an existing current popup preference takes precedence over the legacy key");
        var changes = new List<AppSetting>(); settings.Changed += changes.Add;
        settings.Set(AppSetting.ForegroundAdvisories, false);
        check(changes.SequenceEqual(new[] { AppSetting.ForegroundAdvisories }) && settings.Get(AppSetting.EmergencySiren),
            "popup changes notify consumers without muting the independent sound preference");
        settings.Set(AppSetting.ForegroundAdvisories, false);
        check(changes.Count == 1, "an unchanged setting emits no duplicate change notification");
        settings.Set(AppSetting.HapticFeedback, false);
        check(!new AppSettingsStore(preferences).Get(AppSetting.HapticFeedback), "haptic preferences persist across settings-store instances");
        preferences.FailWrites = true;
        bool failed = false; int previous = changes.Count;
        try { settings.Set(AppSetting.EmergencySiren, false); } catch (IOException) { failed = true; }
        check(failed && changes.Count == previous && settings.Get(AppSetting.EmergencySiren),
            "a failed setting write cannot announce a successful sound change");
        preferences.FailWrites = false;
        check(!preferences.Values.ContainsKey("DetailedOfflineMapEnabled") && !preferences.Values.ContainsKey("NavigationVoiceEnabled"),
            "editing approved controls does not change deferred map or voice preferences");

        int starts = 0, stops = 0; bool playable = true;
        var completions = new List<Action>();
        var alarm = new AlarmPlaybackController(completed => { starts++; completions.Add(completed); return playable; }, () => stops++);
        alarm.PlayAutomatic(false);
        check(starts == 0 && stops == 0 && !alarm.IsActive, "disabled automatic sound never starts or interrupts playback");
        alarm.PlayManual();
        check(starts == 1 && alarm.IsActive && !alarm.IsAutomatic, "the manual siren can start independently of the automatic preference");
        previous = stops; alarm.StopAutomatic();
        check(stops == previous && alarm.IsActive, "muting automatic sound cannot stop an active manual siren");
        alarm.PlayAutomatic(false);
        check(starts == 1 && stops == previous, "a muted incoming advisory cannot preempt the manual siren");
        alarm.PlayAutomatic(true);
        check(starts == 2 && alarm.IsActive && alarm.IsAutomatic, "an enabled advisory owns its automatic playback");
        previous = stops; alarm.StopAutomatic();
        check(stops == previous + 1 && !alarm.IsActive, "turning automatic sound off stops its currently active alarm");
        alarm.PlayAutomatic(true); var old = completions[^1];
        alarm.PlayManual(); old();
        check(alarm.IsActive && !alarm.IsAutomatic, "an old automatic completion cannot clear ownership of newer manual playback");
        completions[^1]();
        check(!alarm.IsActive, "the current playback completion releases its own state");
        playable = false; alarm.PlayAutomatic(true);
        check(!alarm.IsActive && !alarm.IsAutomatic, "unavailable sound hardware cannot be reported as active playback");
        playable = true; alarm.PlayManual(); alarm.Stop();
        check(!alarm.IsActive, "the existing explicit stop action still stops manual playback");

        string repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string fixture = Path.GetFullPath(Path.Combine(repository, "artifacts/settings-unit-fixtures", Guid.NewGuid().ToString("N")));
        if (!Directory.Exists(Path.Combine(repository, "RescuAR.MAUI")) || !fixture.StartsWith(Path.Combine(repository, "artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The isolated cache-test fixture must stay inside this worktree's artifacts directory.");
        string cacheBase = Path.Combine(fixture, "cache");
        var maps = new DerivedMapCache(cacheBase);
        check(maps.Inspect().Bytes == 0 && !Directory.Exists(maps.Root), "an unused map-cache area reports zero without creating or sweeping general cache data");
        Directory.CreateDirectory(Path.Combine(maps.Root, "nested"));
        Directory.CreateDirectory(Path.Combine(cacheBase, "rescuar-map-cache-other"));
        Directory.CreateDirectory(Path.Combine(fixture, "account"));
        var protectedFiles = new[]
        {
            Path.Combine(cacheBase, "media.jpg"), Path.Combine(cacheBase, "routing.mbtiles"),
            Path.Combine(cacheBase, "rescuar-map-cache-other", "pending.json"), Path.Combine(fixture, "account", "profile.json"),
            Path.Combine(fixture, "account", "messages.json")
        };
        foreach (string path in protectedFiles) File.WriteAllText(path, "preserve");
        File.WriteAllBytes(Path.Combine(maps.Root, "tiles.bin"), new byte[7]);
        File.WriteAllBytes(Path.Combine(maps.Root, "nested", "display.sqlite"), new byte[11]);
        check(maps.Inspect().Bytes == 18, "cache size counts only the dedicated replaceable map copies");
        var cleared = maps.Clear();
        check(cleared.DeletedBytes == 18 && cleared.Remaining.Bytes == 0, "scoped clearing removes exactly the replaceable map files");
        check(protectedFiles.All(path => File.Exists(path) && File.ReadAllText(path) == "preserve"),
            "cache clearing preserves routing, media, profile, messages and similarly prefixed sibling pending data");
        check(Directory.Exists(maps.Root) && Directory.Exists(Path.Combine(maps.Root, "nested")),
            "map clearing does not recursively delete or move directories");
        check(maps.Clear().DeletedBytes == 0, "repeated scoped clearing is harmless");
        if (OperatingSystem.IsWindows())
        {
            string locked = Path.Combine(maps.Root, "locked.bin");
            File.WriteAllBytes(locked, new byte[8]);
            using (var held = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var partial = maps.Clear();
                check(partial.DeletedBytes == 0 && partial.Remaining.Bytes == 8 && File.Exists(locked),
                    "an in-use map file remains counted instead of producing false zero-cache success");
            }
            check(maps.Clear().DeletedBytes == 8, "a later scoped retry can remove a formerly in-use map file");
        }
        else Console.WriteLine("SKIP: Windows in-use-file deletion checks do not apply on this host.");
        bool invalid = false;
        try { _ = new DerivedMapCache(" "); } catch (ArgumentException) { invalid = true; }
        check(invalid, "cache deletion cannot be configured with an empty base path");
    }

    private sealed class FakePreferences : IBooleanPreferences
    {
        public Dictionary<string, bool> Values { get; } = new();
        public bool FailWrites;
        public bool ContainsKey(string key) => Values.ContainsKey(key);
        public bool Get(string key, bool fallback) => Values.GetValueOrDefault(key, fallback);
        public void Set(string key, bool value) { if (FailWrites) throw new IOException("test write failure"); Values[key] = value; }
    }
}
