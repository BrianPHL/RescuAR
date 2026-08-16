#if ANDROID
using Android.Content;
using Android.Locations;
#endif

using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using RescuAR.Navigation.Models;

namespace RescuAR.MAUI.Services.Location;

/// <summary>
/// Foreground location provider based on the built-in .NET MAUI geolocation
/// APIs. No additional NuGet package is required.
///
/// This milestone intentionally provides location samples only. Continuous
/// fusion, map matching, PDR, rerouting, and background tracking are separate
/// navigation milestones.
/// </summary>
public sealed class MauiLocationService : ILocationService
{
    private const string LogTag =
        "RescuAR-GPS";

    private static readonly TimeSpan CurrentLocationTimeout =
        TimeSpan.FromSeconds(15);

    public bool IsLocationServiceEnabled
    {
        get
        {
#if ANDROID
            Context? context =
                Platform.AppContext;

            if (context is null)
            {
                return false;
            }

            LocationManager? locationManager =
                context.GetSystemService(
                    Context.LocationService)
                as LocationManager;

            if (locationManager is null)
            {
                return false;
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(28))
            {
                return locationManager.IsLocationEnabled;
            }

            return
                locationManager.IsProviderEnabled(
                    LocationManager.GpsProvider)
                ||
                locationManager.IsProviderEnabled(
                    LocationManager.NetworkProvider);
#else
        return true;
#endif
        }
    }

    public async Task<bool> EnsurePermissionAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PermissionStatus status =
            await Permissions.CheckStatusAsync<
                Permissions.LocationWhenInUse>();

        if (status ==
            PermissionStatus.Granted)
        {
            WriteLog(
                "Location permission already granted.");

            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();

        status =
            await Permissions.RequestAsync<
                Permissions.LocationWhenInUse>();

        bool granted =
            status ==
            PermissionStatus.Granted;

        WriteLog(
            $"Location permission result = {status}");

        return granted;
    }

    public async Task<LocationReading?> GetCurrentLocationAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await EnsurePermissionAsync(
                cancellationToken))
        {
            WriteLog(
                "Current location unavailable because permission was not granted.");

            return null;
        }

        if (!IsLocationServiceEnabled)
        {
            WriteLog(
                "Current location unavailable because Android location services are disabled.");

            return null;
        }

        GeolocationRequest request =
            new(
                GeolocationAccuracy.Best,
                CurrentLocationTimeout);

        try
        {
            Microsoft.Maui.Devices.Sensors.Location? location =
                await Geolocation.Default.GetLocationAsync(
                    request,
                    cancellationToken);

            return ToReadingAndLog(
                "CURRENT",
                location);
        }
        catch (FeatureNotEnabledException)
        {
            WriteLog(
                "Location services are disabled.");

            return null;
        }
        catch (PermissionException)
        {
            WriteLog(
                "Location permission is unavailable.");

            return null;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            WriteLog(
                "Current-location request timed out.");

            return null;
        }
    }

    public async Task<LocationReading?> GetLastKnownLocationAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await EnsurePermissionAsync(
                cancellationToken))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            Microsoft.Maui.Devices.Sensors.Location? location =
                await Geolocation.Default.GetLastKnownLocationAsync();

            return ToReadingAndLog(
                "LAST_KNOWN",
                location);
        }
        catch (FeatureNotEnabledException)
        {
            WriteLog(
                "Location services are disabled.");

            return null;
        }
        catch (PermissionException)
        {
            WriteLog(
                "Location permission is unavailable.");

            return null;
        }
    }

    private static LocationReading? ToReadingAndLog(
        string source,
        Microsoft.Maui.Devices.Sensors.Location? location)
    {
        if (location is null)
        {
            WriteLog(
                $"{source}: no location sample returned.");

            return null;
        }

        GeoCoordinate coordinate =
            new(
                location.Latitude,
                location.Longitude);

        if (!coordinate.IsValid)
        {
            WriteLog(
                $"{source}: invalid coordinate returned: " +
                $"lat={location.Latitude}, lon={location.Longitude}");

            return null;
        }

        /*
         * MAUI Location.Timestamp is a DateTimeOffset. Accuracy is horizontal
         * accuracy in meters when provided by the platform.
         */
        LocationReading reading =
            new(
                coordinate,
                location.Accuracy,
                NormalizeAltitude(
                    location.Altitude),
                location.Speed,
                location.Course,
                location.Timestamp);

        WriteLog(
            $"{source}: " +
            $"lat={reading.Coordinate.Latitude:F7}, " +
            $"lon={reading.Coordinate.Longitude:F7}, " +
            $"accuracy={FormatNullable(reading.AccuracyMeters)} m, " +
            $"altitude={FormatNullable(reading.AltitudeMeters)} m, " +
            $"speed={FormatNullable(reading.SpeedMetersPerSecond)} m/s, " +
            $"course={FormatNullable(reading.CourseDegrees)} deg, " +
            $"timestamp={reading.Timestamp:O}");

        return reading;
    }

    private static double? NormalizeAltitude(
        double? altitude)
    {
        /*
         * Android devices can report 0.0 when altitude is unavailable.
         * Do not treat zero as verified sea-level altitude.
         */
        if (!altitude.HasValue ||
            altitude.Value ==
            0.0)
        {
            return null;
        }

        return altitude;
    }

    private static string FormatNullable(
        double? value)
    {
        return value.HasValue
            ? value.Value.ToString("F2")
            : "<unknown>";
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
