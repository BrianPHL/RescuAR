using RescuAR.MAUI.Services.Settings;

namespace RescuAR.MAUI.Services.Navigation;

public static class OfflineMapRuntime
{
    private static readonly Lazy<OfflineMapCache> cache = new(() => new(new DerivedMapCache(FileSystem.CacheDirectory),
        () => FileSystem.OpenAppPackageFileAsync(OfflineMapInstaller.PackageName)));
    private static readonly SemaphoreSlim clearing = new(1, 1);
    private static int isClearing;
    public static OfflineMapCache Cache => cache.Value;
    public static bool IsClearing => Volatile.Read(ref isClearing) != 0;
    public static event Action? Invalidated;

    private static void InvalidateOwners()
    {
        if (Invalidated is not { } listeners) return;
        foreach (Action listener in listeners.GetInvocationList()) listener();
    }

    public static async Task<MapCacheClearResult> ClearAsync()
    {
        await clearing.WaitAsync();
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => { Volatile.Write(ref isClearing, 1); InvalidateOwners(); });
            return await Cache.ClearAsync();
        }
        finally
        {
            // A view opened during clearing must also remain detached until its next enabled activation.
            await MainThread.InvokeOnMainThreadAsync(() => { Volatile.Write(ref isClearing, 0); InvalidateOwners(); });
            clearing.Release();
        }
    }
}
