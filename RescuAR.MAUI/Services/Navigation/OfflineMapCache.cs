using RescuAR.MAUI.Services.Settings;

namespace RescuAR.MAUI.Services.Navigation;

/// <summary>The only open archive/clear lock; private media and routing data are outside its root.</summary>
public sealed class OfflineMapCache(DerivedMapCache copies, Func<Task<Stream>> openPackage,
    string expectedHash = OfflineMapInstaller.PackageHash, long expectedSize = OfflineMapInstaller.PackageSize) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private OfflineTileArchive? archive;
    private bool disposed;
    private async Task<OfflineTileArchive> Open(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (archive is not null) return archive;
        string path = await OfflineMapInstaller.InstallAsync(copies.Root, openPackage, token, expectedHash, expectedSize).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        archive = new(File.OpenRead(path));
        return archive;
    }
    public async Task<OfflineMapManifest> PrepareAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { return (await Open(token).ConfigureAwait(false)).Manifest; }
        finally { gate.Release(); }
    }
    public async Task<byte[]?> ReadTileAsync(int zoom, int column, int row, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); await gate.WaitAsync(token).ConfigureAwait(false);
        try { var current = await Open(token).ConfigureAwait(false); token.ThrowIfCancellationRequested(); return current.ReadTile(zoom, column, row); }
        finally { gate.Release(); }
    }
    public async Task<bool> CoversAsync(double longitude, double latitude, int zoom, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); await gate.WaitAsync(token).ConfigureAwait(false);
        try { var current = await Open(token).ConfigureAwait(false); token.ThrowIfCancellationRequested(); return current.Covers(longitude, latitude, zoom); }
        finally { gate.Release(); }
    }
    // Caller invalidates visible owners before this operation, so stale tile requests cannot reinstall during clearing.
    public async Task<MapCacheClearResult> ClearAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            archive?.Dispose(); archive = null;
            return await Task.Run(copies.Clear, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public void Dispose()
    {
        gate.Wait();
        try { if (!disposed) { disposed = true; archive?.Dispose(); archive = null; } }
        finally { gate.Release(); }
    }
}
