using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using NetTopologySuite.Geometries;
using RescuAR.MAUI.Services.Location;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.State;
using Shelter = RescuAR.App.Models.EvacuationCenter;
using MapColor = Mapsui.Styles.Color;
using MapBrush = Mapsui.Styles.Brush;
using MapPen = Mapsui.Styles.Pen;

namespace RescuAR.App.Views.Camera;

public sealed class ShelterSearchDisplayItem
{
    public required Shelter Center { get; init; }
    public string Name => Center.Name;
    public string DetailsText => CameraMapPresentationPolicy.DirectoryStatus(Center.Status);
    public required string DistanceText { get; init; }
}

public sealed class RouteDirectionStepItem
{
    public required string IconSource { get; init; }
    public required string InstructionText { get; init; }
    public required string SubtitleText { get; init; }
    public required string DistanceText { get; init; }
    public Color BackgroundColor => Color.FromArgb("#E6F4F2");
}

public partial class CameraPage
{
    private readonly CameraMapViewportPolicy cameraMapViewport = new();
    private readonly RouteDirectionsService cameraDirections = new();
    private readonly MemoryLayer cameraMapRouteLayer = new() { Name = "Camera2DRouteLayer" };
    private readonly MemoryLayer cameraMapUserLayer = new() { Name = "Camera2DUserLocation" };
    private readonly MemoryLayer cameraMapPlacesLayer = new() { Name = "Camera2DDirectoryAndDestination" };
    private OfflineMapLayerController? cameraMapDetails;
    private CancellationTokenSource? cameraMapPage;
    private CancellationTokenSource? cameraMapLocationRequest;
    private Window? cameraMapWindow;
    private bool cameraMapWindowStopped, cameraMapBasicReady, cameraMapPermissionChecked, cameraMapPermissionGranted;
    private bool cameraMapPlannerOpen;
    private long cameraMapGeneration;
    private long cameraMapPlacesTrip = -1;
    private RouteResult? cameraMapRenderedRoute, cameraMapDirectionsRoute;
    private LocationReading? cameraMapBrowsingReading, cameraMapRenderedLocation;
    private LocationReading? cameraMapSearchLocation;
    private Shelter? cameraMapSelectedShelter;
    private string cameraMapBasicNotice = "";
    private string cameraMapCoverage = "";
    private string cameraMapLocationNotice = "Waiting for a current validated GPS fix.";

    private void InitializeCameraMapPresentation()
    {
        var map = new Mapsui.Map { CRS = "EPSG:3857" };
        var online = Mapsui.Tiling.OpenStreetMap.CreateTileLayer();
        online.Name = "Camera2DOnlineBase"; online.Enabled = false;
        map.Layers.Add(online);
        map.Layers.Add(new ILayer[] { cameraMapPlacesLayer, cameraMapRouteLayer, cameraMapUserLayer });
        foreach (var layer in map.Layers) layer.Enabled = false;
        var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(121.1029, 14.6507);
        map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), 9.5546); // Browsing center only; never a user pin or route origin.
        CameraMapControl.Map = map;
        cameraMapDetails = new(message => { cameraMapCoverage = message; UpdateCameraMapCoverage(); });
    }

    private void ObserveCameraMapWindow()
    {
        if (cameraMapWindow == Window) return;
        UnobserveCameraMapWindow(); cameraMapWindow = Window; cameraMapWindowStopped = false;
        if (cameraMapWindow is null) return;
        cameraMapWindow.Stopped += CameraMapWindowStopped;
        cameraMapWindow.Resumed += CameraMapWindowResumed;
    }
    private void UnobserveCameraMapWindow()
    {
        if (cameraMapWindow is null) return;
        cameraMapWindow.Stopped -= CameraMapWindowStopped;
        cameraMapWindow.Resumed -= CameraMapWindowResumed;
        cameraMapWindow = null;
    }
    private void CameraMapWindowStopped(object? sender, EventArgs args) { cameraMapWindowStopped = true; StopCameraMapDisplay(); }
    private void CameraMapWindowResumed(object? sender, EventArgs args) { cameraMapWindowStopped = false; RefreshCameraMapPresentation(); }

    private void StopCameraMapDisplay()
    {
        cameraMapGeneration++;
        cameraMapPage?.Cancel(); cameraMapPage?.Dispose(); cameraMapPage = null;
        cameraMapLocationRequest?.Cancel();
        cameraMapDetails?.Deactivate();
        cameraMapBrowsingReading = null; cameraMapRenderedLocation = null;
        cameraMapPermissionChecked = cameraMapPermissionGranted = false;
        foreach (var layer in CameraMapControl.Map.Layers) layer.Enabled = false;
        cameraMapUserLayer.Features = Array.Empty<GeometryFeature>();
        cameraMapUserLayer.DataHasChanged();
        mapCenterSearchOverlay.IsVisible = false; routeDirectionsSheet.IsVisible = false; routeCalculatingOverlay.IsVisible = false;
    }

    private bool IsCameraMapRequest(long generation, CancellationToken token) =>
        !token.IsCancellationRequested && generation == cameraMapGeneration && pageIsVisible && !cameraMapWindowStopped &&
        currentCameraModuleView == CameraModuleViewMode.Map2D;

    private async Task PrepareCameraMapDisplayAsync(long generation, CancellationToken token)
    {
        IReadOnlyList<ILayer>? loaded = null;
        try
        {
            if (!cameraMapBasicReady)
            {
                try
                {
                    loaded = await DetailedOfflineMapService.CreateBasicLayersAsync(token);
                    if (!IsCameraMapRequest(generation, token)) return;
                    CameraMapControl.Map.Layers.Insert(1, loaded); loaded = null; cameraMapBasicReady = true; cameraMapBasicNotice = "";
                }
                catch (OperationCanceledException) { return; }
                catch { if (IsCameraMapRequest(generation, token)) cameraMapBasicNotice = "Basic roads/points unavailable. "; }
            }
            if (!IsCameraMapRequest(generation, token)) return;
            await cameraMapDetails!.ActivateAsync(CameraMapControl.Map);
        }
        finally { if (loaded is not null) foreach (var layer in loaded) layer.Dispose(); }
    }

    private void UpdateCameraMapCoverage() => mapModeStatusLabel.Text = cameraMapBasicNotice + cameraMapCoverage;

    private void RefreshCameraMapPresentation()
    {
        if (cameraMapDetails is null) return;
        if (Dispatcher.IsDispatchRequired) { Dispatcher.Dispatch(RefreshCameraMapPresentation); return; }
        bool mapMode = currentCameraModuleView == CameraModuleViewMode.Map2D;
        SetCameraSubTab(subTabArButton, subTabArLabel, currentCameraModuleView == CameraModuleViewMode.ArCamera);
        SetCameraSubTab(subTabMapButton, subTabMapLabel, mapMode);
        SetCameraSubTab(subTabFloodButton, subTabFloodLabel, currentCameraModuleView == CameraModuleViewMode.FloodDepth);
        if (routeDirectionsSheet.IsVisible) PopulateCameraDirections();
        bool visible = mapMode && pageIsVisible && !cameraMapWindowStopped;
        if (!visible)
        {
            if (cameraMapPage is not null) StopCameraMapDisplay();
            mapRouteSearchCard.IsVisible = false; mapRouteSummaryPanel.IsVisible = false;
            return;
        }
        if (cameraMapPage is null)
        {
            cameraMapPage = new(); cameraMapGeneration++;
            foreach (var layer in CameraMapControl.Map.Layers) layer.Enabled = true;
            _ = PrepareCameraMapDisplayAsync(cameraMapGeneration, cameraMapPage.Token);
        }

        var destination = NavigationDestinationBridge.Current;
        RouteResult? route; RouteProgressTracker.ProgressSnapshot progress; LocationReading? reading = null; bool held;
        lock (routeProgressFusionSync)
        {
            route = CameraMapPresentationPolicy.ForTrip(activeRoute, activeDestinationVersion, destination.Version, destination.IsAvailable);
            progress = _routeProgressTracker.Current;
            held = destination.IsAvailable && (positionObservationHeld || _gpsPdrFusionPolicy.IsRouteIdentitySuspended);
            if (latestGpsCoordinateForRouting is { } coordinate && latestGpsTimestampForRouting is { } timestamp)
                reading = new(coordinate, latestGpsAccuracyForRouting, null, null, null, timestamp);
        }
        var now = DateTimeOffset.UtcNow;
        // Browsing acquisition never writes route origins/progress. A trip uses only dev's canonical location stream.
        if (!destination.IsAvailable) reading = cameraMapBrowsingReading;
        if (!CameraMapPresentationPolicy.CanShowCurrent(reading, now, held)) reading = null;
        mapFromLocationLabel.FormattedText = null;
        mapFromLocationLabel.Text = reading is null ? "Current location unknown" : $"Current GPS · ±{reading.AccuracyMeters:0} m";
        mapLocationNoticeLabel.Text = reading is null ? (held ? "Navigation position is held; location and nearby distances are unknown." : cameraMapLocationNotice) : "Current validated GPS; shelter distances are straight-line estimates.";
        mapDestinationSelectedLabel.Text = cameraMapSelectedShelter is null ? "Select evacuation center…" :
            cameraMapSelectedShelter.Name + " · " + CameraMapPresentationPolicy.DistanceTo(new(cameraMapSelectedShelter.Latitude, cameraMapSelectedShelter.Longitude), reading, now);

        bool hasTrip = destination.IsAvailable && !safeZoneConfirmed;
        mapRouteSearchCard.IsVisible = !emergencyAdvisoryVisible && !safeZoneConfirmed && (cameraMapPlannerOpen || !hasTrip);
        mapRouteSummaryPanel.IsVisible = !emergencyAdvisoryVisible && hasTrip && !cameraMapPlannerOpen;
        mapReturnToRouteButton.IsVisible = hasTrip && cameraMapPlannerOpen;
        routeCalculatingOverlay.IsVisible = routeRequestInProgress && !emergencyAdvisoryVisible && !safeZoneConfirmed;
        mapCardStartRoutingButton.IsEnabled = !routeRequestInProgress;
        mapRetryRouteButton.IsVisible = hasTrip && route is null && !routeRequestInProgress;
        mapRouteDestinationNameLabel.Text = "To: " + destination.Name;
        mapRouteStatsLabel.Text = route is null ? (routeRequestInProgress ? "Preparing route…" : routeStartupFailureMessage ?? "No accepted route. Check GPS/connection, then Retry route.") :
            $"{route.TotalDistanceMeters:0} m route · {CameraMapPresentationPolicy.WalkEstimate(route.TotalDistanceMeters)}";

        if (destination.Version != cameraMapPlacesTrip)
        {
            cameraMapPlacesTrip = destination.Version;
            cameraMapPlacesLayer.Features = BuildCameraMapPlaces(destination);
            cameraMapPlacesLayer.DataHasChanged();
        }
        if (!ReferenceEquals(route, cameraMapRenderedRoute))
        {
            cameraMapRenderedRoute = route;
            cameraMapRouteLayer.Features = route is null ? Array.Empty<GeometryFeature>() : BuildCameraMapRoute(route);
            cameraMapRouteLayer.DataHasChanged();
            if (route is not null && cameraMapViewport.ShouldFrameRoute(destination.Version, route)) FrameCameraMapRoute(route);
            if (routeDirectionsSheet.IsVisible) PopulateCameraDirections();
        }
        if (reading != cameraMapRenderedLocation)
        {
            cameraMapRenderedLocation = reading;
            cameraMapUserLayer.Features = reading is null ? Array.Empty<GeometryFeature>() : new[] { CameraMapPin(reading.Coordinate, "Current validated GPS", MapColor.FromString("#1A73E8"), 0.8) };
            cameraMapUserLayer.DataHasChanged();
        }
        if (mapCenterSearchOverlay.IsVisible && ((reading is null) != (cameraMapSearchLocation is null) ||
            (reading is not null && cameraMapSearchLocation is not null && reading.Coordinate.DistanceTo(cameraMapSearchLocation.Coordinate) > 20)))
            PopulateCameraShelters();
        var guidance = route is null ? PedestrianTurnGuidanceService.TurnGuidanceSnapshot.Unavailable :
            CameraMapPresentationPolicy.Guidance(route, progress, held || reading is null || lastGpsConfidence < GpsPdrFusionPolicy.GpsConfidence.Medium || lastOffRouteCandidate || dynamicRerouteInProgress);
        mapCurrentGuidanceLabel.Text = routeHazardBlocksGuidance ? routeHazardNotice ?? "Reported hazard affects this route." :
            lastSafeZoneDecision.IsCandidate ? "Near destination — follow the existing arrival confirmation prompt." :
            guidance.IsAvailable ? RouteDirectionsService.Text(guidance.Instruction) + (double.IsFinite(guidance.DistanceToTurnMeters) ? $" in ~{guidance.DistanceToTurnMeters:0} m" : "") :
            route is null ? "Guidance begins after a route is accepted." : "Position uncertain · turn distances held.";
        mapCurrentDistanceLabel.Text = guidance.IsAvailable && !routeHazardBlocksGuidance ? $"~{guidance.RemainingRouteMeters:0} m remaining · approximate" : "";
        CameraMapControl.Map.RefreshGraphics();
        if (destination.IsAvailable) cameraMapLocationRequest?.Cancel();
        else _ = ReadCameraMapBrowsingLocationAsync(cameraMapGeneration, cameraMapPage.Token);
    }

    private static void SetCameraSubTab(Border button, Label label, bool active)
    {
        button.BackgroundColor = active ? Color.FromArgb("#0A8491") : Colors.Transparent;
        label.TextColor = active ? Colors.White : Color.FromArgb("#64748B");
    }

    private async Task ReadCameraMapBrowsingLocationAsync(long generation, CancellationToken pageToken)
    {
        if (cameraMapLocationRequest is not null || (cameraMapPermissionChecked && !cameraMapPermissionGranted)) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(pageToken);
        cameraMapLocationRequest = request; long trip = NavigationDestinationBridge.Current.Version;
        try
        {
            if (!cameraMapPermissionChecked)
            {
                bool allowed = await _locationService.EnsurePermissionAsync(request.Token);
                if (!IsCameraMapRequest(generation, request.Token)) return;
                cameraMapPermissionChecked = true; cameraMapPermissionGranted = allowed;
                if (!allowed) { cameraMapLocationNotice = "Location permission was not granted; browsing only."; return; }
            }
            var reading = await _locationService.GetCurrentLocationAsync(request.Token);
            if (!IsCameraMapRequest(generation, request.Token) || NavigationDestinationBridge.Current.Version != trip) return;
            cameraMapBrowsingReading = CameraMapPresentationPolicy.CanShowCurrent(reading, DateTimeOffset.UtcNow, false) ? reading : null;
            cameraMapLocationNotice = cameraMapBrowsingReading is null ? "No current validated GPS fix. The map can still be browsed." : "";
        }
        catch (OperationCanceledException) { }
        catch { if (IsCameraMapRequest(generation, pageToken)) cameraMapLocationNotice = "Current GPS unavailable; browsing only."; }
        finally { if (ReferenceEquals(cameraMapLocationRequest, request)) cameraMapLocationRequest = null; }
    }

    private static GeometryFeature CameraMapPin(GeoCoordinate coordinate, string label, MapColor color, double size = 0.7)
    {
        var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(coordinate.Longitude, coordinate.Latitude);
        var feature = new GeometryFeature(new NetTopologySuite.Geometries.Point(x, y));
        feature.Styles.Add(new Mapsui.Styles.SymbolStyle { SymbolType = Mapsui.Styles.SymbolType.Ellipse, SymbolScale = size, Fill = new MapBrush(color), Outline = new MapPen(MapColor.White, 2) });
        feature.Styles.Add(new Mapsui.Styles.LabelStyle { Text = label, ForeColor = MapColor.FromString("#0F172A"), BackColor = new MapBrush(MapColor.White), Offset = new Mapsui.Styles.Offset(0, -18) });
        return feature;
    }
    private static GeometryFeature[] BuildCameraMapPlaces(NavigationDestinationBridge.DestinationSnapshot destination)
    {
        var pins = EvacuationCenterRepository.GetEvacuationCenters().Where(center => CameraMapPresentationPolicy.CanProject(new(center.Latitude, center.Longitude)))
            .Select(center => CameraMapPin(new(center.Latitude, center.Longitude), center.Name + " · directory", MapColor.FromString("#0A8491"))).ToList();
        if (destination.IsAvailable && CameraMapPresentationPolicy.CanProject(destination.Coordinate))
            pins.Add(CameraMapPin(destination.Coordinate, "Destination: " + destination.Name, MapColor.FromString("#EA4335"), 1));
        return pins.ToArray();
    }
    private static GeometryFeature[] BuildCameraMapRoute(RouteResult route)
    {
        var coordinates = route.Points.Select(point =>
        {
            var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(point.Coordinate.Longitude, point.Coordinate.Latitude);
            return new Coordinate(x, y);
        }).ToArray();
        var geometry = new LineString(coordinates);
        var casing = new GeometryFeature(geometry);
        casing.Styles.Add(new Mapsui.Styles.VectorStyle { Line = new MapPen(MapColor.FromString("#1557B0"), 9) });
        var line = new GeometryFeature(geometry.Copy());
        line.Styles.Add(new Mapsui.Styles.VectorStyle { Line = new MapPen(MapColor.FromString("#1A73E8"), 6) });
        return new[] { casing, line, CameraMapPin(route.Points[0].Coordinate, "Route start", MapColor.Gray, 0.6) };
    }
    private void FrameCameraMapRoute(RouteResult route)
    {
        var points = route.Points.Select(point => Mapsui.Projections.SphericalMercator.FromLonLat(point.Coordinate.Longitude, point.Coordinate.Latitude)).ToArray();
        double minX = points.Min(point => point.x), maxX = points.Max(point => point.x), minY = points.Min(point => point.y), maxY = points.Max(point => point.y);
        double width = Math.Max(100, CameraMapControl.Width - 80), height = Math.Max(100, CameraMapControl.Height - 330);
        double resolution = Math.Clamp(Math.Max((maxX - minX) / width, (maxY - minY) / height) * 1.2, 0.6, 200);
        CameraMapControl.Map.Navigator.CenterOnAndZoomTo(new MPoint((minX + maxX) / 2, (minY + maxY) / 2), resolution);
    }

    private void OnOpenCenterSearchOverlayClicked(object? sender, EventArgs args)
    {
        cameraMapPlannerOpen = true; mapCenterSearchOverlay.IsVisible = true;
        centerModalSearchEntry.Text = ""; PopulateCameraShelters();
    }
    private void OnCloseCenterSearchOverlayClicked(object? sender, EventArgs args) => mapCenterSearchOverlay.IsVisible = false;
    private void OnClearCenterSearchClicked(object? sender, EventArgs args) { centerModalSearchEntry.Text = ""; PopulateCameraShelters(); }
    private void OnCenterModalSearchTextChanged(object? sender, TextChangedEventArgs args) => PopulateCameraShelters();
    private void PopulateCameraShelters()
    {
        var now = DateTimeOffset.UtcNow;
        var current = CameraMapPresentationPolicy.CanShowCurrent(cameraMapRenderedLocation, now, false) ? cameraMapRenderedLocation : null;
        cameraMapSearchLocation = current;
        var centers = EvacuationCenterRepository.GetEvacuationCenters().Where(center => center.Name.Contains(centerModalSearchEntry.Text ?? "", StringComparison.OrdinalIgnoreCase));
        centers = current is null ? centers.OrderBy(center => center.Name) : centers.OrderBy(center => current.Coordinate.DistanceTo(new(center.Latitude, center.Longitude)));
        centerSearchResultsView.ItemsSource = centers.Select(center => new ShelterSearchDisplayItem { Center = center,
            DistanceText = CameraMapPresentationPolicy.DistanceTo(new(center.Latitude, center.Longitude), current, now) }).ToArray();
        mapSearchOrderLabel.Text = current is null ? "By name · nearby distances unknown" : "By estimated straight-line distance";
    }
    private void OnShelterSearchResultSelected(object? sender, TappedEventArgs args)
    {
        if (args.Parameter is not ShelterSearchDisplayItem selected) return;
        cameraMapSelectedShelter = selected.Center; cameraMapPlannerOpen = true;
        mapCenterSearchOverlay.IsVisible = false; RefreshCameraMapPresentation();
    }
    private async void OnStartRoutingClicked(object? sender, EventArgs args)
    {
        if (cameraMapSelectedShelter is null) { OnOpenCenterSearchOverlayClicked(sender, args); return; }
        cameraMapLocationRequest?.Cancel();
        if (!CameraNavigationLauncher.Publish(cameraMapSelectedShelter))
        {
            await DisplayAlert("Route destination", "This directory entry has no verified destination coordinates.", "OK"); return;
        }
        cameraMapPlannerOpen = false;
        // Publish triggers dev's one destination-change flow; do not assign activeRoute or compute a parallel route.
        RefreshCameraMapPresentation();
    }
    private void OnCameraMapRetryRouteClicked(object? sender, EventArgs args) { StartRouteRequestIfPossible(); RefreshCameraMapPresentation(); }
    private void OnCameraMapReturnToRouteClicked(object? sender, EventArgs args) { cameraMapPlannerOpen = false; RefreshCameraMapPresentation(); }
    private void OnCameraMapOverviewClicked(object? sender, EventArgs args) { if (cameraMapRenderedRoute is { } route) FrameCameraMapRoute(route); }
    private void OnCameraMapRecenterClicked(object? sender, EventArgs args)
    {
        if (CameraMapPresentationPolicy.CanShowCurrent(cameraMapRenderedLocation, DateTimeOffset.UtcNow, false))
        {
            var coordinate = cameraMapRenderedLocation!.Coordinate;
            var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(coordinate.Longitude, coordinate.Latitude);
            CameraMapControl.Map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), 2.3886);
        }
        else { cameraMapPermissionChecked = false; cameraMapLocationNotice = "Current location unknown; waiting for a validated GPS fix."; RefreshCameraMapPresentation(); }
    }
    private void OnRouteDirectionsButtonClicked(object? sender, EventArgs args) { PopulateCameraDirections(); routeDirectionsSheet.IsVisible = true; }
    private void OnTurnGuidancePanelClicked(object? sender, TappedEventArgs args) => OnRouteDirectionsButtonClicked(sender, args);
    private void OnRouteDirectionsCloseClicked(object? sender, EventArgs args) => routeDirectionsSheet.IsVisible = false;
    private void PopulateCameraDirections()
    {
        var destination = NavigationDestinationBridge.Current;
        RouteResult? route;
        lock (routeProgressFusionSync) route = CameraMapPresentationPolicy.ForTrip(activeRoute, activeDestinationVersion, destination.Version, destination.IsAvailable);
        routeDirectionsTitleLabel.Text = "Route overview · estimated geometry";
        routeDirectionsTargetLabel.Text = destination.IsAvailable ? "To: " + destination.Name : "No active destination";
        routeDirectionsDistanceLabel.Text = route is null ? "No accepted route" : $"~{route.TotalDistanceMeters:0} m";
        if (ReferenceEquals(cameraMapDirectionsRoute, route)) return;
        cameraMapDirectionsRoute = route;
        routeStepsCollectionView.ItemsSource = route is null ? Array.Empty<RouteDirectionStepItem>() : cameraDirections.Build(route, destination.Name)
            .Select(step => new RouteDirectionStepItem { IconSource = step.IconSource, InstructionText = step.InstructionText,
                SubtitleText = step.SubtitleText, DistanceText = step.DistanceText }).ToArray();
    }
}
