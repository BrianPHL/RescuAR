namespace RescuAR.MAUI.Services.Navigation;

public enum OfflineDisplayState { Inactive, Loading, Ready, Disabled, Unavailable, Cleared }

/// <summary>Owns only its optional display layers. Routing/people overlays stay with their existing owners.</summary>
public sealed class OfflineOverlaySession<TMap, TLayer>(Func<CancellationToken, Task<IReadOnlyList<TLayer>>> load,
    Action<TMap, IReadOnlyList<TLayer>> attach, Action<TMap, IReadOnlyList<TLayer>> detach, Action<TLayer> dispose,
    Action<OfflineDisplayState> changed) where TMap : class
{
    private TMap? map;
    private IReadOnlyList<TLayer> layers = Array.Empty<TLayer>();
    private CancellationTokenSource? request;
    private long version;
    public OfflineDisplayState State { get; private set; } = OfflineDisplayState.Inactive;
    public TMap? Map => map;
    public CancellationToken Token => request?.Token ?? new CancellationToken(true);
    private void Publish(OfflineDisplayState state) { State = state; changed(state); }
    private void Drop()
    {
        Interlocked.Increment(ref version); request?.Cancel(); request?.Dispose(); request = null;
        if (map is not null && layers.Count > 0) detach(map, layers);
        foreach (var layer in layers) dispose(layer);
        layers = Array.Empty<TLayer>();
    }
    public async Task ActivateAsync(TMap target, bool enabled)
    {
        Deactivate(); map = target;
        await RefreshAsync(enabled);
    }
    public void Deactivate() { Drop(); map = null; Publish(OfflineDisplayState.Inactive); }
    public void Invalidate() { Drop(); if (map is not null) Publish(OfflineDisplayState.Cleared); }
    public async Task RefreshAsync(bool enabled)
    {
        Drop(); if (map is not { } owner) return;
        if (!enabled) { Publish(OfflineDisplayState.Disabled); return; }
        request = new(); var token = request.Token; long current = version;
        Publish(OfflineDisplayState.Loading);
        IReadOnlyList<TLayer>? loaded = null;
        try
        {
            loaded = await load(token);
            if (current != version || map != owner || token.IsCancellationRequested) return;
            try { attach(owner, loaded); }
            catch { detach(owner, loaded); throw; }
            layers = loaded; loaded = null;
            Publish(OfflineDisplayState.Ready);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch { if (current == version && map == owner) Publish(OfflineDisplayState.Unavailable); }
        finally { if (loaded is not null) foreach (var layer in loaded) dispose(layer); }
    }
}
