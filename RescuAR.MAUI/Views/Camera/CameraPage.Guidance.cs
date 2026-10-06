using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using RescuAR.AR;
using RescuAR.Diagnostics;
using RescuAR.MAUI.Services.Location;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.State;
using MapColor = Mapsui.Styles.Color;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private const string VoicePreferenceKey = "NavigationVoiceEnabled";
    private readonly NavigationVoiceGuidanceService voiceGuidance = new(new MauiNavigationSpeech());
    private readonly RouteDirectionsService directionsService = new();
    private readonly MemoryLayer cameraMapRouteLayer = new() { Name = "Selected pedestrian route" };
    private readonly MemoryLayer cameraMapMarkerLayer = new() { Name = "Locations" };
    private readonly MemoryLayer cameraMapShelterLayer = new() { Name = "Listed evacuation centers" };
    private readonly MemoryLayer cameraMapRoadLayer = new() { Name = "Embedded road context" };
    private Task? cameraMapLoadTask;
    private Task? cameraMapLocationTask;
    private CancellationTokenSource? cameraGuidanceCancellation;
    private RouteResult? renderedCameraMapRoute;
    private RouteResult? renderedDirectionsRoute;
    private NavigationDestinationBridge.DestinationSnapshot fittedMapDestination;
    private bool cameraMapHasFitted;
    private bool cameraMapLoaded;
    private bool arRouteAlignmentRequired;
    private bool geographicRouteAlignmentInProgress;
    private DateTimeOffset nextGeographicAlignmentAt;
    private DateTimeOffset nextMapRouteRetryAt;
    private long cameraGuidanceEpoch;
    private string? cameraMapError;
    private bool cameraLocationPermissionDenied;
    private DateTimeOffset nextMapLocationAt;

    private void InitializeCameraGuidance()
    {
        voiceGuidance.SetEnabled(Preferences.Default.Get(VoicePreferenceKey, false));
        CameraMapControl.Map = new Mapsui.Map { CRS = "EPSG:3857" };
        CameraMapControl.Map.Layers.Add(cameraMapRoadLayer);
        CameraMapControl.Map.Layers.Add(cameraMapShelterLayer);
        CameraMapControl.Map.Layers.Add(cameraMapRouteLayer);
        CameraMapControl.Map.Layers.Add(cameraMapMarkerLayer);
        var shelters = new List<GeometryFeature>();
        foreach (var shelter in EvacuationCenterRepository.GetEvacuationCenters())
        {
            GeoCoordinate coordinate = new(shelter.Latitude, shelter.Longitude);
            if (coordinate.IsValid && (coordinate.Latitude != 0 || coordinate.Longitude != 0))
                shelters.Add(CreateCameraMapMarker(coordinate, shelter.Name, MapColor.Gray));
        }
        cameraMapShelterLayer.Features = shelters;
        FitCameraMap();
    }

    private void OnCameraGuidanceAppearing()
    {
        cameraGuidanceEpoch++;
        cameraLocationPermissionDenied = false;
        cameraGuidanceCancellation = new CancellationTokenSource();
        RefreshCameraGuidanceUi();
    }

    private void OnCameraGuidanceDisappearing()
    {
        cameraGuidanceEpoch++;
        cameraGuidanceCancellation?.Cancel();
        cameraGuidanceCancellation?.Dispose();
        cameraGuidanceCancellation = null;
        voiceGuidance.SetActive(false);
        routeDirectionsSheet.IsVisible = false;
    }

    private void OnCameraGuidanceViewChanged(bool changed)
    {
        voiceGuidance.SetActive(false);
        routeDirectionsSheet.IsVisible = false;
        if (changed)
        {
            // The observed route task owns its cleanup. Do not start a second
            // request until its cancellation has been processed.
            routeRequestCancellation?.Cancel();
            cameraGuidanceEpoch++;
            nextMapRouteRetryAt = DateTimeOffset.MinValue;
            if (currentCameraModuleView == CameraModuleViewMode.Map2D && activeRoute is not null)
            {
                // Geographic progress may move while the camera is paused.
                // Never revive the old AR window when returning to the camera.
                lock (routeProgressFusionSync)
                {
                    arRouteAlignmentRequired = true;
                    ARRouteBridge.Clear();
                }
            }
        }
        if (currentCameraModuleView == CameraModuleViewMode.Map2D && pageIsVisible)
        {
            cameraMapLoadTask ??= LoadCameraOfflineMapAsync();
            if (cameraMapLoadTask.IsCompleted && !cameraMapLoaded && cameraMapError is null)
                cameraMapLoadTask = LoadCameraOfflineMapAsync();
            if (CanResumeRetainedRoute()) StartRouteProgress();
            else StartRouteRequestIfPossible();
        }
        else if (pageIsVisible) RefreshArTrackingStatusBanner();
        RefreshCameraGuidanceUi();
    }

    private async Task LoadCameraOfflineMapAsync()
    {
        CancellationToken token = cameraGuidanceCancellation?.Token ?? CancellationToken.None;
        try
        {
            var roads = await CameraOfflineMapDataService.LoadAsync(token);
            var features = await Task.Run(() => roads.Select(road =>
            {
                GeometryFeature feature = new(new LineString(road.Select(ToCameraMapCoordinate).ToArray()));
                return feature;
            }).ToArray(), token);
            token.ThrowIfCancellationRequested();
            cameraMapRoadLayer.Features = features;
            cameraMapRoadLayer.Style = new VectorStyle { Line = new Pen(MapColor.Gray, 1) };
            cameraMapLoaded = true;
            cameraMapError = null;
            cameraMapRoadLayer.DataHasChanged();
            CameraMapControl.Refresh();
            RefreshCameraGuidanceUi();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            cameraMapError = "Offline road context unavailable. Route and directions remain available.";
            AndroidLog.Warn("RescuAR-CameraMap", DiagnosticPrivacyPolicy.FormatException(exception));
            if (pageIsVisible) RefreshCameraGuidanceUi();
        }
    }

    private void RefreshCameraGuidanceUi()
    {
        if (!Dispatcher.IsDispatchRequired) RefreshCameraGuidanceUiCore();
        else Dispatcher.Dispatch(RefreshCameraGuidanceUiCore);
    }

    private void RefreshCameraGuidanceUiCore()
    {
        if (!pageIsVisible) return;
        bool mapMode = currentCameraModuleView == CameraModuleViewMode.Map2D;
        var destination = NavigationDestinationBridge.Current;
        RouteResult? route;
        RouteProgressTracker.ProgressSnapshot progress;
        LocationReading? reading;
        lock (routeProgressFusionSync)
        {
            route = CanResumeRetainedRoute() ? activeRoute : null;
            progress = _routeProgressTracker.Current;
            reading = latestRouteStartupReading;
        }
        bool fresh = IsCurrentRouteStartupLocationAcceptable(reading);
        bool onRoute = progress.HasProgress && !progress.IsOffRoute && !lastOffRouteCandidate &&
            lastRouteMatchConfidence >= RouteMatchConfidence.Medium &&
            lastGpsConfidence >= GpsPdrFusionPolicy.GpsConfidence.Medium && !_gpsPdrFusionPolicy.IsRouteIdentitySuspended;
        bool canGuide = route is not null && fresh && onRoute && !initialRoadApproachPending &&
            !dynamicRerouteInProgress && !routeRequestInProgress && !safeZoneConfirmed && !emergencyAdvisoryVisible;

        cameraGuidanceControls.IsVisible = currentCameraModuleView == CameraModuleViewMode.ArCamera && !safeZoneConfirmed;
        cameraVoiceButton.ImageSource = voiceGuidance.Enabled ? "lucide_volume_2_black.png" : "lucide_volume_x_black.png";
        voiceGuidanceStatusLabel.Text = voiceGuidance.LastError ?? (voiceGuidance.Enabled ? "Voice on" : "Voice off");
        cameraMapVoiceButton.Text = voiceGuidance.Enabled ? "Mute" : "Voice off";
        cameraDirectionsButton.IsVisible = destination.IsAvailable && !safeZoneConfirmed &&
            currentCameraModuleView != CameraModuleViewMode.FloodDepth;

        if (mapMode)
        {
            if (!cameraMapLoaded && cameraMapError is null && cameraMapLoadTask?.IsCompleted != false)
                cameraMapLoadTask = LoadCameraOfflineMapAsync();
            arTrackingStatusBanner.IsVisible = false;
            routeLocatorPanel.IsVisible = false;
            cameraMapDestinationLabel.Text = destination.IsAvailable ? destination.Name : "Choose an evacuation center";
            string location = cameraLocationPermissionDenied ? "Location permission denied; tap Retry to request again" :
                reading is null ? "Location unavailable" : fresh ? "Current location" :
                $"Last known location • {reading.Timestamp.ToLocalTime():HH:mm}";
            string state = safeZoneConfirmed ? "Arrival confirmed" : dynamicRerouteInProgress ? "Updating route…" :
                routeRequestInProgress ? "Calculating route…" : route is null ? routeStartupFailureMessage ?? "No route calculated" :
                !fresh ? "Waiting for a current location" : initialRoadApproachPending ? "Approach the mapped route; check access" :
                !onRoute ? "Checking your position on the route" : $"{Math.Max(0, progress.RemainingMeters):0} m remaining";
            mapModeStatusLabel.Text = $"{state}\n{location} • " +
                (cameraMapError ?? (cameraMapLoaded ? "Offline road map" : "Loading offline road map…")) +
                $" • {voiceGuidanceStatusLabel.Text}";
            RefreshCameraMapGeometry(route, reading, fresh, destination);
            if (cameraMapLocationTask is null || cameraMapLocationTask.IsCompleted)
                if (activeDestinationCoordinate is null && !cameraLocationPermissionDenied &&
                    DateTimeOffset.UtcNow >= nextMapLocationAt)
                {
                    nextMapLocationAt = DateTimeOffset.UtcNow.AddSeconds(10);
                    cameraMapLocationTask = RefreshCameraMapLocationAsync();
                }
            if (route is null && destination.IsAvailable && !routeRequestInProgress && !dynamicRerouteInProgress &&
                !cameraLocationPermissionDenied &&
                DateTimeOffset.UtcNow >= nextMapRouteRetryAt)
            {
                nextMapRouteRetryAt = DateTimeOffset.UtcNow.AddSeconds(10);
                StartRouteRequestIfPossible();
            }
            var guidance = canGuide ? _turnGuidanceService.Evaluate(route!, progress.CommittedProgressMeters) :
                PedestrianTurnGuidanceService.TurnGuidanceSnapshot.Unavailable;
            turnGuidancePanel.IsVisible = guidance.IsAvailable;
            if (guidance.IsAvailable)
            {
                turnGuidancePanel.Margin = new Thickness(8, 90, 8, 0);
                turnGuidancePanel.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#E8FFFFFF");
                turnDirectionIconLabel.Source = RouteDirectionsService.Icon(guidance.Instruction);
                string distance = double.IsFinite(guidance.DistanceToTurnMeters) ? $" in {guidance.DistanceToTurnMeters:0} meters" : "";
                turnInstructionLabel.Text = RouteDirectionsService.Text(guidance.Instruction) + distance;
                turnDistanceLabel.Text = $"{Math.Max(0, progress.RemainingMeters):0} meters to {destination.Name}";
                voiceGuidance.SetActive(true);
                _ = voiceGuidance.UpdateAsync(route!, guidance, progress.CommittedProgressMeters,
                    turnInstructionLabel.Text, DateTimeOffset.UtcNow);
            }
            else voiceGuidance.SetActive(false);
        }
        else
        {
            bool arCanSpeak = currentCameraModuleView == CameraModuleViewMode.ArCamera && canGuide &&
                !arRouteAlignmentRequired && !cameraPipelineTerminalFailureVisible &&
                turnGuidancePanel.IsVisible && lastTurnGuidance.IsAvailable;
            voiceGuidance.SetActive(arCanSpeak);
            if (arCanSpeak)
                _ = voiceGuidance.UpdateAsync(route!, lastTurnGuidance, progress.CommittedProgressMeters,
                    turnInstructionLabel.Text, DateTimeOffset.UtcNow);
            if (arRouteAlignmentRequired && !geographicRouteAlignmentInProgress && !routeRequestInProgress &&
                _arCoreService.IsInitialized && !_arCoreService.IsSessionPaused && fresh &&
                DateTimeOffset.UtcNow >= nextGeographicAlignmentAt && route is not null)
                _ = AlignGeographicRouteForArAsync(route, reading!);
        }
        if (routeDirectionsSheet.IsVisible) PopulateRouteDirectionsSheet(route, progress, destination);
    }

    private async Task RefreshCameraMapLocationAsync()
    {
        CancellationToken token = cameraGuidanceCancellation?.Token ?? CancellationToken.None;
        long epoch = cameraGuidanceEpoch;
        try
        {
            if (!await _locationService.EnsurePermissionAsync(token))
            {
                if (epoch == cameraGuidanceEpoch) cameraLocationPermissionDenied = true;
                return;
            }
            var reading = await _locationService.GetCurrentLocationAsync(token) ??
                await _locationService.GetLastKnownLocationAsync(token);
            token.ThrowIfCancellationRequested();
            if (epoch != cameraGuidanceEpoch || !pageIsVisible || activeDestinationCoordinate is not null) return;
            if (reading?.Coordinate.IsValid == true && reading.Timestamp <= DateTimeOffset.UtcNow.AddSeconds(2))
                lock (routeProgressFusionSync) latestRouteStartupReading = reading;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { AndroidLog.Warn("RescuAR-CameraMap", DiagnosticPrivacyPolicy.FormatException(exception)); }
    }

    private void RefreshCameraMapGeometry(RouteResult? route, LocationReading? reading, bool fresh,
        NavigationDestinationBridge.DestinationSnapshot destination)
    {
        if (!ReferenceEquals(renderedCameraMapRoute, route))
        {
            renderedCameraMapRoute = route;
            cameraMapRouteLayer.Features = route is { Points.Count: >= 2 }
                ? new[] { new GeometryFeature(new LineString(route.Points.Select(point => ToCameraMapCoordinate(point.Coordinate)).ToArray())) }
                : Array.Empty<GeometryFeature>();
            cameraMapRouteLayer.Style = new VectorStyle { Line = new Pen(new MapColor(10, 146, 156), 5) };
            cameraMapRouteLayer.DataHasChanged();
            cameraMapHasFitted = false;
        }
        List<GeometryFeature> markers = [];
        if (route is { Points.Count: >= 2 }) markers.Add(CreateCameraMapMarker(route.Points[0].Coordinate, "Route start", MapColor.Gray));
        if (reading?.Coordinate.IsValid == true)
            markers.Add(CreateCameraMapMarker(reading.Coordinate, fresh ? "You" : "Last known", fresh ? MapColor.Blue : MapColor.Gray));
        if (destination.IsAvailable) markers.Add(CreateCameraMapMarker(destination.Coordinate, destination.Name, MapColor.Red));
        cameraMapMarkerLayer.Features = markers;
        cameraMapMarkerLayer.DataHasChanged();
        if (!cameraMapHasFitted || destination != fittedMapDestination) FitCameraMap();
        CameraMapControl.Refresh();
    }

    private static Coordinate ToCameraMapCoordinate(GeoCoordinate coordinate)
    {
        var (x, y) = SphericalMercator.FromLonLat(coordinate.Longitude, coordinate.Latitude);
        return new Coordinate(x, y);
    }

    private static GeometryFeature CreateCameraMapMarker(GeoCoordinate coordinate, string label, MapColor color)
    {
        GeometryFeature feature = new(new NetTopologySuite.Geometries.Point(ToCameraMapCoordinate(coordinate)));
        feature.Styles.Add(new SymbolStyle { Fill = new Mapsui.Styles.Brush(color), Outline = new Pen(MapColor.White, 2), SymbolScale = 0.7 });
        feature.Styles.Add(new LabelStyle { Text = label, ForeColor = MapColor.Black,
            BackColor = new Mapsui.Styles.Brush(MapColor.White), Offset = new Offset(0, -20),
            Font = new Mapsui.Styles.Font { Size = 12 } });
        return feature;
    }

    private void FitCameraMap()
    {
        if (CameraMapControl.Width <= 0 || CameraMapControl.Height <= 0)
        {
            cameraMapHasFitted = false;
            return;
        }
        var destination = NavigationDestinationBridge.Current;
        var route = renderedCameraMapRoute;
        var points = route?.Points.Select(point => ToCameraMapCoordinate(point.Coordinate)).ToArray();
        if (points is { Length: >= 2 })
        {
            double minX = points.Min(point => point.X), minY = points.Min(point => point.Y);
            double maxX = points.Max(point => point.X), maxY = points.Max(point => point.Y);
            CameraMapControl.Map.Navigator.ZoomToBox(new MRect(minX - 80, minY - 80, maxX + 80, maxY + 80));
        }
        else
        {
            var coordinate = destination.IsAvailable ? destination.Coordinate :
                latestRouteStartupReading?.Coordinate ?? new GeoCoordinate(14.6507, 121.1029);
            var projected = ToCameraMapCoordinate(coordinate);
            CameraMapControl.Map.Navigator.CenterOnAndZoomTo(new MPoint(projected.X, projected.Y), 9.5546);
        }
        // A regional viewport is map coverage, never a substitute user marker.
        fittedMapDestination = destination;
        cameraMapHasFitted = true;
    }

    private async Task AlignGeographicRouteForArAsync(RouteResult route, LocationReading reading)
    {
        geographicRouteAlignmentInProgress = true;
        long epoch = cameraGuidanceEpoch;
        CancellationToken token = cameraGuidanceCancellation?.Token ?? CancellationToken.None;
        try
        {
            var heading = await _headingAlignmentService.CaptureAsync(reading.Coordinate, reading.AltitudeMeters, token);
            token.ThrowIfCancellationRequested();
            if (epoch != cameraGuidanceEpoch || !pageIsVisible || !ReferenceEquals(activeRoute, route) ||
                currentCameraModuleView == CameraModuleViewMode.Map2D || !CanResumeRetainedRoute() ||
                _arCoreService.IsSessionPaused || heading is not { IsAvailable: true, IsStable: true } ||
                heading.Value.SessionGeneration != ARCameraPoseBridge.CurrentFrame.Generation.SessionGeneration) return;
            lock (routeProgressFusionSync)
            {
                lastHeadingAlignment = heading;
                activeMapToArYawDegrees = heading.Value.MapToArYawDegrees;
                ARRouteBridge.Clear();
                arRouteAlignmentRequired = false;
                ResetArRouteVisualMode("map route aligned to the retained AR session");
                roadEntryConfirmation.Reset();
                initialRoadApproachPending = true;
            }
            StartRouteProgress();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { AndroidLog.Warn("RescuAR-Heading", DiagnosticPrivacyPolicy.FormatException(exception)); }
        finally
        {
            geographicRouteAlignmentInProgress = false;
            nextGeographicAlignmentAt = DateTimeOffset.UtcNow.AddSeconds(10);
        }
    }

    private void OnVoiceGuidanceToggleClicked(object? sender, EventArgs e)
    {
        voiceGuidance.SetEnabled(!voiceGuidance.Enabled);
        Preferences.Default.Set(VoicePreferenceKey, voiceGuidance.Enabled);
        RefreshCameraGuidanceUi();
    }

    private void OnTurnGuidancePanelClicked(object? sender, TappedEventArgs e) => OpenRouteDirectionsSheet();
    private void OnCameraDirectionsClicked(object? sender, EventArgs e) => OpenRouteDirectionsSheet();
    private void OnRouteDirectionsCloseClicked(object? sender, EventArgs e) => routeDirectionsSheet.IsVisible = false;
    private void OnCameraMapFitClicked(object? sender, EventArgs e) => FitCameraMap();
    private void OnCameraMapRetryClicked(object? sender, EventArgs e)
    {
        cameraMapError = null;
        cameraLocationPermissionDenied = false;
        nextMapLocationAt = DateTimeOffset.MinValue;
        if (!cameraMapLoaded && cameraMapLoadTask?.IsCompleted != false) cameraMapLoadTask = LoadCameraOfflineMapAsync();
        nextMapRouteRetryAt = DateTimeOffset.UtcNow.AddSeconds(10);
        StartRouteRequestIfPossible();
    }

    private void OpenRouteDirectionsSheet()
    {
        renderedDirectionsRoute = null;
        routeDirectionsSheet.IsVisible = true;
        RefreshCameraGuidanceUi();
    }

    private void PopulateRouteDirectionsSheet(RouteResult? route, RouteProgressTracker.ProgressSnapshot progress,
        NavigationDestinationBridge.DestinationSnapshot destination)
    {
        routeDirectionsTargetLabel.Text = destination.IsAvailable ? destination.Name : "No destination selected";
        routeDirectionsDistanceLabel.Text = route is null ? "No active route" :
            $"{Math.Max(0, progress.RemainingMeters):0} m remaining • {route.TotalDistanceMeters:0} m total";
        routeDirectionsStatusLabel.Text = dynamicRerouteInProgress ? "Updating route; showing the retained route." :
            "Directions estimated from mapped geometry. Check access and local advisories.";
        if (ReferenceEquals(renderedDirectionsRoute, route) && route is not null) return;
        renderedDirectionsRoute = route;
        routeStepsCollectionView.ItemsSource = route is null ? new[] {
            new RouteDirectionStep(0, PedestrianTurnGuidanceService.TurnInstruction.Continue,
                "Directions unavailable", routeStartupFailureMessage ?? "Select a center and wait for a current location.", "", "lucide_arrow_up_teal.png")
        } : directionsService.Build(route, destination.Name);
    }
}
