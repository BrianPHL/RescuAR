using Microsoft.Maui.Storage;
using Newtonsoft.Json;

namespace RescuAR.App.Services.Storage;

/// <summary>The shared public evacuation directory and its hotline cache.</summary>
public static class CacheService
{
    private const string CacheFileName = "evacuation_centers_cache.json";
    private const string LastUpdatedPrefKey = "EvacuationCenters_LastUpdatedUtc";
    private const string HasCachePrefKey = "EvacuationCenters_HasSavedCache";
    private static readonly object sync = new();
    private static string GetCacheFilePath() => Path.Combine(FileSystem.AppDataDirectory, CacheFileName);

    public static bool HasSavedEvacuationData() => LoadEvacuationCenters() is not null;

    public static EvacuationCenterCacheEnvelope? LoadEvacuationCenters()
    {
        lock (sync)
        {
            try
            {
                string path = GetCacheFilePath();
                if (!File.Exists(path)) return null;
                var envelope = JsonConvert.DeserializeObject<EvacuationCenterCacheEnvelope>(File.ReadAllText(path));
                if (envelope is not null) { envelope.SourceKey ??= ""; envelope.Hotlines ??= new(); }
                return envelope?.Centers is not null && (envelope.Centers.Count > 0 || envelope.DirectoryConfirmed) ? envelope : null;
            }
            catch { return null; }
        }
    }

    // Compatibility entry point: legacy callers cannot accidentally confirm an empty directory.
    public static void SaveEvacuationCenters(IEnumerable<CachedEvacuationCenterItem> centers,
        IEnumerable<CachedEmergencyHotlineItem>? hotlines, DateTimeOffset timestamp)
    {
        if (centers is null) return;
        var list = centers.ToList();
        if (list.Count > 0) TrySaveEvacuationCenters(list, hotlines, timestamp, "");
    }

    public static bool TrySaveEvacuationCenters(IEnumerable<CachedEvacuationCenterItem> centers,
        IEnumerable<CachedEmergencyHotlineItem>? hotlines, DateTimeOffset timestamp, string sourceKey)
    {
        lock (sync)
        {
            return Write(new()
            {
                Centers = centers.ToList(), Hotlines = hotlines?.ToList() ?? new(),
                LastUpdatedUtc = timestamp, SourceKey = sourceKey, DirectoryConfirmed = true
            });
        }
    }

    public static void UpdateHotlines(IEnumerable<CachedEmergencyHotlineItem> hotlines, string sourceKey)
    {
        lock (sync)
        {
            var saved = LoadEvacuationCenters();
            if (saved is null || (saved.SourceKey.Length > 0 && saved.SourceKey != sourceKey)) return;
            saved.Hotlines = hotlines.ToList();
            Write(saved);
        }
    }

    // Keep the previous file intact if serialization or replacement fails. Never truncate it as a fallback.
    private static bool Write(EvacuationCenterCacheEnvelope envelope)
    {
        string path = GetCacheFilePath(), temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonConvert.SerializeObject(envelope, Formatting.Indented));
            File.Move(temp, path, overwrite: true);
            Preferences.Default.Set(LastUpdatedPrefKey, envelope.LastUpdatedUtc.ToUnixTimeSeconds());
            Preferences.Default.Set(HasCachePrefKey, true);
            return true;
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            return false;
        }
    }

    public static DateTimeOffset? GetLastUpdatedTime()
    {
        var saved = LoadEvacuationCenters();
        return saved is not null && saved.LastUpdatedUtc > DateTimeOffset.UnixEpoch ? saved.LastUpdatedUtc : null;
    }
}
