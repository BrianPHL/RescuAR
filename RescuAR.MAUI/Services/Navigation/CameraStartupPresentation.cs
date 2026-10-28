namespace RescuAR.MAUI.Services.Navigation;

internal readonly record struct CameraStartupPresentation(bool Visible, bool Busy, string Title, string Message)
{
    public static CameraStartupPresentation Resolve(bool active, bool running, bool cameraReady,
        TimeSpan waiting, string? failure = null)
    {
        if (!active) return default;
        if (!string.IsNullOrWhiteSpace(failure))
            return new(true, false, "Camera unavailable", failure);
        // A visible camera feed does not require ground, heading or route acquisition.
        if (running && cameraReady) return default;
        if (waiting >= TimeSpan.FromSeconds(20))
            return new(true, false, "Camera is taking longer than expected",
                "Check camera access or switch to 2D Map. The live view will appear when ready.");
        return new(true, true, running ? "Preparing camera view…" : "Starting AR camera…",
            "The live view will appear as soon as the camera is ready.");
    }
}

internal readonly record struct CameraStartupRun(long Session, long Graphics)
{
    public bool IsValid => Session > 0 && Graphics > 0;
}

/// <summary>
/// Startup ends after the first qualified camera texture for this active run.
/// Brief conversion gaps must not turn a startup cover into a frame monitor.
/// Rendering and spatial consumers retain their independent freshness gates.
/// </summary>
internal sealed class CameraStartupState
{
    private CameraStartupRun? readyRun;

    public CameraStartupPresentation Update(bool active, bool running, bool freshTexture,
        CameraStartupRun run, TimeSpan waiting, string? failure = null)
    {
        if (!active || !running || !run.IsValid || !string.IsNullOrWhiteSpace(failure))
            readyRun = null;
        else if (freshTexture)
            readyRun = run;
        else if (readyRun != run)
            readyRun = null;

        return CameraStartupPresentation.Resolve(active, running,
            run.IsValid && readyRun == run, waiting, failure);
    }
}
