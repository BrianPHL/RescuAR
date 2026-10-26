using Map = Mapsui.Map;
using Mapsui.Layers;
using RescuAR.MAUI.Services.Settings;

namespace RescuAR.MAUI.Services.Navigation;

/// <summary>Page-scoped detail ownership. All entry points run on the page dispatcher.</summary>
public sealed class OfflineMapLayerController
{
    private readonly OfflineOverlaySession<Map, ILayer> session;
    private readonly Action<string> status;
    private bool subscribed;
    private long coverageRequest;

    public OfflineMapLayerController(Action<string> status)
    {
        this.status = status;
        session = new(DetailedOfflineMapService.CreateLayersAsync,
            (map, layers) => { map.Layers.Insert(Math.Min(1, map.Layers.Count), layers); map.Refresh(); },
            (map, layers) => { foreach (var layer in layers) map.Layers.Remove(layer); map.Refresh(); },
            layer => { layer.Enabled = false; layer.Dispose(); }, Changed);
    }

    public async Task ActivateAsync(Map map)
    {
        Deactivate();
        AppPreferences.Current.Changed += SettingChanged;
        OfflineMapRuntime.Invalidated += Invalidated;
        map.Navigator.FetchRequested += ViewportChanged;
        subscribed = true;
        await session.ActivateAsync(map, AppPreferences.IsEnabled(AppSetting.DetailedOfflineMap));
    }

    public void Deactivate()
    {
        if (subscribed)
        {
            AppPreferences.Current.Changed -= SettingChanged;
            OfflineMapRuntime.Invalidated -= Invalidated;
            if (session.Map is { } map) map.Navigator.FetchRequested -= ViewportChanged;
            subscribed = false;
        }
        coverageRequest++;
        session.Deactivate();
    }

    private void SettingChanged(AppSetting setting)
    {
        if (setting == AppSetting.DetailedOfflineMap)
            MainThread.BeginInvokeOnMainThread(async () => { if (subscribed) await session.RefreshAsync(AppPreferences.IsEnabled(setting)); });
    }
    private void Invalidated() { coverageRequest++; session.Invalidate(); }
    private void ViewportChanged(object? sender, EventArgs args) => MainThread.BeginInvokeOnMainThread(async () => await UpdateCoverageAsync());
    private void Changed(OfflineDisplayState state)
    {
        coverageRequest++;
        status(state switch
        {
            OfflineDisplayState.Loading => "Preparing packaged detail… Basic roads and points remain available where covered.",
            OfflineDisplayState.Disabled => "Detailed offline map Off · basic roads and points remain where available.",
            OfflineDisplayState.Unavailable => "Packaged detail unavailable · basic roads and points remain where available.",
            OfflineDisplayState.Cleared => "Map cache cleared · reopen this map or enable detail to recreate replaceable copies.",
            OfflineDisplayState.Ready => "Packaged detail loaded · checking coverage at the map center…",
            _ => ""
        });
        if (state == OfflineDisplayState.Ready) _ = UpdateCoverageAsync();
    }
    private async Task UpdateCoverageAsync()
    {
        if (session.State != OfflineDisplayState.Ready || session.Map is not { } map) return;
        var viewport = map.Navigator.Viewport;
        if (!double.IsFinite(viewport.Resolution) || viewport.Resolution <= 0) return;
        var token = session.Token; long current = ++coverageRequest;
        var (longitude, latitude) = Mapsui.Projections.SphericalMercator.ToLonLat(viewport.CenterX, viewport.CenterY);
        int zoom = (int)Math.Round(Math.Log2(156543.03392804097 / viewport.Resolution));
        try
        {
            bool covered = await OfflineMapRuntime.Cache.CoversAsync(longitude, latitude, zoom, token);
            if (token.IsCancellationRequested || current != coverageRequest || session.Map != map || session.State != OfflineDisplayState.Ready) return;
            status(covered ? "Packaged tile at map center · zoom 13–18 · static context; verify current conditions."
                : "No packaged tile at map center or this zoom · basic data where available; detail covers part of Marikina at zoom 13–18.");
        }
        catch (OperationCanceledException) { }
        catch { if (current == coverageRequest && session.State == OfflineDisplayState.Ready) status("Packaged tile coverage unavailable · basic data remains where available."); }
    }
}
