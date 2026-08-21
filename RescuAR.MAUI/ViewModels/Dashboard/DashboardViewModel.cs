using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Storage;
using RescuAR.App.Models;
using RescuAR.App.Services.Reports;
using RescuAR.App.Services.Weather;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace RescuAR.App.ViewModels.Dashboard;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IWeatherService _weatherService;

    [ObservableProperty]
    private string userName = "Aubrey";

    [ObservableProperty]
    private string greeting = "Good day,";

    [ObservableProperty]
    private int preparednessScore = 100;

    [ObservableProperty]
    private double scoreProgress = 1.0;

    [ObservableProperty]
    private string preparednessStatus = "Highly Prepared";

    [ObservableProperty]
    private int activeAdvisoriesCount = 2;

    [ObservableProperty]
    private string weatherSummary = "24 °C • Clear / Sunny";

    [ObservableProperty]
    private string weatherTemperatureText = "24 °C";

    [ObservableProperty]
    private string weatherConditionTitle = "Clear / Sunny";

    [ObservableProperty]
    private string weatherConditionSummary = "Clear weather conditions in your area";

    [ObservableProperty]
    private string locationName = "Quezon City, Metro Manila";

    [ObservableProperty]
    private string floodRiskLevel = "Moderate Flood Risk";

    [ObservableProperty]
    private string riverStatusText = "Marikina River: Level 1 (Standby)";

    [ObservableProperty]
    private string riverStatusPillText = "Monitoring";

    [ObservableProperty]
    private DisasterAdvisory? selectedAdvisory;

    [ObservableProperty]
    private bool isPopupVisible;

    public DashboardViewModel()
    {
        _weatherService = WeatherService.Instance;

        RefreshDashboard();

        // Listen for real-time admin advisory pushes
        RealtimeAdvisoryManager.OnNewAdvisoryPushed += (newAdvisory) =>
        {
            SelectedAdvisory = newAdvisory;
            IsPopupVisible = true;

            if (newAdvisory != null)
            {
                RiverStatusText = $"Marikina River: {newAdvisory.DisplayAlertLevel}";
                RiverStatusPillText = newAdvisory.DisplayAlertLevel.ToLower() switch
                {
                    "critical" or "high" => "EVACUATE",
                    "warning" or "moderate" => "ALERT",
                    _ => "Monitoring"
                };
            }
        };

        RealtimeAdvisoryManager.StartRealtimeListener();
    }

    public void RefreshDashboard()
    {
        UserName = Preferences.Get("UserName", "Aubrey");
        PreparednessScore = Preferences.Get("PASS_Score", 100);
        ScoreProgress = PreparednessScore / 100.0;
        PreparednessStatus = Preferences.Get("PASS_Status", "Highly Prepared");

        int hour = DateTime.Now.Hour;
        if (hour < 12) Greeting = "Good morning,";
        else if (hour < 18) Greeting = "Good afternoon,";
        else Greeting = "Good evening,";

        // Load live Open-Meteo weather forecast
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await LoadOpenMeteoWeatherAsync();
        });
    }

    private async Task LoadOpenMeteoWeatherAsync()
    {
        try
        {
            double lat = 14.6340; // Default Marikina / QC
            double lon = 121.0990;

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
                // Permission fallback
            }

            var weatherData = await _weatherService.GetWeatherAsync(lat, lon);
            if (weatherData != null)
            {
                WeatherTemperatureText = $"{weatherData.TemperatureCelsius} °C";
                WeatherConditionTitle = weatherData.ConditionDescription;
                WeatherConditionSummary = weatherData.ConditionSummary;
                WeatherSummary = $"{weatherData.TemperatureCelsius} °C • {weatherData.ConditionDescription}";

                if (!string.IsNullOrWhiteSpace(weatherData.LocationName))
                {
                    LocationName = weatherData.LocationName;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Open-Meteo Weather Load Error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OpenProfileAsync()
    {
        if (Shell.Current != null)
        {
            try
            {
                await Shell.Current.GoToAsync("ProfilePage");
            }
            catch (Exception)
            {
                // Fallback
            }
        }
    }

    [RelayCommand]
    private async Task OpenPASSAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("Prepare/PASS");
        }
    }

    [RelayCommand]
    private async Task OpenChecklistAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("Prepare/Checklist");
        }
    }

    [RelayCommand]
    private async Task OpenEvacuationAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("Prepare/EvacuationCenterInfo");
        }
    }

    [RelayCommand]
    private async Task OpenAdvisoriesAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("AdvisoryFeedPage");
        }
    }

    [RelayCommand]
    private void ClosePopup()
    {
        IsPopupVisible = false;
    }

    [RelayCommand]
    private async Task GoToAdvisoriesFeedAsync()
    {
        IsPopupVisible = false;
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("AdvisoryFeedPage");
        }
    }
}
