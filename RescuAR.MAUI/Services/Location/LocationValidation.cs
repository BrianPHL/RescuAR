namespace RescuAR.MAUI.Services.Location;

/// <summary>
/// Temporary Milestone 5 diagnostic.
/// Call ValidateOnceAsync() from a UI action or startup point when you are
/// ready to verify a real device GPS fix.
///
/// It is deliberately not auto-started: Android runtime permission prompts
/// should be initiated while the MAUI activity is visibly active.
/// </summary>
public static class LocationValidation
{
    private const string LogTag =
        "RescuAR-GPS";

    private static int hasRun;

    public static async Task<LocationReading?> ValidateOnceAsync(
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(
                ref hasRun,
                1) != 0)
        {
            return null;
        }

        ILocationService service =
            MauiProgram.Services.GetRequiredService<
                ILocationService>();

        WriteLog(
            "========== RescuAR GPS Validation ==========");

        WriteLog(
            $"Android location services enabled = " +
            $"{service.IsLocationServiceEnabled}");

        bool permissionGranted =
            await service.EnsurePermissionAsync(
                cancellationToken);

        WriteLog(
            $"Foreground location permission granted = " +
            $"{permissionGranted}");

        if (!permissionGranted)
        {
            WriteLog(
                "GPS validation stopped: permission unavailable.");

            WriteLog(
                "============================================");

            return null;
        }

        LocationReading? reading =
            await service.GetCurrentLocationAsync(
                cancellationToken);

        WriteLog(
            reading is null
                ? "GPS validation result = NO CURRENT FIX"
                : "GPS validation result = CURRENT FIX RECEIVED");

        WriteLog(
            "============================================");

        return reading;
    }

    private static void WriteLog(
        string message)
    {
#if ANDROID
        Android.Util.Log.Debug(
            LogTag,
            message);
#endif

        System.Diagnostics.Trace.WriteLine(
            $"[{LogTag}] {message}");
    }
}
