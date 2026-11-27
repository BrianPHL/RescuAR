namespace RescuAR.MAUI.Platforms.Android.Services;

internal enum ArCameraFrameAcquisitionEvent
{
    FrameReady,
    MissingFrame,
    RetryableFailure,
    SessionInterrupted,
    FatalFailure,
}

internal enum ArCameraFrameAcquisitionAction
{
    Continue,
    Wait,
    RecoverSession,
    Fault,
}

internal readonly record struct ArCameraFrameRun(long Session, long Graphics, long Request)
{
    public bool Owns(ArCameraFrameRun current, bool shouldRun) =>
        shouldRun && Session > 0 && Graphics > 0 && this == current;
}

internal readonly record struct ArCameraFrameAcquisitionDecision(
    ArCameraFrameAcquisitionAction Action, int DelayMilliseconds = 0);

/// <summary>Bounded acquisition recovery independent of the GPU import retry budget.</summary>
internal sealed class ArCameraFrameAcquisitionPolicy
{
    internal const long MissingFrameGraceMilliseconds = 3_000;
    internal const long StableFrameWindowMilliseconds = 1_000;
    private ArCameraFrameRun owner;
    private long? unsuccessfulSince;
    private long? receivingSince;
    private int failures;
    private bool recoveryAttempted;

    public ArCameraFrameAcquisitionDecision Observe(ArCameraFrameRun run,
        ArCameraFrameAcquisitionEvent observation, long now)
    {
        if (run != owner)
        {
            owner = run;
            unsuccessfulSince = receivingSince = null;
            failures = 0;
            recoveryAttempted = false;
        }

        if (observation == ArCameraFrameAcquisitionEvent.FrameReady)
        {
            unsuccessfulSince = null;
            failures = 0;
            receivingSince ??= now;
            if (now - receivingSince.Value >= StableFrameWindowMilliseconds)
                recoveryAttempted = false;
            return new(ArCameraFrameAcquisitionAction.Continue);
        }

        receivingSince = null;
        unsuccessfulSince ??= now;
        if (observation == ArCameraFrameAcquisitionEvent.FatalFailure)
            return new(ArCameraFrameAcquisitionAction.Fault);

        bool expired = now - unsuccessfulSince.Value >= MissingFrameGraceMilliseconds;
        if (!expired && observation == ArCameraFrameAcquisitionEvent.MissingFrame)
            return new(ArCameraFrameAcquisitionAction.Continue);

        if (!expired && observation == ArCameraFrameAcquisitionEvent.RetryableFailure && ++failures < 3)
            return new(ArCameraFrameAcquisitionAction.Wait, failures * 250);

        if (recoveryAttempted) return new(ArCameraFrameAcquisitionAction.Fault);

        recoveryAttempted = true;
        unsuccessfulSince = null;
        failures = 0;
        return new(ArCameraFrameAcquisitionAction.RecoverSession, 250);
    }

    // Match ARCore binding exception names without requiring Android on host checks.
    public static ArCameraFrameAcquisitionEvent Classify(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                return ArCameraFrameAcquisitionEvent.FatalFailure;

            switch (current.GetType().Name)
            {
                case "NotYetAvailableException":
                case "DeadlineExceededException":
                    return ArCameraFrameAcquisitionEvent.MissingFrame;
                case "CameraNotAvailableException":
                case "SessionPausedException":
                    return ArCameraFrameAcquisitionEvent.SessionInterrupted;
                case "FatalException":
                case "MissingGlContextException":
                case "TextureNotSetException":
                case "UnsupportedConfigurationException":
                case "SecurityException":
                    return ArCameraFrameAcquisitionEvent.FatalFailure;
            }
        }

        return ArCameraFrameAcquisitionEvent.RetryableFailure;
    }
}
