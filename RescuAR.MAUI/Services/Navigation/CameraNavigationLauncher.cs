#if ANDROID
using Android.Util;
#endif

using System.Globalization;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.State;

namespace RescuAR.MAUI.Services.Navigation;

/// <summary>
/// Single application entry point for starting AR navigation to an
/// evacuation center.
///
/// This class deliberately supports both evacuation-center models currently
/// present in RescuAR:
///
/// 1. RescuAR.App.Models.EvacuationCenter
///    - UI/backend model
///    - owns Name / Latitude / Longitude / capacity/status information.
///
/// 2. RescuAR.Navigation.Models.EvacuationCenter
///    - clean navigation-domain model
///    - owns GeoCoordinate?.
///
/// The models remain separate. This launcher maps either representation into
/// NavigationDestinationBridge and then opens the existing Camera Shell tab.
/// </summary>
public static class CameraNavigationLauncher
{
    private const string LogTag =
        "RescuAR-MLD";

    /// <summary>
    /// Primary overload for the existing MAUI UI/backend evacuation-center
    /// model.
    ///
    /// Use this directly from the evacuation-center selection command/event:
    ///
    ///     await CameraNavigationLauncher.OpenAsync(selectedCenter);
    ///
    /// No navigation-model conversion is required in the View/ViewModel.
    /// </summary>
    public static async Task<bool> OpenAsync(
        RescuAR.App.Models.EvacuationCenter center)
    {
        ArgumentNullException.ThrowIfNull(
            center);

        string name =
            center.Name?.Trim() ??
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                name))
        {
#if ANDROID
            Log.Warn(
                LogTag,
                "Navigation not started: selected evacuation center has no name.");
#endif

            return false;
        }

        if (!TryCreateCoordinate(
                center.Latitude,
                center.Longitude,
                out GeoCoordinate coordinate,
                out string? reason))
        {
#if ANDROID
            Log.Warn(
                LogTag,
                "Navigation not started: " +
                $"'{name}' has an invalid/unverified coordinate. " +
                $"Reason={reason}");
#endif

            return false;
        }

#if ANDROID
        Log.Debug(
            LogTag,
            "UI evacuation center mapped to navigation destination: " +
            $"name='{name}', " +
            $"lat={coordinate.Latitude:F7}, " +
            $"lon={coordinate.Longitude:F7}");
#endif

        return await OpenAsync(
            name,
            coordinate);
    }

    /// <summary>
    /// Overload for the navigation-domain evacuation-center model.
    /// </summary>
    public static async Task<bool> OpenAsync(
        RescuAR.Navigation.Models.EvacuationCenter center)
    {
        ArgumentNullException.ThrowIfNull(
            center);

        if (!NavigationDestinationBridge.Set(
                center))
        {
#if ANDROID
            Log.Warn(
                LogTag,
                $"Navigation not started: '{center.Name}' has no verified coordinate.");
#endif

            return false;
        }

        return await OpenCameraAsync(
            center.Name);
    }

    /// <summary>
    /// Primitive/adapter overload for pages whose model is not one of the
    /// two known evacuation-center types.
    /// </summary>
    public static async Task<bool> OpenAsync(
        string name,
        double latitude,
        double longitude)
    {
        GeoCoordinate coordinate =
            new(
                latitude,
                longitude);

        return await OpenAsync(
            name,
            coordinate);
    }

    /// <summary>
    /// Already-converted coordinate overload.
    /// </summary>
    public static async Task<bool> OpenAsync(
        string name,
        GeoCoordinate coordinate)
    {
        string cleanName =
            name?.Trim() ??
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                cleanName))
        {
#if ANDROID
            Log.Warn(
                LogTag,
                "Navigation not started: destination name is empty.");
#endif

            return false;
        }

        if (!coordinate.IsValid)
        {
#if ANDROID
            Log.Warn(
                LogTag,
                "Navigation not started: " +
                $"'{cleanName}' coordinate is invalid " +
                $"({coordinate.Latitude:F7},{coordinate.Longitude:F7}).");
#endif

            return false;
        }

        NavigationDestinationBridge.Set(
            cleanName,
            coordinate);

        return await OpenCameraAsync(
            cleanName);
    }

    /// <summary>
    /// Publishes the destination but leaves the user on the current page.
    ///
    /// This is useful if a page already controls its own Shell navigation.
    /// After calling this successfully, its existing navigation to //Camera
    /// may remain unchanged.
    /// </summary>
    public static bool Publish(
        RescuAR.App.Models.EvacuationCenter center)
    {
        ArgumentNullException.ThrowIfNull(
            center);

        string name =
            center.Name?.Trim() ??
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                name))
        {
            return false;
        }

        if (!TryCreateCoordinate(
                center.Latitude,
                center.Longitude,
                out GeoCoordinate coordinate,
                out string? reason))
        {
#if ANDROID
            Log.Warn(
                LogTag,
                "Navigation destination publish rejected: " +
                $"name='{name}', reason={reason}");
#endif

            return false;
        }

        NavigationDestinationBridge.Set(
            name,
            coordinate);

#if ANDROID
        Log.Debug(
            LogTag,
            $"Navigation destination prepared without changing tabs: '{name}'.");
#endif

        return true;
    }

    private static async Task<bool> OpenCameraAsync(
        string destinationName)
    {
        Shell? shell =
            Shell.Current;

        if (shell is null)
        {
#if ANDROID
            Log.Error(
                LogTag,
                "Navigation destination was published, but Shell.Current is null. " +
                "Camera tab could not be opened.");
#endif

            return false;
        }

#if ANDROID
        Log.Debug(
            LogTag,
            $"Opening Camera tab for destination '{destinationName}'.");
#endif

        try
        {
            /*
             * AppShell defines:
             *
             *     Route="Camera"
             *
             * Absolute navigation preserves the normal bottom-tab layout.
             */
            await shell.GoToAsync(
                "//Camera");

#if ANDROID
            Log.Debug(
                LogTag,
                $"Camera tab navigation completed for '{destinationName}'.");
#endif

            return true;
        }
        catch (Exception exception)
        {
#if ANDROID
            Log.Error(
                LogTag,
                "Failed to open Camera tab after publishing destination: " +
                $"{exception}");
#endif

            return false;
        }
    }

    private static bool TryCreateCoordinate(
        object? latitudeValue,
        object? longitudeValue,
        out GeoCoordinate coordinate,
        out string? reason)
    {
        coordinate =
            default;

        reason =
            null;

        if (!TryConvertDouble(
                latitudeValue,
                out double latitude))
        {
            reason =
                "latitude could not be converted to a number";

            return false;
        }

        if (!TryConvertDouble(
                longitudeValue,
                out double longitude))
        {
            reason =
                "longitude could not be converted to a number";

            return false;
        }

        if (!double.IsFinite(
                latitude) ||
            !double.IsFinite(
                longitude))
        {
            reason =
                "latitude/longitude is not finite";

            return false;
        }

        GeoCoordinate candidate =
            new(
                latitude,
                longitude);

        if (!candidate.IsValid)
        {
            reason =
                $"coordinate is outside WGS84 bounds: " +
                $"lat={latitude}, lon={longitude}";

            return false;
        }

        /*
         * 0,0 is technically valid WGS84 but is not a plausible Marikina
         * evacuation-center coordinate. Treat the common uninitialized value
         * as unavailable rather than accidentally routing to the Gulf of
         * Guinea.
         */
        if (Math.Abs(
                latitude) <
                0.000001 &&
            Math.Abs(
                longitude) <
                0.000001)
        {
            reason =
                "coordinate is the uninitialized 0,0 value";

            return false;
        }

        coordinate =
            candidate;

        return true;
    }

    private static bool TryConvertDouble(
        object? value,
        out double result)
    {
        result =
            0.0;

        if (value is null)
        {
            return false;
        }

        try
        {
            /*
             * Convert.ToDouble(object, provider) tolerates common numeric
             * model property types (double, float, decimal, int, nullable
             * values after boxing) without coupling this bridge to the exact
             * database model storage type.
             */
            result =
                Convert.ToDouble(
                    value,
                    CultureInfo.InvariantCulture);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
