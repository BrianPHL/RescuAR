namespace RescuAR.MAUI.Services.Settings;

// The advisory preference owns automatic playback only; existing manual callers retain their channel.
public sealed class AlarmPlaybackController(Func<Action, bool> start, Action stop)
{
    private long generation;
    public bool IsAutomatic { get; private set; }
    public bool IsActive { get; private set; }
    public void PlayManual() => Play(false);
    public void PlayAutomatic(bool enabled) { if (enabled) Play(true); }
    private void Play(bool automatic)
    {
        Stop();
        long request = ++generation;
        IsActive = true; IsAutomatic = automatic;
        try { if (!start(() => Complete(request))) Complete(request); }
        catch { Complete(request); throw; }
    }
    private void Complete(long request) { if (request == generation) { IsActive = false; IsAutomatic = false; } }
    public void StopAutomatic() { if (IsActive && IsAutomatic) Stop(); }
    public void Stop()
    {
        generation++; IsActive = false; IsAutomatic = false;
        stop();
    }
}
