namespace RescuAR.MAUI.Platforms.Android.Services;

internal enum ArCameraImportFailureDisposition
{
    RetryWithBackoff,
    TerminalNativeBridge,
    TerminalUnsupportedFormat,
    TerminalRenderer,
}

internal static class ArCameraImportFailurePolicy
{
    internal const int TransientFailureBudget = 3;

    public static ArCameraImportFailureDisposition Classify(
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is DllNotFoundException or
                EntryPointNotFoundException or
                BadImageFormatException)
            {
                return ArCameraImportFailureDisposition.TerminalNativeBridge;
            }

            if (current is NotSupportedException)
            {
                return ArCameraImportFailureDisposition.TerminalUnsupportedFormat;
            }

            if (current.Message.Contains(
                    "VK_ERROR_DEVICE_LOST",
                    StringComparison.OrdinalIgnoreCase))
            {
                return ArCameraImportFailureDisposition.TerminalRenderer;
            }
        }

        return ArCameraImportFailureDisposition.RetryWithBackoff;
    }

    public static bool ShouldEnterTerminalState(ArCameraImportFailureDisposition disposition, int consecutiveFailureCount)
    {
        if (consecutiveFailureCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(consecutiveFailureCount));
        }

        return disposition != ArCameraImportFailureDisposition.RetryWithBackoff ||
            consecutiveFailureCount >= TransientFailureBudget;
    }
}
