using System.Collections.Concurrent;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using RescuAR.Navigation.Data;
using RescuAR.MAUI.Services.Settings;
using Color = Mapsui.Styles.Color;
using Brush = Mapsui.Styles.Brush;

namespace RescuAR.MAUI.Services.Navigation;

/// <summary>Static cartography only. Existing routing, hazard and people overlays retain their owners.</summary>
public static class DetailedOfflineMapService
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<Geometry[]>>> templates = new();

    private static async Task<Geometry[]> ReadGeometryAsync(Func<Task<Stream>> open)
    {
        using var stream = await open().ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        string json = await reader.ReadToEndAsync().ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var collection = new GeoJsonReader().Read<NetTopologySuite.Features.FeatureCollection>(json);
            return collection.Where(feature => feature.Geometry is not null && !feature.Geometry.IsEmpty).Select(feature =>
            {
                var geometry = feature.Geometry.Copy(); geometry.Apply(new Projector()); return geometry;
            }).ToArray();
        }).ConfigureAwait(false);
    }

    private static async Task<MemoryLayer> LayerAsync(string key, string name, Func<Task<Stream>> open, IStyle style, CancellationToken token)
    {
        // Cancelling one page does not cancel another page's shared, immutable geometry template.
        var lazy = templates.GetOrAdd(key, _ => new(() => ReadGeometryAsync(open)));
        Geometry[] geometries;
        try { geometries = await lazy.Value.WaitAsync(token).ConfigureAwait(false); }
        catch
        {
            if (lazy.IsValueCreated && lazy.Value.IsFaulted)
                ((ICollection<KeyValuePair<string, Lazy<Task<Geometry[]>>>>)templates).Remove(new(key, lazy));
            throw;
        }
        token.ThrowIfCancellationRequested();
        return new MemoryLayer { Name = name, Features = geometries.Select(geometry => new GeometryFeature(geometry.Copy())).ToArray(), Style = style };
    }

    public static async Task<IReadOnlyList<ILayer>> CreateLayersAsync(CancellationToken token)
    {
        if (OfflineMapRuntime.IsClearing) throw new OperationCanceledException("Map copies are being cleared.", token);
        await OfflineMapRuntime.Cache.PrepareAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        List<ILayer> layers = [new TileLayer(new PackagedMapTileSource(OfflineMapRuntime.Cache.ReadTileAsync,
            () => !OfflineMapRuntime.IsClearing && AppPreferences.IsEnabled(AppSetting.DetailedOfflineMap), token)) { Name = "Packaged detail (zoom 13–18)" }];
        try
        {
            foreach (string context in new[] { "WATER", "PARKS", "BUILDINGS" })
            {
                var color = context == "WATER" ? Color.FromString("#90CBE8") : context == "PARKS" ? Color.FromString("#B4D9B7") : Color.FromString("#D4D4CE");
                layers.Add(await LayerAsync(context, context + " (static context)", () => FileSystem.OpenAppPackageFileAsync(context + ".geojson"),
                    new VectorStyle { Fill = new Brush(color), Line = new Pen(color, 0.5) }, token).ConfigureAwait(false));
            }
            token.ThrowIfCancellationRequested();
            return layers;
        }
        catch { foreach (var layer in layers) layer.Dispose(); throw; }
    }

    private sealed class Projector : ICoordinateFilter
    {
        public void Filter(Coordinate coordinate)
        {
            var projected = Mapsui.Projections.SphericalMercator.FromLonLat(coordinate.X, coordinate.Y);
            coordinate.X = projected.x; coordinate.Y = projected.y;
        }
    }

}
