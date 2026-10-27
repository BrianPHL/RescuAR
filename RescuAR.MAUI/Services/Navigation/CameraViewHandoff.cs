namespace RescuAR.MAUI.Services.Navigation;

public enum CameraPresentationView { Ar, Map2D }

/// <summary>A one-use display request owned by one destination version; owns no route or location.</summary>
public sealed class CameraViewHandoff
{
    public static CameraViewHandoff Current { get; } = new();
    private readonly object sync = new();
    private (long Id, long Trip, CameraPresentationView View)? pending;
    private long nextId;
    public event Action? Requested;
    public long Request(long trip, CameraPresentationView view)
    {
        long id;
        lock (sync) { id = ++nextId; pending = (id, trip, view); }
        Requested?.Invoke();
        return id;
    }
    public bool TryConsume(long trip, out CameraPresentationView view)
    {
        lock (sync)
        {
            var request = pending; pending = null; view = default;
            if (request is not { } item || item.Trip != trip) return false;
            view = item.View; return true;
        }
    }
    public void Cancel(long id) { lock (sync) { if (pending?.Id == id) pending = null; } }
}
