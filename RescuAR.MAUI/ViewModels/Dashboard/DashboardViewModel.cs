using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Storage;
using RescuAR.App.Models;
using RescuAR.App.Services.Reports;
using RescuAR.App.Services.Weather;

namespace RescuAR.App.ViewModels.Dashboard;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IWeatherService _weatherService;

    [ObservableProperty]
    public partial string UserName { get; set; } = "Aubrey";

    [ObservableProperty]
    public partial string Greeting { get; set; } = "Good day,";

    [ObservableProperty]
    public partial int PreparednessScore { get; set; } = 100;

    [ObservableProperty]
    public partial double ScoreProgress { get; set; } = 1.0;

    [ObservableProperty]
    public partial string PreparednessStatus { get; set; } = "Highly Prepared";

    [ObservableProperty]
    public partial int ActiveAdvisoriesCount { get; set; } = 2;

    [ObservableProperty]
    public partial string WeatherSummary { get; set; } = "24 °C • Clear / Sunny";

    [ObservableProperty]
    public partial string WeatherTemperatureText { get; set; } = "24 °C";

    [ObservableProperty]
    public partial string WeatherConditionTitle { get; set; } = "Clear / Sunny";

    [ObservableProperty]
    public partial string WeatherConditionSummary { get; set; } = "Clear weather conditions in your area";

    [ObservableProperty]
    public partial string LocationName { get; set; } = "Quezon City, Metro Manila";

    [ObservableProperty]
    public partial string FloodRiskLevel { get; set; } = "Moderate Flood Risk";

    [ObservableProperty]
    public partial string RiverStatusText { get; set; } = "Marikina River: Level 1 (Standby)";

    [ObservableProperty]
    public partial string RiverStatusPillText { get; set; } = "Monitoring";

    [ObservableProperty]
    public partial DisasterAdvisory? SelectedAdvisory { get; set; }

    [ObservableProperty]
    public partial bool IsPopupVisible { get; set; }

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
