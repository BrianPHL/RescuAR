using RescuAR.App.Services.Storage;

internal static class DirectoryCacheChecks
{
    public static void Run(Action<bool, string> check)
    {
        string repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string parent = Path.Combine(repository, "artifacts", "directory-cache-unit-fixtures");
        string fixture = Path.GetFullPath(Path.Combine(parent, Guid.NewGuid().ToString("N")));
        if (!fixture.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Fixture must stay inside this repository's artifacts directory.");
        System.IO.Directory.CreateDirectory(fixture);
        Microsoft.Maui.Storage.FileSystem.AppDataDirectory = fixture;
        var at = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        var centers = new[] { new CachedEvacuationCenterItem { Id = "a-guid-like-id", Name = "A center", Latitude = 14.65, Longitude = 121.1 } };
        check(CacheService.TrySaveEvacuationCenters(centers, new[] { new CachedEmergencyHotlineItem { Name = "Saved hotline" } }, at, "server"), "production cache writes a confirmed directory snapshot");
        var saved = CacheService.LoadEvacuationCenters();
        check(saved?.SourceKey == "server" && saved.Centers.Single().Id == "a-guid-like-id" && saved.LastUpdatedUtc == at, "production cache round-trips source, string identity and timestamp");
        check(CacheService.TrySaveEvacuationCenters(Array.Empty<CachedEvacuationCenterItem>(), saved!.Hotlines, at, "server"), "production cache accepts an explicitly confirmed empty result");
        saved = CacheService.LoadEvacuationCenters();
        check(saved is { DirectoryConfirmed: true } && saved.Centers.Count == 0 && saved.Hotlines.Single().Name == "Saved hotline", "empty directory confirmation reloads without losing hotline data");
        CacheService.UpdateHotlines(new[] { new CachedEmergencyHotlineItem { Name = "Updated hotline" } }, "server");
        check(CacheService.LoadEvacuationCenters() is { Centers.Count: 0, DirectoryConfirmed: true } envelope && envelope.Hotlines.Single().Name == "Updated hotline", "hotline refresh cannot resurrect removed centers or remove empty confirmation");
        CacheService.UpdateHotlines(new[] { new CachedEmergencyHotlineItem { Name = "Wrong server" } }, "other");
        check(CacheService.LoadEvacuationCenters()!.Hotlines.Single().Name == "Updated hotline", "another server's hotline update cannot overwrite the saved directory");
        string path = Path.Combine(fixture, "evacuation_centers_cache.json");
        File.WriteAllText(path, "{\"Centers\":[],\"DirectoryConfirmed\":false}");
        check(CacheService.LoadEvacuationCenters() is null, "legacy empty files without server confirmation do not suppress the historical fallback");
        File.WriteAllText(path, "{\"Centers\":[],\"DirectoryConfirmed\":true,\"SourceKey\":null,\"Hotlines\":null}");
        check(CacheService.LoadEvacuationCenters() is { SourceKey: "", Hotlines.Count: 0 }, "optional null metadata cannot break legacy cache loading");
    }
}

namespace Microsoft.Maui.Storage
{
    // Only storage bindings are substituted; the production serializer/file replacement runs above.
    internal static class FileSystem { public static string AppDataDirectory { get; set; } = ""; }
    internal static class Preferences { public static StubPreferences Default { get; } = new(); }
    internal sealed class StubPreferences { public void Set<T>(string key, T value) { } }
}
