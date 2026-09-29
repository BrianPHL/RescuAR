namespace RescuAR.MAUI.Services;

/// <summary>
/// Retains the camera view's intent across transient Activity and surface loss.
/// The lifecycle owner serializes changes; frame work may read readiness.
/// </summary>
public sealed class ArCoreRunIntent
{
    private volatile bool requested;
    private volatile bool activityResumed = true;
    private volatile bool surfaceReady;
    private volatile bool shutdown;

    public bool Requested => requested && !shutdown;
    public bool ActivityResumed => activityResumed;
    public bool SurfaceReady => surfaceReady;
    public bool ShouldRun => Requested && activityResumed && surfaceReady;

    public void Request(bool running) => requested = running && !shutdown;
    public void SetActivityResumed(bool resumed) => activityResumed = resumed;
    public void SetSurfaceReady(bool ready) => surfaceReady = ready;

    public void Shutdown()
    {
        shutdown = true;
        requested = false;
        surfaceReady = false;
    }
}
