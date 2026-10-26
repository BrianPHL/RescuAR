using BruTile;
using BruTile.Predefined;

namespace RescuAR.MAUI.Services.Navigation;

/// <summary>Mapsui 5 fetches packaged bytes only from an ILocalTileSource.</summary>
public sealed class PackagedMapTileSource(
    Func<int, int, int, CancellationToken, Task<byte[]?>> readTile,
    Func<bool> canRead, CancellationToken owner) : ILocalTileSource
{
    public string Name => "RescuAR packaged offline map";
    public ITileSchema Schema { get; } = new GlobalSphericalMercator(13, 18);
    public Attribution Attribution { get; } = new("© OpenStreetMap contributors · Made with Maperitive", "https://www.openstreetmap.org/copyright");

    public async Task<byte[]?> GetTileAsync(TileInfo tileInfo)
    {
        if (owner.IsCancellationRequested || !canRead()) return null;
        try { return await readTile(tileInfo.Index.Level, tileInfo.Index.Col, tileInfo.Index.Row, owner).ConfigureAwait(false); }
        catch (OperationCanceledException) when (owner.IsCancellationRequested) { return null; }
    }
}
