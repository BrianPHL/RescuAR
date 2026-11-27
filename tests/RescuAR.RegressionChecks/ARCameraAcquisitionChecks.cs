using RescuAR.MAUI.Platforms.Android.Services;

internal static class ARCameraAcquisitionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var run = new ArCameraFrameRun(1, 2, 3);
        var policy = new ArCameraFrameAcquisitionPolicy();
        ArCameraFrameAcquisitionAction Observe(ArCameraFrameAcquisitionEvent observation, long at) =>
            policy.Observe(run, observation, at).Action;

        check(Observe(ArCameraFrameAcquisitionEvent.MissingFrame, 50) == ArCameraFrameAcquisitionAction.Continue &&
            Observe(ArCameraFrameAcquisitionEvent.MissingFrame, 3049) == ArCameraFrameAcquisitionAction.Continue,
            "camera acquisition tolerates ordinary startup and short gaps in fresh frames");
        check(Observe(ArCameraFrameAcquisitionEvent.MissingFrame, 3050) == ArCameraFrameAcquisitionAction.RecoverSession &&
            Observe(ArCameraFrameAcquisitionEvent.MissingFrame, 3400) == ArCameraFrameAcquisitionAction.Continue &&
            Observe(ArCameraFrameAcquisitionEvent.MissingFrame, 6400) == ArCameraFrameAcquisitionAction.Fault,
            "a continuous missing-frame stall gets one session restart before falling back");

        policy = new();
        var first = policy.Observe(run, ArCameraFrameAcquisitionEvent.RetryableFailure, 10);
        var second = policy.Observe(run, ArCameraFrameAcquisitionEvent.RetryableFailure, 260);
        check(first == new ArCameraFrameAcquisitionDecision(ArCameraFrameAcquisitionAction.Wait, 250) &&
            second == new ArCameraFrameAcquisitionDecision(ArCameraFrameAcquisitionAction.Wait, 500) &&
            Observe(ArCameraFrameAcquisitionEvent.RetryableFailure, 760) == ArCameraFrameAcquisitionAction.RecoverSession,
            "unknown frame errors back off before attempting one recovery");
        check(Observe(ArCameraFrameAcquisitionEvent.FrameReady, 1500) == ArCameraFrameAcquisitionAction.Continue &&
            Observe(ArCameraFrameAcquisitionEvent.SessionInterrupted, 1600) == ArCameraFrameAcquisitionAction.Fault,
            "one fresh frame does not rearm repeated camera-loss recovery");

        policy = new();
        check(Observe(ArCameraFrameAcquisitionEvent.SessionInterrupted, 0) == ArCameraFrameAcquisitionAction.RecoverSession &&
            Observe(ArCameraFrameAcquisitionEvent.FrameReady, 250) == ArCameraFrameAcquisitionAction.Continue &&
            Observe(ArCameraFrameAcquisitionEvent.FrameReady, 1250) == ArCameraFrameAcquisitionAction.Continue &&
            Observe(ArCameraFrameAcquisitionEvent.SessionInterrupted, 1500) == ArCameraFrameAcquisitionAction.RecoverSession,
            "a healthy period restores the budget for a separate camera interruption");
        check(!run.Owns(run, false) && !run.Owns(run with { Graphics = 9 }, true) &&
            !run.Owns(run with { Session = 9 }, true) && !run.Owns(run with { Request = 9 }, true) && run.Owns(run, true),
            "only the current session, graphics and lifecycle request may recover the camera");
        check(policy.Observe(run with { Request = 4 }, ArCameraFrameAcquisitionEvent.SessionInterrupted, 2000).Action ==
            ArCameraFrameAcquisitionAction.RecoverSession,
            "a new explicit camera run starts with its own recovery budget");

        check(ArCameraFrameAcquisitionPolicy.Classify(new NotYetAvailableException()) == ArCameraFrameAcquisitionEvent.MissingFrame &&
            ArCameraFrameAcquisitionPolicy.Classify(new InvalidOperationException("wrapped", new DeadlineExceededException())) ==
                ArCameraFrameAcquisitionEvent.MissingFrame &&
            ArCameraFrameAcquisitionPolicy.Classify(new CameraNotAvailableException()) == ArCameraFrameAcquisitionEvent.SessionInterrupted &&
            ArCameraFrameAcquisitionPolicy.Classify(new SessionPausedException()) == ArCameraFrameAcquisitionEvent.SessionInterrupted &&
            ArCameraFrameAcquisitionPolicy.Classify(new FatalException()) == ArCameraFrameAcquisitionEvent.FatalFailure &&
            ArCameraFrameAcquisitionPolicy.Classify(new EntryPointNotFoundException()) == ArCameraFrameAcquisitionEvent.FatalFailure &&
            ArCameraFrameAcquisitionPolicy.Classify(new InvalidOperationException()) == ArCameraFrameAcquisitionEvent.RetryableFailure &&
            Observe(ArCameraFrameAcquisitionEvent.FatalFailure, 2100) == ArCameraFrameAcquisitionAction.Fault,
            "frame errors distinguish transient missing images, session interruptions, unknown faults and immediate failures");
    }

    private sealed class NotYetAvailableException : Exception { }
    private sealed class DeadlineExceededException : Exception { }
    private sealed class CameraNotAvailableException : Exception { }
    private sealed class SessionPausedException : Exception { }
    private sealed class FatalException : Exception { }
}
