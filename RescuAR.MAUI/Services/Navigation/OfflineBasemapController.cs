using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Microsoft.Maui.Networking;
using NetTopologySuite.Geometries;
using Map = Mapsui.Map;
using Color = Mapsui.Styles.Color;

namespace RescuAR.MAUI.Services.Navigation;

/// <summary>Page-owned cartography only; navigation and Circle presence keep their existing owners.</summary>
public sealed class OfflineBasemapController
{
    private readonly OfflineOverlaySession<Map, ILayer> session;
    private readonly OfflineMapLayerController detail;
    private readonly Action<string> status;
    private TileLayer? online;
    private string detailStatus = "Loading offline map…";

    public OfflineBasemapController(Action<string> status)
    {
        this.status = status;
        // Basic roads, optional detail, online tiles, then the page's route/member overlays.
        detail = new(message => { detailStatus = message; UpdateStatus(); }, insertionIndex: 1);
        session = new(LoadAsync,
            (map, layers) => { map.Layers.Insert(0, layers); map.Refresh(); },
            (map, layers) => { foreach (var layer in layers) map.Layers.Remove(layer); map.Refresh(); },
            layer => { layer.Enabled = false; layer.Dispose(); }, Changed);
    }

    public Task ActivateAsync(Map map)
    {
        // GPS and route redraws must not restart map preparation or replace the viewport.
        if (ReferenceEquals(session.Map, map)) return Task.CompletedTask;
        Deactivate();
        Connectivity.Current.ConnectivityChanged += ConnectivityChanged;
        return session.ActivateAsync(map, true);
    }

    public void Deactivate()
    {
        Connectivity.Current.ConnectivityChanged -= ConnectivityChanged;
        detail.Deactivate();
        session.Deactivate();
        online = null;
    }

    private static async Task<IReadOnlyList<ILayer>> LoadAsync(CancellationToken token)
    {
        // Shared data is immutable; every map owns and disposes its own projected features.
        var roads = await CameraOfflineMapDataService.LoadAsync(token);
        var features = await Task.Run(() => roads.Select(road => new GeometryFeature(new LineString(road.Select(coordinate =>
        {
            var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(coordinate.Longitude, coordinate.Latitude);
            return new Coordinate(x, y);
        }).ToArray()))).ToArray(), token);
        token.ThrowIfCancellationRequested();
        var basic = new MemoryLayer
        {
            Name = "Offline basic roads", Features = features,
            Style = new VectorStyle { Line = new Pen(Color.FromString("#94A3B8"), 1.5) },
            Attribution = new() { Text = "© OpenStreetMap contributors", Url = "https://www.openstreetmap.org/copyright" }
        };
        try
        {
            var tiles = MapBaseLayer.CreateOnlineLayer("Online basemap");
            tiles.Enabled = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
            return [basic, tiles];
        }
        catch { basic.Dispose(); throw; }
    }

    private void Changed(OfflineDisplayState state)
    {
        if (state == OfflineDisplayState.Ready && session.Map is { } map)
        {
            online = map.Layers.OfType<TileLayer>().First(layer => layer.Name == "Online basemap");
            UpdateConnectivity();
            _ = detail.ActivateAsync(map);
        }
        else if (state == OfflineDisplayState.Loading) status("Loading offline roads…");
        else if (state == OfflineDisplayState.Unavailable) status("Map unavailable · reopen to retry.");
    }

    private void ConnectivityChanged(object? sender, ConnectivityChangedEventArgs args) =>
        MainThread.BeginInvokeOnMainThread(UpdateConnectivity);

    private void UpdateConnectivity()
    {
        if (session.State != OfflineDisplayState.Ready || session.Map is not { } map || online is null) return;
        online.Enabled = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        map.Refresh();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (session.State != OfflineDisplayState.Ready) return;
        status(online?.Enabled == true ? "Online map · offline roads and Marikina detail available." : detailStatus);
    }
}
