using RescuAR.AR;
using RescuAR.MAUI.Services.Navigation;

internal static class ARCameraPresentationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var previous = ARRenderGenerationBridge.Current;
        bool accepting = ARRenderGenerationBridge.IsAcceptingFrames;
        var generation = new ARRenderGenerationToken(101, 102, 103);
        var metadata = new ARFrameMetadata(generation, 9000, new(103, 0, 1080, 2340))
            { AcquiredAtMonotonicMilliseconds = 1000 };
        ARRenderGenerationBridge.Activate(generation);
        try
        {
            check(ARCameraPresentationPolicy.CanPresent(102, metadata, true, 1000),
                "a fresh current camera frame permits AR scene presentation");
            check(!ARCameraPresentationPolicy.CanPresent(102, metadata, false, 1000),
                "a missing or in-flight camera texture cannot expose the scene skybox");
            check(!ARCameraPresentationPolicy.CanPresent(102, metadata, true, 1751) &&
                !ARCameraPresentationPolicy.CanPresent(102, metadata, true, 999),
                "stale and future acquisition timestamps cannot present a camera frame");
            ARCameraPresentationPolicy.RecordPresented(metadata, 1000);
            check(ARCameraPresentationPolicy.HasRecentPresentation(generation, 1500) &&
                !ARCameraPresentationPolicy.HasRecentPresentation(generation, 1751),
                "camera health observes actual completed presentation without extending its freshness budget");
            check(!ARCameraPresentationPolicy.CanPresent(104, metadata, true, 1000),
                "another graphics context cannot present the old camera texture");
            foreach (var changed in new[] { generation with { SessionGeneration = 104 },
                generation with { GraphicsGeneration = 104 }, generation with { GeometryGeneration = 104 } })
            {
                ARRenderGenerationBridge.Activate(changed);
                check(!ARCameraPresentationPolicy.CanPresent(102, metadata, true, 1000),
                    "session, graphics or display changes invalidate camera presentation");
            }
            ARRenderGenerationBridge.Invalidate();
            check(!ARCameraPresentationPolicy.CanPresent(102, metadata, true, 1000),
                "suspended AR processing cannot present retained camera content");
            check(ARCameraPresentationPolicy.CanPresent(0, default, false, 1000),
                "ordinary desktop rendering does not acquire an Android camera requirement");
        }
        finally
        {
            if (accepting) ARRenderGenerationBridge.Activate(previous);
            else ARRenderGenerationBridge.Invalidate();
        }

        var state = new CameraStartupState();
        var run = new CameraStartupRun(1, 2);
        state.Update(true, true, true, run, TimeSpan.Zero, monotonicNow: 1000);
        var view = state.Update(true, true, false, run, TimeSpan.Zero, monotonicNow: 1100);
        check(!view.Visible, "a brief post-ready gap does not flash a recovery cover");
        view = state.Update(true, true, false, run, TimeSpan.Zero, monotonicNow: 3099);
        check(!view.Visible, "camera recovery cover waits two continuous seconds");
        view = state.Update(true, true, false, run, TimeSpan.Zero, monotonicNow: 3100);
        check(view.Visible && view.Busy && view.Title == "Recovering camera view…",
            "a sustained missing camera frame shows recovery rather than frozen imagery or a skybox");
        view = state.Update(true, true, false, run, TimeSpan.Zero, monotonicNow: 21_100);
        check(view.Visible && !view.Busy && view.Message.Contains("2D Map"),
            "a prolonged interruption provides an actionable map fallback");
        view = state.Update(true, true, true, run, TimeSpan.Zero, monotonicNow: 21_200);
        check(!view.Visible, "a fresh recovered texture clears the recovery cover immediately");
        state.Update(true, true, false, run, TimeSpan.Zero, monotonicNow: 22_000);
        view = state.Update(true, true, false, run, TimeSpan.Zero, monotonicNow: 23_000);
        check(!view.Visible, "separate short gaps cannot accumulate into a camera failure");
        view = state.Update(true, true, false, run, TimeSpan.Zero, monotonicNow: 25_000, recentPresentation: true);
        check(!view.Visible, "completed camera draws between UI polls prevent a false recovery cover during conversion");
        state.Update(false, true, false, run, TimeSpan.Zero, monotonicNow: 23_100);
        view = state.Update(true, true, false, run, TimeSpan.Zero, monotonicNow: 23_200, recentPresentation: true);
        check(view.Visible && view.Title == "Preparing camera view…", "leaving AR clears recovery and still requires a fresh startup frame on return");
    }
}
