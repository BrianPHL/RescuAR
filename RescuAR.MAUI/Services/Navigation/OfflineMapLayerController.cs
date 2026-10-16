using Mapsui;
using Mapsui.Layers;
using RescuAR.MAUI.Services.Settings;
using Map = Mapsui.Map;

namespace RescuAR.MAUI.Services.Navigation;

/// <summary>Owns optional display layers and a preference subscription for one visible page.</summary>
public sealed class OfflineMapLayerController(Action<string> setStatus)
{
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private IReadOnlyList<ILayer> layers = [];
    private Map? map;
    private bool active;

    public async Task ActivateAsync(Map target)
    {
        map = target;
        if (!active) AppPreferences.Current.Changed += OnPreferenceChanged;
        active = true;
        await RefreshAsync();
    }

    public void Deactivate()
    {
        active = false;
        AppPreferences.Current.Changed -= OnPreferenceChanged;
    }

    private void OnPreferenceChanged(AppSetting setting)
    {
        if (setting == AppSetting.DetailedOfflineMap)
            MainThread.BeginInvokeOnMainThread(async () => await RefreshAsync());
    }

    private async Task RefreshAsync()
    {
        if (!active || map is null) return;
        bool enabled = AppPreferences.Current.Get(AppSetting.DetailedOfflineMap);
        foreach (var layer in layers) layer.Enabled = enabled;
        map.Refresh();
        if (!enabled) { setStatus("Basic offline roads • detailed map off"); return; }
        await loadGate.WaitAsync();
        try
        {
            if (!active || !AppPreferences.Current.Get(AppSetting.DetailedOfflineMap)) return;
            setStatus("Loading detailed offline map…");
            await Task.Run(() => DetailedOfflineMapService.PrepareAsync());
            if (!active || !AppPreferences.Current.Get(AppSetting.DetailedOfflineMap)) return;
            if (layers.Count == 0)
            {
                var loaded = await Task.Run(() => DetailedOfflineMapService.CreateLayersAsync());
                if (!active || !AppPreferences.Current.Get(AppSetting.DetailedOfflineMap))
                {
                    foreach (var layer in loaded) layer.Dispose();
                    return;
                }
                layers = loaded;
                // Background context stays below dev roads, selected routes and location markers.
                for (int index = 0; index < layers.Count; index++) map.Layers.Insert(index, layers[index]);
            }
            foreach (var layer in layers) layer.Enabled = true;
            setStatus("Detailed offline map • Marikina • zoom 13–18 • static context");
            map.Refresh();
        }
        catch (Exception)
        {
            if (active) setStatus(AppPreferences.Current.Get(AppSetting.DetailedOfflineMap)
                ? "Detailed map unavailable • basic roads remain available. Reopen the map to retry."
                : "Basic offline roads • detailed map off");
        }
        finally { loadGate.Release(); }
    }
}
