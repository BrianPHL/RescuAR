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

        var state = new CameraStartupState();
        var run = new CameraStartupRun(10, 20);
        view = state.Update(true, true, false, run, TimeSpan.Zero);
        check(view.Visible && view.Busy, "a new camera run still waits for its first qualified camera texture");
        view = state.Update(true, true, true, run, TimeSpan.FromSeconds(1));
        check(!view.Visible, "the first qualified texture completes startup for its session and graphics context");
        for (int i = 0; i < 10; i++)
        {
            view = state.Update(true, true, false, run, TimeSpan.FromSeconds(2 + i));
            check(!view.Visible && !view.Busy, $"post-ready conversion gap {i + 1} cannot flash the startup cover");
        }
        view = state.Update(true, true, false, run, TimeSpan.FromMinutes(1));
        check(!view.Visible, "completed startup does not become a timeout banner after a frame gap");
        view = state.Update(true, true, false, run, TimeSpan.Zero);
        check(!view.Visible, "AR-to-Flood changes sharing the same running camera preserve startup readiness");

        state.Update(false, true, false, run, TimeSpan.Zero);
        view = state.Update(true, true, false, run, TimeSpan.Zero);
        check(view.Visible, "leaving Camera, choosing 2D or stopping the window resets startup before re-entry");
        state.Update(true, true, true, run, TimeSpan.Zero);
        view = state.Update(true, false, true, run, TimeSpan.Zero);
        check(view.Visible, "a real camera pause cannot be hidden by readiness from its previous running interval");
        view = state.Update(true, true, false, run, TimeSpan.Zero);
        check(view.Visible, "resuming a paused camera requires a new fresh texture");

        state.Update(true, true, true, run, TimeSpan.Zero);
        var nextSession = run with { Session = 11 };
        view = state.Update(true, true, false, nextSession, TimeSpan.Zero);
        check(view.Visible, "a replacement ARCore session cannot inherit the previous session's ready latch");
        state.Update(true, true, true, nextSession, TimeSpan.Zero);
        var nextGraphics = nextSession with { Graphics = 21 };
        view = state.Update(true, true, false, nextGraphics, TimeSpan.Zero);
        check(view.Visible, "a replacement graphics context requires its own first camera texture");
        state.Update(true, true, true, nextGraphics, TimeSpan.Zero);
        view = state.Update(true, true, false, nextGraphics, TimeSpan.Zero, "Camera rendering unavailable.");
        check(view.Visible && !view.Busy && view.Message == "Camera rendering unavailable.", "confirmed camera failures remain visible after a previously successful startup");
        view = state.Update(true, true, false, nextGraphics, TimeSpan.Zero);
        check(view.Visible && view.Busy, "clearing an actual failure does not reuse the old ready latch");
        state.Update(true, true, true, nextGraphics, TimeSpan.Zero);
        view = state.Update(true, true, true, default, TimeSpan.Zero);
        check(view.Visible, "an invalid render-generation identity cannot qualify startup readiness");
        view = state.Update(true, true, false, nextGraphics, TimeSpan.Zero);
        check(view.Visible, "render-generation invalidation also clears readiness before recovery");

    }
}
