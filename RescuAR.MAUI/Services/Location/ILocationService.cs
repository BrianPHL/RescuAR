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
