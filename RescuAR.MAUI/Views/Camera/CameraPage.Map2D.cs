using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using RescuAR.App.Services.Evacuation;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private Window? cameraMapWindow;
    private bool cameraMapWindowStopped;
    private CancellationTokenSource? cameraDirectoryPage;
    private bool cameraDirectoryRefreshing;

    private void StartCameraShelterDirectory()
    {
        if (cameraDirectoryPage is not null) return;
        cameraDirectoryPage = new();
        EvacuationDirectoryRuntime.Current.Changed += CameraShelterDirectoryChanged;
        Connectivity.Current.ConnectivityChanged += CameraShelterConnectivityChanged;
        ApplyCameraShelterDirectory();
        _ = RefreshCameraShelterDirectoryAsync();
    }

    private void StopCameraShelterDirectory()
    {
        if (cameraDirectoryPage is null) return;
        EvacuationDirectoryRuntime.Current.Changed -= CameraShelterDirectoryChanged;
        Connectivity.Current.ConnectivityChanged -= CameraShelterConnectivityChanged;
        cameraDirectoryPage.Cancel();
        cameraDirectoryPage.Dispose();
        cameraDirectoryPage = null;
        cameraDirectoryRefreshing = false;
    }

    private void CameraShelterDirectoryChanged(EvacuationDirectorySnapshot snapshot) => Dispatcher.Dispatch(() =>
    {
        if (cameraDirectoryPage is not null && pageIsVisible && !cameraMapWindowStopped)
            ApplyCameraShelterDirectory();
    });

    private void CameraShelterConnectivityChanged(object? sender, ConnectivityChangedEventArgs args)
    {
        if (args.NetworkAccess == NetworkAccess.Internet)
            Dispatcher.Dispatch(() => _ = RefreshCameraShelterDirectoryAsync(force: true));
    }

    private async Task RefreshCameraShelterDirectoryAsync(bool force = false)
    {
        if (cameraDirectoryPage is not { } owner || cameraDirectoryRefreshing) return;
        var token = owner.Token;
        cameraDirectoryRefreshing = true;
        UpdateCameraShelterDirectoryStatus();
        try
        {
            await EvacuationDirectoryRuntime.Current.RefreshAsync(force, token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(cameraDirectoryPage, owner))
            {
                cameraDirectoryRefreshing = false;
                if (pageIsVisible && !cameraMapWindowStopped) ApplyCameraShelterDirectory();
            }
        }
    }

    private void ApplyCameraShelterDirectory()
    {
        if (Dispatcher.IsDispatchRequired) { Dispatcher.Dispatch(ApplyCameraShelterDirectory); return; }
        var directory = EvacuationDirectoryRuntime.Current;
        if (_selectedShelterFor2DMap is { } selected &&
            directory.ResolveSelected(selected.Id, selected.Latitude, selected.Longitude) is null)
        {
            _selectedShelterFor2DMap = null;
            mapDestinationSelectedLabel.Text = "Select evacuation center…";
        }
        Populate2DMapDestinationCenters();
        if (mapCenterSearchOverlay.IsVisible) PopulateCenterSearchResults(centerModalSearchEntry.Text);
        if (currentCameraModuleView == CameraModuleViewMode.Map2D) RefreshCameraShelterPins();
        UpdateCameraShelterDirectoryStatus();
    }

    private void UpdateCameraShelterDirectoryStatus()
    {
        var snapshot = EvacuationDirectoryRuntime.Current.Current;
        mapSearchOrderLabel.Text = cameraDirectoryRefreshing ? "Fetching shelters…"
            : $"{snapshot.Centers.Count} shelters · {(GetCameraMapReading() is null ? "by name" : "nearest first")}";
        cameraDirectoryNoticeLabel.Text = snapshot.Notice;
        cameraDirectoryNoticeLabel.IsVisible = snapshot.Origin != DirectoryOrigin.Live ||
            snapshot.Notice.Contains("omitted", StringComparison.OrdinalIgnoreCase) ||
            snapshot.Notice.Contains("could not", StringComparison.OrdinalIgnoreCase);
        cameraSheltersEmptyLabel.Text = cameraDirectoryRefreshing ? "Loading evacuation centers from the database…"
            : snapshot.Centers.Count == 0 ? "No evacuation centers are listed in the current directory."
            : "No evacuation centers match your search.";
    }

    private void RefreshCameraShelterPins()
    {
        if (CameraMapControl?.Map is not { } map) return;
        var features = new List<Mapsui.Nts.GeometryFeature>();
        foreach (var shelter in EvacuationCenterRepository.GetEvacuationCenters())
        {
            var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(shelter.Longitude, shelter.Latitude);
            var feature = new Mapsui.Nts.GeometryFeature(new NetTopologySuite.Geometries.Point(x, y));
            feature.Styles.Add(new Mapsui.Styles.SymbolStyle
            {
                SymbolType = Mapsui.Styles.SymbolType.Ellipse,
                SymbolScale = 1.0,
                Fill = new Mapsui.Styles.Brush(new Mapsui.Styles.Color(22, 163, 74, 235)),
                Outline = new Mapsui.Styles.Pen(Mapsui.Styles.Color.White, 2)
            });
            feature.Styles.Add(new Mapsui.Styles.LabelStyle
            {
                Text = shelter.Name,
                ForeColor = new Mapsui.Styles.Color(15, 23, 42),
                BackColor = new Mapsui.Styles.Brush(new Mapsui.Styles.Color(255, 255, 255, 220)),
                Font = new Mapsui.Styles.Font { FontFamily = "Arial", Size = 9.5, Bold = true },
                Offset = new Mapsui.Styles.Offset(0, -18),
                HorizontalAlignment = Mapsui.Styles.LabelStyle.HorizontalAlignmentEnum.Center
            });
            features.Add(feature);
        }
        var layer = map.Layers.OfType<Mapsui.Layers.MemoryLayer>()
            .FirstOrDefault(item => item.Name == "Camera2DShelterDirectory");
        if (layer is null)
        {
            layer = new Mapsui.Layers.MemoryLayer { Name = "Camera2DShelterDirectory" };
            map.Layers.Add(layer);
        }
        layer.Features = features;
        layer.DataHasChanged();
        map.RefreshGraphics();
    }

    private void ObserveCameraMapWindow()
    {
        if (cameraMapWindow != Window)
        {
            UnobserveCameraMapWindow();
            cameraMapWindow = Window;
            cameraMapWindowStopped = false;
            if (cameraMapWindow is not null)
            {
                cameraMapWindow.Stopped += CameraMapWindowStopped;
                cameraMapWindow.Resumed += CameraMapWindowResumed;
            }
        }
        ObserveCameraViewRequests();
        StartCameraShelterDirectory();
        RefreshCameraStartupPresentation();
    }

    private void UnobserveCameraMapWindow()
    {
        StopCameraShelterDirectory();
        UnobserveCameraViewRequests();
        if (cameraMapWindow is null) return;
        cameraMapWindow.Stopped -= CameraMapWindowStopped;
        cameraMapWindow.Resumed -= CameraMapWindowResumed;
        cameraMapWindow = null;
    }

    private void CameraMapWindowStopped(object? sender, EventArgs args)
    {
        cameraMapWindowStopped = true;
        RefreshForegroundLocationOwnership();
        StopCameraMapDisplay();
    }

    private void CameraMapWindowResumed(object? sender, EventArgs args)
    {
        cameraMapWindowStopped = false;
        RefreshForegroundLocationOwnership();
        StartCameraShelterDirectory();
        ApplyPendingCameraViewRequest();
        RefreshCameraModuleDynamicUi();
        StartRouteRequestIfPossible();
    }

    private void StopCameraMapDisplay()
    {
        StopCameraShelterDirectory();
        CancelRouteRequest("Camera window stopped or page exited.");
        StopRouteProgress("Camera window stopped or page exited.");
        CancelCameraInstructionSpeech();
        RefreshCameraStartupPresentation();
    }
}
