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
    private long? missingSince;

    public CameraStartupPresentation Update(bool active, bool running, bool freshTexture,
        CameraStartupRun run, TimeSpan waiting, string? failure = null, long? monotonicNow = null,
        bool recentPresentation = false)
    {
        if (!active || !running || readyRun != run || freshTexture || recentPresentation || !string.IsNullOrWhiteSpace(failure))
            missingSince = null;
        if (!active || !running || !run.IsValid || !string.IsNullOrWhiteSpace(failure))
            readyRun = null;
        else if (freshTexture)
            readyRun = run;
        else if (readyRun != run)
            readyRun = null;

        // Keep brief GPU conversion gaps invisible. A sustained interruption
        // needs an honest recovery cover rather than a frozen camera image.
        if (readyRun == run && !freshTexture && !recentPresentation && monotonicNow.HasValue)
        {
            if (missingSince > monotonicNow) missingSince = null;
            missingSince ??= monotonicNow;
            long elapsed = monotonicNow.Value - missingSince.Value;
            if (elapsed >= 20_000)
                return new(true, false, "Camera view interrupted",
                    "Switch to 2D Map to continue. The live view will return when ready.");
            if (elapsed >= 2_000)
                return new(true, true, "Recovering camera view…", "Keep the street in view while the camera recovers.");
        }
        return CameraStartupPresentation.Resolve(active, running,
            run.IsValid && readyRun == run, waiting, failure);
    }
}
