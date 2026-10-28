using RescuAR.MAUI.Services.Navigation;

internal static class CameraStartupChecks
{
    public static void Run(Action<bool, string> check)
    {
        var view = CameraStartupPresentation.Resolve(false, false, false, TimeSpan.Zero);
        check(!view.Visible && !view.Busy, "inactive camera pages and 2D mode do not display or animate camera loading");
        view = CameraStartupPresentation.Resolve(true, false, false, TimeSpan.Zero);
        check(view.Visible && view.Busy && view.Title == "Starting AR camera…", "cold camera entry masks the skybox with an explicit startup status");
        view = CameraStartupPresentation.Resolve(true, true, false, TimeSpan.FromSeconds(2));
        check(view.Visible && view.Busy && view.Title == "Preparing camera view…", "a running AR session is insufficient until a fresh converted camera texture exists");
        view = CameraStartupPresentation.Resolve(true, false, true, TimeSpan.FromSeconds(2));
        check(view.Visible, "a retained texture cannot reveal a camera session that is still paused or restarting");
        view = CameraStartupPresentation.Resolve(true, true, true, TimeSpan.FromSeconds(2));
        check(!view.Visible && !view.Busy, "a fresh live camera feed clears loading without waiting for ground or route acquisition");
        view = CameraStartupPresentation.Resolve(true, true, false, TimeSpan.FromSeconds(19));
        check(view.Visible && view.Busy, "camera conversion remains visibly pending during the initial startup interval");
        view = CameraStartupPresentation.Resolve(true, true, false, TimeSpan.FromSeconds(20));
        check(view.Visible && !view.Busy && view.Message.Contains("2D Map"), "slow startup shows actionable status rather than an endless busy spinner or skybox");
        view = CameraStartupPresentation.Resolve(true, true, true, TimeSpan.FromMinutes(1));
        check(!view.Visible, "a late valid camera feed clears even the delayed-start status");
        view = CameraStartupPresentation.Resolve(true, false, false, TimeSpan.Zero, "Camera permission is required.");
        check(view.Visible && !view.Busy && view.Message == "Camera permission is required.", "known camera failures display their message without claiming loading is progressing");
        view = CameraStartupPresentation.Resolve(true, true, true, TimeSpan.Zero, "Camera rendering unavailable.");
        check(view.Visible && !view.Busy, "terminal camera failures cannot be hidden by a briefly retained texture");
        view = CameraStartupPresentation.Resolve(false, false, false, TimeSpan.FromMinutes(1), "Camera unavailable.");
        check(!view.Visible && !view.Busy, "backgrounding or switching to 2D hides even a retained camera error overlay");
        view = CameraStartupPresentation.Resolve(true, true, false, TimeSpan.Zero, "  ");
        check(view.Visible && view.Busy, "an empty failure message cannot masquerade as a confirmed startup failure");
    }
}
