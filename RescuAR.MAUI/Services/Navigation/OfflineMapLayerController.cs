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
            OfflineDisplayState.Loading => "Loading offline map…",
            OfflineDisplayState.Disabled => "Offline detail Off · online map requires a connection.",
            OfflineDisplayState.Unavailable => "Offline detail unavailable · online map requires a connection.",
            OfflineDisplayState.Cleared => "Map cache cleared · reopen this map or enable detail to recreate replaceable copies.",
            OfflineDisplayState.Ready => "Offline map ready · checking coverage…",
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
            status(covered ? "Offline map · Marikina detail"
                : "Outside offline detail · online map requires a connection.");
        }
        catch (OperationCanceledException) { }
        catch { if (current == coverageRequest && session.State == OfflineDisplayState.Ready) status("Offline coverage unavailable · online map requires a connection."); }
    }
}
