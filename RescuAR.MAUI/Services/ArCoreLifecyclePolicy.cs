namespace RescuAR.MAUI.Services;

/// <summary>
/// Pure lifecycle rules shared by the Android owner and automated tests.
/// </summary>
public static class ArCoreLifecyclePolicy
{
    public static bool IsLatestRequest(long requestGeneration, ArCoreLifecycleTarget requestedTarget, long latestGeneration, ArCoreLifecycleTarget desiredTarget) =>
        requestGeneration == latestGeneration && requestedTarget == desiredTarget;

    public static bool IsLegalTransition(ArCoreLifecycleState from, ArCoreLifecycleState to)
    {
        if (from == to)
        {
            return true;
        }

        return from switch
        {
            ArCoreLifecycleState.Uninitialized => to is ArCoreLifecycleState.Initializing or ArCoreLifecycleState.Disposing or ArCoreLifecycleState.Disposed or ArCoreLifecycleState.Faulted,
            ArCoreLifecycleState.Initializing => to is ArCoreLifecycleState.Uninitialized or ArCoreLifecycleState.Running or ArCoreLifecycleState.Paused or ArCoreLifecycleState.Disposing or ArCoreLifecycleState.Faulted,
            ArCoreLifecycleState.Running => to is ArCoreLifecycleState.Pausing or ArCoreLifecycleState.Disposing or ArCoreLifecycleState.Faulted,
            ArCoreLifecycleState.Pausing => to is ArCoreLifecycleState.Paused or ArCoreLifecycleState.Disposing or ArCoreLifecycleState.Faulted,
            ArCoreLifecycleState.Paused => to is ArCoreLifecycleState.Initializing or ArCoreLifecycleState.Running or ArCoreLifecycleState.Disposing or ArCoreLifecycleState.Faulted,
            ArCoreLifecycleState.Faulted => to is ArCoreLifecycleState.Uninitialized or ArCoreLifecycleState.Initializing or ArCoreLifecycleState.Pausing or ArCoreLifecycleState.Paused or ArCoreLifecycleState.Disposing or ArCoreLifecycleState.Disposed,
            ArCoreLifecycleState.Disposing => to is ArCoreLifecycleState.Disposed or ArCoreLifecycleState.Faulted,
            ArCoreLifecycleState.Disposed => false,
            _ => false,
        };
    }

    public static ArCoreFailureCode ClassifyFailureCode(Exception? exception, ArCoreFailureCode fallback)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            string typeName = current.GetType().Name;
            if (typeName.Contains("NotCompatible", StringComparison.OrdinalIgnoreCase))
            {
                return ArCoreFailureCode.UnsupportedDevice;
            }

            if (typeName.Contains("NotInstalled", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("ApkTooOld", StringComparison.OrdinalIgnoreCase) ||
                typeName.Contains("SdkTooOld", StringComparison.OrdinalIgnoreCase))
            {
                return ArCoreFailureCode.InstallationRequired;
            }

            if (typeName.Contains("CameraNotAvailable", StringComparison.OrdinalIgnoreCase))
            {
                return ArCoreFailureCode.CameraUnavailable;
            }

            if (current is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                return ArCoreFailureCode.NativeBridgeUnavailable;
            }

            if (current is TimeoutException)
            {
                return ArCoreFailureCode.TimedOut;
            }
        }

        return fallback;
    }

    public static ArCoreLifecycleState GetFailureState(bool sessionExists, bool sessionPaused) =>
        sessionExists && sessionPaused ? ArCoreLifecycleState.Paused : ArCoreLifecycleState.Faulted;
}
