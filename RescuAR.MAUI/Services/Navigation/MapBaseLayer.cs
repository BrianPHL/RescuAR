using BruTile;
using BruTile.Cache;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui.Tiling.Layers;
using RescuAR.MAUI.Services.Settings;

namespace RescuAR.MAUI.Services.Navigation;

public static class MapBaseLayer
{
    // Keep a stable, truthful application identifier; do not use a library default or an unconfirmed contact address.
    public const string UserAgent = "RescuAR-Mobile/1.0 (Marikina Emergency System)";

    public static TileLayer CreateOnlineLayer(string name)
    {
        var cacheRoot = new DerivedMapCache(FileSystem.CacheDirectory).Root;
        var source = new HttpTileSource(new GlobalSphericalMercator(),
            "https://tile.openstreetmap.org/{z}/{x}/{y}.png", name: "OpenStreetMap",
            persistentCache: new FileCache(Path.Combine(cacheRoot, "openstreetmap"), "png", TimeSpan.FromDays(7)),
            attribution: new Attribution("© OpenStreetMap contributors", "https://www.openstreetmap.org/copyright"),
            configureHttpRequestMessage: request => request.Headers.TryAddWithoutValidation("User-Agent", UserAgent));
        return new TileLayer(source) { Name = name };
    }
}
