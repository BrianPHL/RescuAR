#if ANDROID
using Android.Content;
using Android.Locations;
using Android.OS;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using RescuAR.Diagnostics;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Timing;
using AndroidLocation = Android.Locations.Location;
using Environment = System.Environment;

namespace RescuAR.MAUI.Services.Location;

/// <summary>One native foreground subscription shared by navigation requests.
/// Collects actual GPS/network updates instead of MAUI's first-within-50m shortcut.</summary>
internal sealed class AndroidNavigationLocationSource : Java.Lang.Object, ILocationListener
{
    private readonly object sync = new();
    private readonly SemaphoreSlim requestGate = new(1, 1);
    private LocationManager? manager;
    private bool listening;
    private bool suspended;
    private bool providersDirty;
    private int foregroundOwners, activeRequests;
    private long gpsSamples, networkSamples, rejectedSamples;
    private LocationReading? gps, network;

    public AndroidNavigationLocationSource() => Platform.ActivityStateChanged += OnActivityStateChanged;

    private void OnActivityStateChanged(object? sender, ActivityStateChangedEventArgs e)
    {
        lock (sync)
        {
            if (e.State is ActivityState.Paused or ActivityState.Stopped)
            {
                suspended = true;
                StopIfUnused(force: true);
            }
            else if (e.State == ActivityState.Resumed) suspended = false;
        }
    }

    public IDisposable AcquireForegroundUpdates()
    {
        lock (sync) foregroundOwners++;
        return new IdempotentReleaseLease(() =>
        {
            lock (sync)
            {
                foregroundOwners--;
                StopIfUnused();
            }
        });
    }

    public async Task<LocationReading?> GetCurrentAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (sync)
            {
                activeRequests++;
                if (suspended) return null;
                Start();
            }
            long started = Environment.TickCount64;
            while (Environment.TickCount64 - started < timeout.TotalMilliseconds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var now = NavigationClock.UtcNow;
                LocationReading? best;
                lock (sync)
                {
                    if (suspended) return null;
                    Start();
                    best = LocationAcquisitionPolicy.Select(gps, network, now);
                }
                if (LocationAcquisitionPolicy.CanComplete(best, now,
                    TimeSpan.FromMilliseconds(Environment.TickCount64 - started))) return best;
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
            lock (sync)
            {
                var result = LocationAcquisitionPolicy.Select(gps, network, NavigationClock.UtcNow);
                AndroidLog.Info("RescuAR-GPS", $"NAV_LOCATION_POLL timeout=True; selected={result?.Provider ?? "none"}; " +
                    $"gpsCallbacks={gpsSamples}; networkCallbacks={networkSamples}; rejectedCallbacks={rejectedSamples}; " +
                    $"gpsAt={gps?.Timestamp:O}; networkAt={network?.Timestamp:O}; owners={foregroundOwners}.");
                return result;
            }
        }
        finally
        {
            lock (sync)
            {
                activeRequests--;
                StopIfUnused();
            }
            requestGate.Release();
        }
    }

    public async Task<LocationReading?> GetNavigationCurrentAsync(DateTimeOffset? newerThan,
        CancellationToken cancellationToken)
    {
        await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (sync)
            {
                activeRequests++;
                if (suspended) return null;
                Start();
            }
            long started = Environment.TickCount64;
            LocationReading? best = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var now = NavigationClock.UtcNow;
                lock (sync)
                {
                    if (suspended) return null;
                    Start();
                    best = AStarLocationAcquisitionPolicy.Select(gps, network, best, now, newerThan);
                }
                var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - started);
                if (AStarLocationAcquisitionPolicy.CanComplete(best, now, newerThan, elapsed) ||
                    elapsed >= AStarLocationAcquisitionPolicy.CollectionWindow)
                {
                    AndroidLog.Info("RescuAR-GPS", $"NAV_LOCATION_ACQUISITION selected={best?.Provider ?? "none"}; " +
                        $"accuracy={best?.AccuracyMeters:F2}m; ageMs={(now - best?.ObservationTime)?.TotalMilliseconds:F0}; " +
                        $"elapsedMs={elapsed.TotalMilliseconds:F0}; newerThan={newerThan:O}; " +
                        "acquisitionTimePreserved=True; coordinatesFiltered=False.");
                    return best;
                }
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (sync)
            {
                activeRequests--;
                StopIfUnused();
            }
            requestGate.Release();
        }
    }

    private void Start()
    {
        if (listening && !providersDirty) return;
        if (listening) { manager?.RemoveUpdates(this); listening = false; }
        providersDirty = false;
        manager ??= Platform.AppContext.GetSystemService(Context.LocationService) as LocationManager;
        if (manager is null) throw new FeatureNotSupportedException("Android location manager unavailable.");
        try
        {
            int providers = 0;
            bool precisePermission = Platform.AppContext.CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation)
                == Android.Content.PM.Permission.Granted;
            foreach (string provider in new[] { LocationManager.GpsProvider, LocationManager.NetworkProvider })
            {
                if (provider == LocationManager.GpsProvider && !precisePermission) continue;
                if (!manager.IsProviderEnabled(provider)) continue;
                // Zero minimum distance is deliberate: stationary accuracy
                // improvements and slow pedestrian steps must still be delivered.
                manager.RequestLocationUpdates(provider, 1000, 0, this, Looper.MainLooper);
                providers++;
            }
            if (providers == 0) throw new FeatureNotEnabledException("Android location providers are disabled.");
            listening = true;
            AndroidLog.Info("RescuAR-GPS", $"NAV_LOCATION_SOURCE started; precisePermission={precisePermission}; intervalMs=1000; minimumDistance=0; selection=REPORTED_ACCURACY; cloud=False.");
        }
        catch
        {
            manager.RemoveUpdates(this);
            throw;
        }
    }

    private void StopIfUnused(bool force = false)
    {
        if (!listening || !force && (foregroundOwners > 0 || activeRequests > 0)) return;
        manager?.RemoveUpdates(this);
        listening = false;
        gps = network = null;
        AndroidLog.Info("RescuAR-GPS", $"NAV_LOCATION_SOURCE stopped; lifecycleSuspend={force}; owners={foregroundOwners}; requests={activeRequests}.");
    }

    public void OnLocationChanged(AndroidLocation location)
    {
        long receivedAt = Environment.TickCount64;
        if (!NavigationClock.TryMapElapsedNanoseconds(location.ElapsedRealtimeNanos,
                SystemClock.ElapsedRealtimeNanos(), receivedAt, out long acquiredAt))
        {
            lock (sync)
            {
                if (!listening) return;
                rejectedSamples++;
                if (rejectedSamples == 1 || rejectedSamples % 30 == 0)
                    AndroidLog.Warn("RescuAR-GPS", $"NAV_LOCATION_CLOCK rejected=True; provider={location.Provider}; reason=INVALID_NATIVE_ACQUISITION; count={rejectedSamples}.");
            }
            return;
        }
        DateTimeOffset timestamp;
        try { timestamp = DateTimeOffset.FromUnixTimeMilliseconds(location.Time); }
        catch (ArgumentOutOfRangeException) { return; }
        var reading = new LocationReading(new GeoCoordinate(location.Latitude, location.Longitude),
            location.HasAccuracy ? location.Accuracy : null,
            location.HasAltitude ? location.Altitude : null,
            location.HasSpeed ? location.Speed : null, location.HasBearing ? location.Bearing : null, timestamp)
        {
            Provider = location.Provider ?? "unavailable",
            CourseAccuracyDegrees = OperatingSystem.IsAndroidVersionAtLeast(26) && location.HasBearingAccuracy
                ? location.BearingAccuracyDegrees : null,
            AcquisitionElapsedRealtimeNanoseconds = location.ElapsedRealtimeNanos,
            AcquisitionMonotonicMilliseconds = acquiredAt
        };
        lock (sync)
        {
            if (!listening) return;
            if (!LocationAcquisitionPolicy.IsCurrent(reading, NavigationClock.UtcNow))
            {
                rejectedSamples++;
                return;
            }
            if (reading.Provider == LocationManager.GpsProvider)
            {
                gpsSamples++;
                if (location.ElapsedRealtimeNanos > (gps?.AcquisitionElapsedRealtimeNanoseconds ?? 0)) gps = reading;
            }
            else if (reading.Provider == LocationManager.NetworkProvider)
            {
                networkSamples++;
                if (location.ElapsedRealtimeNanos > (network?.AcquisitionElapsedRealtimeNanoseconds ?? 0)) network = reading;
            }
        }
    }

    public void OnProviderDisabled(string provider)
    {
        lock (sync)
        {
            providersDirty = true;
            if (provider == LocationManager.GpsProvider) gps = null;
            if (provider == LocationManager.NetworkProvider) network = null;
        }
    }
    public void OnProviderEnabled(string provider) { lock (sync) providersDirty = true; }
    public void OnStatusChanged(string? provider, Availability status, Bundle? extras) { }
}
#endif
