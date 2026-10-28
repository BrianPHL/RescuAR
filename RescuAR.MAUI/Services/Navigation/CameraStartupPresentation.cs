namespace RescuAR.MAUI.Services.Navigation;

internal readonly record struct CameraStartupPresentation(bool Visible, bool Busy, string Title, string Message)
{
    public static CameraStartupPresentation Resolve(bool active, bool running, bool freshTexture,
        TimeSpan waiting, string? failure = null)
    {
        if (!active) return default;
        if (!string.IsNullOrWhiteSpace(failure))
            return new(true, false, "Camera unavailable", failure);
        // A visible camera feed does not require ground, heading or route acquisition.
        if (running && freshTexture) return default;
        if (waiting >= TimeSpan.FromSeconds(20))
            return new(true, false, "Camera is taking longer than expected",
                "Check camera access or switch to 2D Map. The live view will appear when ready.");
        return new(true, true, running ? "Preparing camera view…" : "Starting AR camera…",
            "The live view will appear as soon as the camera is ready.");
    }
}
