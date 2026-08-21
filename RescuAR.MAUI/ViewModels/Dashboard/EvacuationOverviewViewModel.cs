using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using RescuAR.App.Services.AreaStatus;
using RescuAR.App.Services.Dashboard;

namespace RescuAR.App.ViewModels.Dashboard;

public partial class EvacuationOverviewViewModel : ObservableObject
{
    private readonly IDashboardDataService _dataService;
    private readonly IAreaStatusService _areaStatusService;

    [ObservableProperty]
    public partial string DistanceText { get; set; } = "Calculating...";

    [ObservableProperty]
    public partial string CenterName { get; set; } = "Locating nearest shelter...";

    [ObservableProperty]
    public partial string ActionText { get; set; } = "Nearest Evacuation Center";

    [ObservableProperty]
    public partial string ModuleRoute { get; set; } = "//Map";

    [ObservableProperty]
    public partial string ModuleName { get; set; } = "Evacuation Center Info";

    public EvacuationOverviewViewModel() : this(DashboardDataService.Instance, AreaStatusService.Instance)
    {
    }

    public EvacuationOverviewViewModel(IDashboardDataService dataService, IAreaStatusService areaStatusService)
    {
        _dataService = dataService;
        _areaStatusService = areaStatusService;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(300);
            await LoadDataAsync();
        });
    }

    private async Task LoadDataAsync()
    {
        double lat = 14.6507;
        double lon = 121.1029;

        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status == PermissionStatus.Granted)
            {
                var lastLoc = await Geolocation.Default.GetLastKnownLocationAsync();
                if (lastLoc != null)
                {
                    lat = lastLoc.Latitude;
                    lon = lastLoc.Longitude;
                }
            }
        }
        catch (Exception)
        {
        }

        try
        {
            var (centerName, distanceMeters) = await _areaStatusService.GetRealNearestEvacuationCenterAsync(lat, lon);
            CenterName = centerName;
            DistanceText = distanceMeters >= 1000 
                ? $"{distanceMeters / 1000:F1} km away" 
                : $"{(int)distanceMeters} meters away";
        }
        catch (Exception)
        {
            var data = await _dataService.GetEvacuationDataAsync();
            DistanceText = data.DistanceMeters >= 1000 ? $"{data.DistanceMeters / 1000:F1} km away" : $"{data.DistanceMeters:F0} meters away";
            CenterName = data.CenterName;
        }
    }

    [RelayCommand]
    private async Task NavigateToEvacuationCenterAsync()
    {
        if (Shell.Current != null)
        {
            try
            {
                await Shell.Current.GoToAsync(ModuleRoute);
            }
            catch (Exception)
            {
                await Shell.Current.DisplayAlertAsync(
                    "Link Redirection",
                    $"Redirecting to link reference:\n{ModuleRoute}\n\nTarget Module: {ModuleName}",
                    "OK");
            }
        }
    }
}
