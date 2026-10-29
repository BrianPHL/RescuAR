#if ANDROID
using Android.Content;
using Android.Locations;
#endif

using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using RescuAR.Diagnostics;
using RescuAR.Navigation.Models;

namespace RescuAR.MAUI.Services.Location;

/// <summary>
/// Foreground location adapter. Android collects GPS/network observations
/// directly so MAUI's first-within-50m completion cannot end acquisition early.
/// No additional NuGet package is required.
///
/// Continuous location in the current moving-window milestone is implemented
/// as a foreground polling loop owned by CameraPage. This service remains the
/// single adapter that converts MAUI Location into RescuAR LocationReading.
/// </summary>
public sealed class MauiLocationService : ILocationService, IForegroundLocationSource
{
#if ANDROID
    private readonly AndroidNavigationLocationSource navigationLocations = new();
#endif
    public IDisposable? AcquireForegroundUpdates()
    {
#if ANDROID
        return navigationLocations.AcquireForegroundUpdates();
#else
        return null;
#endif
    }
    private readonly ReliableLocationCache reliableLocations = new();
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

#if !ANDROID
        GeolocationRequest request =
            new(
                GeolocationAccuracy.Best,
                CurrentLocationTimeout);
#endif

        try
        {
#if ANDROID
            var reading = await navigationLocations.GetCurrentAsync(CurrentLocationTimeout, cancellationToken);
            if (reading is not null)
            {
                reliableLocations.Observe(reading);
                WriteLog($"CURRENT: provider={reading.Provider}; accuracy={FormatNullable(reading.AccuracyMeters)} m; " +
                    $"courseAccuracy={FormatNullable(reading.CourseAccuracyDegrees)} deg; timestamp={reading.Timestamp:O}; acquisitionTimePreserved=True.");
            }
            return reading;
#else
            Microsoft.Maui.Devices.Sensors.Location? location =
                await Geolocation.Default.GetLocationAsync(
                    request,
                    cancellationToken);

            return ToReadingAndLog(
                "CURRENT",
                location);
#endif
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
#if ANDROID
        catch (Java.Lang.SecurityException)
        {
            WriteLog("Location permission changed during Android acquisition.");
            return null;
        }
#endif
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

            ToReadingAndLog(
                "LAST_KNOWN",
                location);
            return GetRetainedLocation();
        }
        catch (FeatureNotEnabledException)
        {
            WriteLog(
                "Location services are disabled.");

            return GetRetainedLocation();
        }
        catch (PermissionException)
        {
            WriteLog(
                "Location permission is unavailable.");

            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException &&
            exception is not OutOfMemoryException)
        {
            WriteLog("Cached device location lookup failed: " +
                DiagnosticPrivacyPolicy.FormatException(exception));
            return GetRetainedLocation();
        }
    }

    private LocationReading? GetRetainedLocation()
    {
        var reading = reliableLocations.Get();
        WriteLog(reading is null ? "LAST_RELIABLE: no qualified recent location retained."
            : $"LAST_RELIABLE: accuracy={reading.AccuracyMeters:F1} m, " +
              $"age={(DateTimeOffset.UtcNow - reading.Timestamp).TotalSeconds:F1}s, " +
              $"timestamp={reading.Timestamp:O}; original acquisition time retained.");
        return reading;
    }

    private LocationReading? ToReadingAndLog(
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
                $"coordinate={DiagnosticPrivacyPolicy.FormatCoordinate(location.Latitude, location.Longitude)}");

            return null;
        }

        LocationReading reading =
            new(
                coordinate,
                location.Accuracy,
                NormalizeAltitude(
                    location.Altitude),
                location.Speed,
                location.Course,
                location.Timestamp);
        reliableLocations.Observe(reading);

        WriteLog(
            $"{source}: " +
            $"coordinate={DiagnosticPrivacyPolicy.FormatCoordinate(reading.Coordinate.Latitude, reading.Coordinate.Longitude)}, " +
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
        /*
         * Device-visible diagnostics intentionally use Android.Util.Log.
         * Do not replace this with Console.WriteLine/Debug.WriteLine.
         */
        Android.Util.Log.Debug(
            LogTag,
            message);
#endif
    }
}
