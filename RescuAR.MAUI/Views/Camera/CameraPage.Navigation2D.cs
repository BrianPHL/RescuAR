using RescuAR.Navigation.Timing;
using RescuAR.Navigation.Progress;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using RescuAR.App.Services.Evacuation;
using RescuAR.MAUI.Services.Location;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.State;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private readonly CameraMapViewportPolicy cameraMapViewport = new();
    private RouteResult? cameraMapRenderedRoute;
    private long cameraMapRenderedTrip = -1;
    private RouteResult? cameraDirectionsRoute;
    private long cameraDirectionsTrip = -1;
    private bool cameraMapHasInitialFrame;
    private bool cameraOpenDirectionsWhenReady;
    private bool? cameraMapHadLocation;
    private bool cameraLocationPermissionDenied;

    private LocationReading? GetCameraMapReading()
    {
        lock (routeProgressFusionSync)
            return CameraMapPresentationPolicy.CanShowCurrent(latestRouteStartupReading,
                NavigationClock.UtcNow, positionObservationHeld) ? latestRouteStartupReading : null;
    }

    private RouteResult? GetCameraMapRoute()
    {
        var destination = NavigationDestinationBridge.Current;
        lock (routeProgressFusionSync)
            return CameraMapPresentationPolicy.ForTrip(activeRoute, activeDestinationVersion,
                destination.Version, destination.IsAvailable && !safeZoneConfirmed && !routeHazardBlocksGuidance);
    }

    // Both views consume the accepted geographic trip. AR alignment never
    // becomes a prerequisite for drawing the map or advancing GPS progress.
    private void RefreshCameraMapNavigation()
    {
        if (!pageIsVisible || cameraMapWindowStopped) return;
        if (Dispatcher.IsDispatchRequired) { Dispatcher.Dispatch(RefreshCameraMapNavigation); return; }
        var destination = NavigationDestinationBridge.Current;
        var route = GetCameraMapRoute();
        if (routeDirectionsSheet.IsVisible &&
            (!ReferenceEquals(cameraDirectionsRoute, route) || cameraDirectionsTrip != destination.Version))
        {
            cameraDirectionsRoute = route;
            cameraDirectionsTrip = destination.Version;
            PopulateRouteDirectionsSheet();
        }
        if (!CanSpeakCurrentRoute()) CancelCameraInstructionSpeech();
        if (currentCameraModuleView != CameraModuleViewMode.Map2D) return;

        if (!ReferenceEquals(cameraMapRenderedRoute, route) || cameraMapRenderedTrip != destination.Version)
        {
            cameraMapRenderedRoute = route;
            cameraMapRenderedTrip = destination.Version;
            InitializeOrRefresh2DCameraMap();
        }
        RefreshCameraMapLocationPin();
        var reading = GetCameraMapReading();
        mapFromLocationLabel.FormattedText = null;
        mapFromLocationLabel.Text = reading is null ? "Waiting for accurate GPS" : "Current device location";
        mapSearchOrderLabel.Text = cameraDirectoryRefreshing ? "Fetching shelters…"
            : $"{EvacuationDirectoryRuntime.Current.Current.Centers.Count} shelters · {(reading is null ? "by name" : "nearest first")}";
        if (cameraMapHadLocation != (reading is not null))
        {
            cameraMapHadLocation = reading is not null;
            if (mapCenterSearchOverlay.IsVisible) PopulateCenterSearchResults(centerModalSearchEntry.Text);
            Populate2DMapDestinationCenters();
        }

        bool navigating = destination.IsAvailable && !safeZoneConfirmed;
        mapRouteSearchCard.IsVisible = !navigating && !safeZoneConfirmed;
        mapRouteSummaryPanel.IsVisible = navigating;
        routeCalculatingOverlay.IsVisible = navigating && route is null && routeRequestInProgress && !routeStartupGpsRetryPending;
        endNavigationButton.IsVisible = navigating;
        startRoutingButton.IsVisible = !destination.IsAvailable && _selectedShelterFor2DMap is not null && !safeZoneConfirmed;
        exploreSafeZonesButton.IsVisible = !destination.IsAvailable && _selectedShelterFor2DMap is null && !safeZoneConfirmed;
        mapRouteDestinationNameLabel.Text = $"To: {destination.Name}";
        mapRouteStepsButton.Text = route is null ? "Retry" : "Steps";
        mapRouteStepsButton.IsEnabled = !routeRequestInProgress;
        if (route is null)
            mapRouteStatsLabel.Text = safeZoneConfirmed ? "Arrival confirmed"
                : routeHazardNotice ?? routeStartupFailureMessage ?? "Calculating pedestrian route…";
        else
        {
            var progress = _routeProgressTracker.Current;
            double remaining = progress.HasProgress ? progress.RemainingMeters : route.TotalDistanceMeters;
            var hazardUpdated = _hazardReroutingService.LastRemoteRefreshSuccessUtc;
            string hazardStatus = hazardUpdated is not { } at ? "Hazard data unavailable"
                : Connectivity.Current.NetworkAccess == NetworkAccess.Internet && DateTimeOffset.UtcNow - at < TimeSpan.FromSeconds(15)
                    ? $"Known hazards updated {at.ToLocalTime():HH:mm}" : $"Saved hazards · {at.ToLocalTime():MMM d, HH:mm}";
            mapRouteStatsLabel.Text = $"{NavigationRecoveryPolicy.ProviderLabel(route)} · {remaining:F0} m · ~{Math.Max(1, Math.Round(remaining / 80)):F0} min walk\n{hazardStatus}";
        }

        RefreshCameraMapTurnGuidance(route);
        if (cameraOpenDirectionsWhenReady && route is not null)
        {
            cameraOpenDirectionsWhenReady = false;
            OpenRouteDirectionsSheet();
        }
        if (!destination.IsAvailable && !cameraLocationPermissionDenied &&
            NavigationClock.UtcNow - cameraMapLastLocationAttempt >= TimeSpan.FromSeconds(5))
            _ = Refresh2DMapLocationAsync(requestCurrent: false);
    }

    private void RefreshCameraMapTurnGuidance(RouteResult? route)
    {
        var progress = _routeProgressTracker.Current;
        bool held = !HasCurrentGuidancePosition(progress, NavigationClock.UtcNow);
        var guidance = route is null ? PedestrianTurnGuidanceService.TurnGuidanceSnapshot.Unavailable
            : CameraMapPresentationPolicy.Guidance(route, progress, held);
        if (!guidance.IsAvailable || dynamicRerouteInProgress || emergencyAdvisoryVisible ||
            safeZoneConfirmed || lastSafeZoneDecision.IsCandidate)
        {
            SetTurnGuidanceVisible(false);
            return;
        }
        turnDirectionIconLabel.Source = RouteDirectionsService.Icon(guidance.Instruction);
        string text = RouteDirectionsService.Text(guidance.Instruction);
        if (RouteDirectionsService.IsTurn(guidance.Instruction) && double.IsFinite(guidance.DistanceToTurnMeters))
            text += $" in {Math.Max(0, guidance.DistanceToTurnMeters):F0} m";
        turnInstructionLabel.Text = text;
        turnDistanceLabel.Text = $"{guidance.RemainingRouteMeters:F0} m remaining to {activeDestinationName}";
        SetTurnGuidanceVisible(true);
        if (!string.Equals(_lastSpokenInstruction, text, StringComparison.Ordinal))
        {
            _lastSpokenInstruction = text;
            _ = SpeakTurnInstructionAsync(text);
        }
    }

    private bool CanSpeakCurrentRoute() => pageIsVisible && !cameraMapWindowStopped &&
        !dynamicRerouteInProgress && !emergencyAdvisoryVisible && !safeZoneConfirmed &&
        !lastSafeZoneDecision.IsCandidate && GetCameraMapRoute() is not null &&
        GetCameraMapReading() is not null &&
        HasCurrentGuidancePosition(_routeProgressTracker.Current, NavigationClock.UtcNow);

    private void CancelCameraInstructionSpeech()
    {
        _ttsCancellation?.Cancel();
        _lastSpokenInstruction = string.Empty;
    }

    private void RefreshCameraMapLocationPin()
    {
        if (CameraMapControl?.Map is not { } map) return;
        var layer = map.Layers.OfType<Mapsui.Layers.MemoryLayer>()
            .FirstOrDefault(item => item.Name == "Camera2DUserLocation");
        if (layer is null)
        {
            layer = new Mapsui.Layers.MemoryLayer { Name = "Camera2DUserLocation" };
            map.Layers.Add(layer);
        }
        var features = new List<Mapsui.Nts.GeometryFeature>();
        if (GetCameraMapReading() is { } reading)
        {
            var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(reading.Coordinate.Longitude, reading.Coordinate.Latitude);
            var feature = new Mapsui.Nts.GeometryFeature(new NetTopologySuite.Geometries.Point(x, y));
            feature.Styles.Add(new Mapsui.Styles.SymbolStyle
            {
                SymbolType = Mapsui.Styles.SymbolType.Ellipse,
                SymbolScale = 1.0,
                Fill = new Mapsui.Styles.Brush(new Mapsui.Styles.Color(26, 115, 232)),
                Outline = new Mapsui.Styles.Pen(Mapsui.Styles.Color.White, 2)
            });
            features.Add(feature);
        }
        layer.Features = features;
        layer.DataHasChanged();
        map.RefreshGraphics();
    }
}
