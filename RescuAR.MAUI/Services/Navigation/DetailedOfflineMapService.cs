using BruTile;
using BruTile.Predefined;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using NetTopologySuite.IO;
using RescuAR.MAUI.Services.Settings;
using Color = Mapsui.Styles.Color;
using Brush = Mapsui.Styles.Brush;

namespace RescuAR.MAUI.Services.Navigation;

/// <summary>Static cartographic context shared by the camera and map; not hazard or access evidence.</summary>
public static class DetailedOfflineMapService
{
    private static readonly SemaphoreSlim packageGate = new(1, 1);
    private static OfflineTileArchive? package;
    private static readonly string[] contexts = ["WATER", "PARKS", "BUILDINGS"];
    private static string DirectoryPath => Path.Combine(FileSystem.AppDataDirectory, "offline-map-v1");
    public static long LocalCopySize => File.Exists(Path.Combine(DirectoryPath, OfflineMapInstaller.PackageName))
        ? new FileInfo(Path.Combine(DirectoryPath, OfflineMapInstaller.PackageName)).Length : 0;

    public static async Task PrepareAsync(CancellationToken token = default)
    {
        await packageGate.WaitAsync(token).ConfigureAwait(false);
        try { await EnsurePackageAsync(token).ConfigureAwait(false); }
        finally { packageGate.Release(); }
    }

    public static async Task<IReadOnlyList<ILayer>> CreateLayersAsync(CancellationToken token = default)
    {
        await PrepareAsync(token);
        List<ILayer> layers = [new TileLayer(new LocalTileSource()) { Name = "Offline detailed map (zoom 13–18)" }];
        try
        {
            foreach (string context in contexts)
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync(context + ".geojson");
                using var reader = new StreamReader(stream);
                string json = await reader.ReadToEndAsync(token);
                var features = await Task.Run(() =>
                {
                    var collection = new GeoJsonReader().Read<NetTopologySuite.Features.FeatureCollection>(json);
                    return collection.Where(f => f.Geometry is not null && !f.Geometry.IsEmpty).Select(f =>
                    {
                        var geometry = f.Geometry.Copy();
                        geometry.Apply(new OfflineMapProjector());
                        return new GeometryFeature(geometry);
                    }).ToArray();
                }, token);
                var color = context == "WATER" ? Color.FromString("#90CBE8") :
                    context == "PARKS" ? Color.FromString("#B4D9B7") : Color.FromString("#D4D4CE");
                layers.Add(new MemoryLayer
                {
                    Name = context + " (static context)", Features = features,
                    Style = new VectorStyle { Fill = new Brush(color), Line = new Pen(color, 0.5) }
                });
            }
            return layers;
        }
        catch { foreach (var layer in layers) layer.Dispose(); throw; }
    }

    private static async Task EnsurePackageAsync(CancellationToken token)
    {
        if (package is not null) return;
        string path = await OfflineMapInstaller.InstallAsync(DirectoryPath,
            () => FileSystem.OpenAppPackageFileAsync(OfflineMapInstaller.PackageName), token);
        package = new OfflineTileArchive(File.OpenRead(path));
    }

    public static async Task RemoveLocalCopyAsync()
    {
        await packageGate.WaitAsync();
        try
        {
            package?.Dispose(); package = null;
            string path = Path.Combine(DirectoryPath, OfflineMapInstaller.PackageName);
            if (File.Exists(path)) File.Delete(path);
        }
        finally { packageGate.Release(); }
    }

    private sealed class LocalTileSource : ITileSource
    {
        public string Name => "RescuAR packaged offline map";
        public ITileSchema Schema { get; } = new GlobalSphericalMercator(13, 18);
        public Attribution Attribution { get; } = new("Maperitive export • static map context");
        public async Task<byte[]?> GetTileAsync(TileInfo tileInfo)
        {
            if (!AppPreferences.Current.Get(AppSetting.DetailedOfflineMap)) return null;
            await packageGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!AppPreferences.Current.Get(AppSetting.DetailedOfflineMap)) return null;
                await EnsurePackageAsync(CancellationToken.None).ConfigureAwait(false);
                return package!.ReadTile(tileInfo.Index.Level, tileInfo.Index.Col, tileInfo.Index.Row);
            }
            finally { packageGate.Release(); }
        }
    }
}
