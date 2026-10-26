using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui;
using RescuAR.App.Services.SafetyCircle;

namespace RescuAR.App.ViewModels.Map;

public partial class SafetyCircleViewModel
{
    private readonly ICircleDeviceReader localDevice = new MauiCircleDeviceReader();
    private CircleDeviceReading? localReading;
    private bool locating, localPermissionChecked, localPermissionGranted;
    [ObservableProperty] private string localLocationStatus = "Your device position is unavailable.";
    [ObservableProperty] private bool isPeopleSheetOpen;
    [ObservableProperty] private bool isMapDetailsOpen;

    private CircleDeviceReading? CurrentLocalReading => CirclePresencePolicy.CanDisplayLocal(localReading, DateTimeOffset.UtcNow) ? localReading : null;

    partial void OnStatusMessageChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) IsMapDetailsOpen = true;
    }

    // This display path has no upload operation and never changes the sharing switch.
    private async Task RefreshLocalLocationAsync()
    {
        if (locating || owner is not { } scope || page is null || (localPermissionChecked && !localPermissionGranted)) return;
        long request = version; var token = page.Token;
        locating = true;
        try
        {
            Require(scope, request);
            if (!localPermissionChecked)
            {
                var permission = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                Require(scope, request);
                if (permission != PermissionStatus.Granted) permission = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                Require(scope, request);
                localPermissionChecked = true; localPermissionGranted = permission == PermissionStatus.Granted;
                if (!localPermissionGranted)
                {
                    localReading = null;
                    LocalLocationStatus = "Location permission is off · your position is hidden.";
                    UpdateMapMarkers(); return;
                }
            }
            var reading = await localDevice.ReadAsync(token);
            Require(scope, request);
            localReading = CirclePresencePolicy.CanDisplayLocal(reading, DateTimeOffset.UtcNow) ? reading : null;
            LocalLocationStatus = localReading is { } current ? $"Your device position · accuracy ~{current.Accuracy:0} m" : "Waiting for a recent, accurate device position.";
            UpdateMapMarkers();
            if (!centered && CurrentLocalReading is { } position) { CenterOnLocal(position); centered = true; }
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (request == version)
            {
                localReading = null; LocalLocationStatus = "Your device position is unavailable · check location settings.";
                UpdateMapMarkers();
            }
        }
        finally { if (request == version) locating = false; }
    }

    private void AddLocalPosition(List<Mapsui.Nts.GeometryFeature> features)
    {
        if (CurrentLocalReading is not { } reading) return;
        var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(reading.Longitude, reading.Latitude);
        var self = CircleMembers.FirstOrDefault(member => member.UserId == owner?.UserId);
        byte[]? photo = null;
        if (self is not null) avatarBytes.TryGetValue(self.Presence.Person.AvatarUrl, out photo);
        var pin = new Mapsui.Nts.GeometryFeature(new NetTopologySuite.Geometries.Point(x, y));
        pin.Styles.Add(new Mapsui.Styles.ImageStyle
        {
            Image = GenerateLife360PinImageSource(photo, "You · device", "#0A8491", true, self?.Initials ?? "ME"),
            SymbolScale = 0.5, Offset = new Mapsui.Styles.Offset(0, 35)
        });
        features.Add(pin);
    }

    private void CenterOnLocal(CircleDeviceReading reading)
    {
        var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(reading.Longitude, reading.Latitude);
        Map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), 9.5546);
    }

    [RelayCommand] private void TogglePeopleSheet() => IsPeopleSheetOpen = !IsPeopleSheetOpen;
    [RelayCommand] private void ToggleMapDetails() => IsMapDetailsOpen = !IsMapDetailsOpen;
    [RelayCommand] private async Task CenterOnMe()
    {
        if (CurrentLocalReading is null)
        {
            localPermissionChecked = localPermissionGranted = false;
            await RefreshLocalLocationAsync();
        }
        if (CurrentLocalReading is { } reading) CenterOnLocal(reading);
        else IsMapDetailsOpen = true;
    }
}
