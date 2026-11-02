namespace RescuAR.MAUI.Services.Location;

/// <summary>
/// RescuAR's navigation-facing location provider.
/// </summary>
public interface ILocationService
{
    bool IsLocationServiceEnabled { get; }

    Task<bool> EnsurePermissionAsync(
        CancellationToken cancellationToken = default);

    Task<LocationReading?> GetCurrentLocationAsync(
        CancellationToken cancellationToken = default);

    Task<LocationReading?> GetLastKnownLocationAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Optional foreground ownership; no background-location service.</summary>
public interface IForegroundLocationSource
{
    IDisposable? AcquireForegroundUpdates();
}

/// <summary>Optional acquisition of a fresh, unconsumed fix for offline A*.</summary>
public interface IAStarLocationSource
{
    Task<LocationReading?> GetAStarLocationAsync(DateTimeOffset? newerThan,
        CancellationToken cancellationToken = default);
}

/// <summary>Shared bounded acquisition for either navigation provider.</summary>
public interface INavigationLocationSource
{
    Task<LocationReading?> GetNavigationLocationAsync(DateTimeOffset? newerThan,
        CancellationToken cancellationToken = default);
}
