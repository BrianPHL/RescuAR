using Microsoft.Maui.Networking;
using RescuAR.App.Models;
using RescuAR.App.Services.Evacuation;
using RescuAR.App.Services.Storage;
using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.State;
using RescuAR.Navigation.Timing;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private static readonly TimeSpan EmergencyShelterSearchBudget = TimeSpan.FromSeconds(20);
    private const double MaximumEmergencyOriginChangeMeters = 20;
    private CancellationTokenSource? emergencyShelterSearch;
    private PreparedEmergencyRoute? preparedEmergencyRoute;

    private sealed record PreparedEmergencyRoute(long DestinationVersion, GeoCoordinate Destination,
        GeoCoordinate Origin, RouteResult Route, DateTimeOffset PlannedAt);

    private void CancelEmergencyShelterSearch() => emergencyShelterSearch?.Cancel();

    private void ApplyPreparedEmergencyRoute(NavigationDestinationBridge.DestinationSnapshot destination)
    {
        var prepared = preparedEmergencyRoute;
        preparedEmergencyRoute = null;
        if (!destination.IsAvailable || prepared is null ||
            prepared.DestinationVersion != destination.Version || prepared.Destination != destination.Coordinate)
            return;

        pendingInitialRoute = prepared.Route;
        pendingInitialOrigin = prepared.Origin;
        pendingInitialDestination = prepared.Destination;
        pendingInitialRouteAt = prepared.PlannedAt;
    }

    private async Task<bool> SelectEmergencyShelterAsync(DisasterAdvisory advisory,
        NavigationDestinationBridge.DestinationSnapshot unselected)
    {
        if (cameraDirectoryPage is not { } page || cameraMapWindowStopped ||
            _selectedShelterFor2DMap is not null || mapCenterSearchOverlay.IsVisible)
            return false;

        var startedView = currentCameraModuleView;
        using var search = CancellationTokenSource.CreateLinkedTokenSource(page.Token);
        var token = search.Token;
        search.CancelAfter(EmergencyShelterSearchBudget);
        emergencyShelterSearch = search;

        bool StillChoosing() => pageIsVisible && !cameraMapWindowStopped && !safeZoneConfirmed &&
            ReferenceEquals(cameraDirectoryPage, page) &&
            ReferenceEquals(lastEmergencyAdvisoryForStatus, advisory) &&
            currentCameraModuleView == startedView && !mapCenterSearchOverlay.IsVisible &&
            _selectedShelterFor2DMap is null && NavigationDestinationBridge.Current == unselected;

        bool StillOwnsSearch()
        {
            token.ThrowIfCancellationRequested();
            return StillChoosing();
        }

        async Task<bool> OpenChooserAsync(string message)
        {
            if (!StillChoosing()) return false;
            await DisplayAlert("Choose a shelter", message, "OK");
            if (!StillChoosing()) return false;
            ApplyCameraModuleView(CameraModuleViewMode.Map2D, "emergency shelter selection requires review");
            OnOpenCenterSearchOverlayClicked(null, EventArgs.Empty);
            return false;
        }

        try
        {
            var directory = EvacuationDirectoryRuntime.Current;
            var snapshot = directory.Current;
            if (!HasCurrentEmergencyDirectory(snapshot))
                snapshot = await directory.RefreshAsync(force: true, token: token);

            if (!StillOwnsSearch()) return false;
            if (!HasCurrentEmergencyDirectory(snapshot))
                return await OpenChooserAsync("Current shelter availability could not be verified. Check the directory status and choose a shelter.");

            var shelters = snapshot.Centers.Select(ToEmergencyShelter).ToArray();
            if (!shelters.Any(EmergencyShelterSelection.IsEligible))
                return await OpenChooserAsync("The current directory has no open shelter with known available capacity. Review the listed shelter status.");

            if (!await _locationService.EnsurePermissionAsync(token))
            {
                return StillOwnsSearch()
                    ? await OpenChooserAsync("Location access is needed to choose a shelter automatically. You can review the shelter directory.")
                    : false;
            }

            if (!StillOwnsSearch()) return false;
            var hazards = await _hazardReroutingService.GetRouteHazardsAsync(
                Connectivity.Current.NetworkAccess == NetworkAccess.Internet, token);
            if (!StillOwnsSearch()) return false;

            var (reading, _, _) = await ResolveRouteStartupLocationAsync(token);
            if (!StillOwnsSearch()) return false;
            if (reading is null || !IsCurrentRouteStartupLocationAcceptable(reading))
                return await OpenChooserAsync("A recent, accurate location was unavailable. You can review the shelter directory.");

            foreach (var shelter in EmergencyShelterSelection.NearbyEligible(shelters, reading.Coordinate))
            {
                if (!StillOwnsSearch()) return false;
                if (!IsCurrentRouteStartupLocationAcceptable(reading))
                {
                    (reading, _, _) = await ResolveRouteStartupLocationAsync(token);
                    if (!StillOwnsSearch()) return false;
                    if (reading is null || !IsCurrentRouteStartupLocationAcceptable(reading))
                        break;
                }

                LocationReading routeReading = reading;
                var route = await _mldArIntegrationService.RequestRouteWithDeferredRoadApproachAsync(
                    routeReading.Coordinate, shelter.Coordinate,
                    async requestToken =>
                    {
                        var graph = await GetArrivalGraphSafelyAsync().WaitAsync(requestToken);
                        return graph is null ? null : await Task.Run(
                            () => FindRoadAccess(routeReading, graph, shelter.Coordinate, requestToken), requestToken);
                    }, token, hazards);

                if (!StillOwnsSearch()) return false;
                if (route is null) continue;

                if (!IsCurrentRouteStartupLocationAcceptable(reading))
                {
                    (reading, _, _) = await ResolveRouteStartupLocationAsync(token);
                    if (!StillOwnsSearch()) return false;
                    if (reading is null || !IsCurrentRouteStartupLocationAcceptable(reading))
                        break;
                    if (reading.Coordinate.DistanceTo(routeReading.Coordinate) > MaximumEmergencyOriginChangeMeters)
                        continue;
                }

                snapshot = directory.Current;
                if (!HasCurrentEmergencyDirectory(snapshot)) break;
                var current = snapshot.Centers.FirstOrDefault(center => center.Id == shelter.Id &&
                    new GeoCoordinate(center.Latitude, center.Longitude) == shelter.Coordinate);
                hazards = _hazardReroutingService.GetRouteHazards();
                if (current is null || !EmergencyShelterSelection.IsEligible(ToEmergencyShelter(current)) ||
                    !EmergencyShelterSelection.IsRouteUsable(route, reading.Coordinate, shelter.Coordinate, hazards))
                    continue;

                if (!StillOwnsSearch()) return false;
                long destinationVersion = unselected.Version + 1;
                preparedEmergencyRoute = new(destinationVersion, shelter.Coordinate,
                    routeReading.Coordinate, route, NavigationClock.UtcNow);
                if (!NavigationDestinationBridge.TrySetIfUnselected(unselected.Version, current.Name.Trim(), shelter.Coordinate))
                {
                    preparedEmergencyRoute = null;
                    return false;
                }

                if (NavigationDestinationBridge.Current.Version == destinationVersion)
                    ApplyCameraModuleView(CameraModuleViewMode.ArCamera, "validated emergency shelter route");
                return true;
            }

            return await OpenChooserAsync("No open shelter with available capacity and a usable walking route was verified. Check the directory and choose a shelter.");
        }
        catch (OperationCanceledException)
        {
            if (page.IsCancellationRequested || !StillChoosing()) return false;
            return await OpenChooserAsync("A shelter and walking route could not be verified in time. Check the directory and choose a shelter.");
        }
        finally
        {
            if (ReferenceEquals(emergencyShelterSearch, search))
                emergencyShelterSearch = null;
        }
    }

    private static bool HasCurrentEmergencyDirectory(EvacuationDirectorySnapshot snapshot) =>
        EmergencyShelterSelection.HasCurrentDirectory(snapshot.Origin == DirectoryOrigin.Live,
            snapshot.UpdatedAt, DateTimeOffset.UtcNow);

    private static EmergencyShelterSelection.Shelter ToEmergencyShelter(CachedEvacuationCenterItem center) =>
        new(center.Id, center.Name, new GeoCoordinate(center.Latitude, center.Longitude),
            center.Status, center.Capacity, center.HasReportedOccupancy ? center.CurrentEvacuees : null);
}
