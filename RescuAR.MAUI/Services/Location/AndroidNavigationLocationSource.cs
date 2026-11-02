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
    private long gpsSamples, networkSamples, fusedSamples, rejectedSamples;
    private LocationReading? gps, network, fused;

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
                    best = LocationAcquisitionPolicy.Select(gps, network, now, fused);
                }
                if (LocationAcquisitionPolicy.CanComplete(best, now,
                    TimeSpan.FromMilliseconds(Environment.TickCount64 - started))) return best;
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
            lock (sync)
            {
                var result = LocationAcquisitionPolicy.Select(gps, network, NavigationClock.UtcNow, fused);
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
                    best = AStarLocationAcquisitionPolicy.Select(gps, network, best, now, newerThan, fused);
                }
                var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - started);
                if (AStarLocationAcquisitionPolicy.CanComplete(best, now, newerThan, elapsed) ||
                    elapsed >= AStarLocationAcquisitionPolicy.MaximumWait)
                {
                    AndroidLog.Info("RescuAR-GPS", $"NAV_LOCATION_ACQUISITION selected={best?.Provider ?? "none"}; " +
                        $"accuracy={best?.AccuracyMeters:F2}m; ageMs={(now - best?.ObservationTime)?.TotalMilliseconds:F0}; " +
                        $"elapsedMs={elapsed.TotalMilliseconds:F0}; newerThan={newerThan:O}; " +
                        $"gpsCallbacks={gpsSamples}; networkCallbacks={networkSamples}; fusedCallbacks={fusedSamples}; rejectedCallbacks={rejectedSamples}; " +
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
            var requestedProviders = new List<string> { LocationManager.GpsProvider, LocationManager.NetworkProvider };
            if (OperatingSystem.IsAndroidVersionAtLeast(31) && precisePermission &&
                manager.AllProviders.Contains(LocationManager.FusedProvider)) requestedProviders.Add(LocationManager.FusedProvider);
            foreach (string provider in requestedProviders)
            {
                if (provider == LocationManager.GpsProvider && !precisePermission) continue;
                if (!manager.IsProviderEnabled(provider)) continue;
                // Zero minimum distance is deliberate: stationary accuracy
                // improvements and slow pedestrian steps must still be delivered.
                try
                {
                    manager.RequestLocationUpdates(provider, 1000, 0, this, Looper.MainLooper);
                    providers++;
                }
                catch (Java.Lang.IllegalArgumentException) when (provider == "fused")
                {
                    AndroidLog.Info("RescuAR-GPS", "NAV_LOCATION_SOURCE optional fused provider unavailable; GPS/network retained.");
                }
            }
            if (providers == 0) throw new FeatureNotEnabledException("Android location providers are disabled.");
            listening = true;
            AndroidLog.Info("RescuAR-GPS", $"NAV_LOCATION_SOURCE started; precisePermission={precisePermission}; providers={providers}; intervalMs=1000; minimumDistance=0; selection=REPORTED_ACCURACY; cloud=False.");
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
        gps = network = fused = null;
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
            if (reading.Provider == LocationManager.GpsProvider) gpsSamples++;
            else if (reading.Provider == LocationManager.NetworkProvider) networkSamples++;
            else if (reading.Provider == "fused") fusedSamples++;
            if (!LocationAcquisitionPolicy.IsCurrent(reading, NavigationClock.UtcNow))
            {
                rejectedSamples++;
                return;
            }
            if (reading.Provider == LocationManager.GpsProvider)
            {
                if (location.ElapsedRealtimeNanos > (gps?.AcquisitionElapsedRealtimeNanoseconds ?? 0)) gps = reading;
            }
            else if (reading.Provider == LocationManager.NetworkProvider)
            {
                if (location.ElapsedRealtimeNanos > (network?.AcquisitionElapsedRealtimeNanoseconds ?? 0)) network = reading;
            }
            else if (reading.Provider == "fused" && location.ElapsedRealtimeNanos > (fused?.AcquisitionElapsedRealtimeNanoseconds ?? 0))
                fused = reading;
        }
    }

    public void OnProviderDisabled(string provider)
    {
        lock (sync)
        {
            providersDirty = true;
            if (provider == LocationManager.GpsProvider) gps = null;
            if (provider == LocationManager.NetworkProvider) network = null;
            if (provider == "fused") fused = null;
        }
    }
    public void OnProviderEnabled(string provider) { lock (sync) providersDirty = true; }
    public void OnStatusChanged(string? provider, Availability status, Bundle? extras) { }
}
#endif
