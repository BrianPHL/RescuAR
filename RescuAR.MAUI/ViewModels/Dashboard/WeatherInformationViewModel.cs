using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using RescuAR.App.Models;
using RescuAR.App.Services.Weather;

namespace RescuAR.App.ViewModels.Dashboard;

public partial class WeatherInformationViewModel : ObservableObject
{
    private readonly IWeatherService _weatherService;

    [ObservableProperty]
    public partial string LocationName { get; set; } = "Tap to load weather...";

    [ObservableProperty]
    public partial string TemperatureDisplay { get; set; } = "-- °C";

    [ObservableProperty]
    public partial string ConditionDescription { get; set; } = "Location weather";

    [ObservableProperty]
    public partial string ConditionSummary { get; set; } = "Tap to grant location permission & view forecast";

    [ObservableProperty]
    public partial string IconPathData { get; set; } = "M12,7c-2.76,0-5,2.24-5,5s2.24,5,5,5s5-2.24,5-5S14.76,7,12,7L12,7z";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool HasLocationPermission { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public WeatherInformationViewModel() : this(WeatherService.Instance)
    {
    }

    public WeatherInformationViewModel(IWeatherService weatherService)
    {
        _weatherService = weatherService;

        // Safely schedule location request on MainThread after UI initialization
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(500); // Give MAUI Activity window time to attach
            await RequestLocationAndRefreshAsync();
        });
    }

    [RelayCommand]
    public async Task RequestLocationAndRefreshAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        try
        {
            PermissionStatus status = PermissionStatus.Unknown;

            // Safely check and request permission on Android UI thread
            try
            {
                status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                }
            }
            catch (Exception)
            {
                // Activity context not ready or permission dialog suppressed
            }

            if (status == PermissionStatus.Granted)
            {
                HasLocationPermission = true;

                try
                {
                    var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5));
                    var location = await Geolocation.Default.GetLocationAsync(request);
                    
                    if (location != null)
                    {
                        Latitude = location.Latitude;
                        Longitude = location.Longitude;
                    }
                    else
                    {
                        var lastLocation = await Geolocation.Default.GetLastKnownLocationAsync();
                        if (lastLocation != null)
                        {
                            Latitude = lastLocation.Latitude;
                            Longitude = lastLocation.Longitude;
                        }
                    }
                }
                catch (Exception)
                {
                    // Geolocation sensor unavailable
                }

                if (Latitude != 0 || Longitude != 0)
                {
                    var weather = await _weatherService.GetWeatherAsync(Latitude, Longitude);
                    if (weather != null)
                    {
                        LocationName = weather.LocationName;
                        TemperatureDisplay = $"{weather.TemperatureCelsius} °C";
                        ConditionDescription = weather.ConditionDescription;
                        ConditionSummary = weather.ConditionSummary;
                        IconPathData = weather.IconPathData;
                        return;
                    }
                }
                else
                {
                    // Fallback to default location weather if GPS signal is acquiring
                    var defaultWeather = await _weatherService.GetWeatherAsync(14.6507, 121.1029);
                    if (defaultWeather != null)
                    {
                        LocationName = defaultWeather.LocationName;
                        TemperatureDisplay = $"{defaultWeather.TemperatureCelsius} °C";
                        ConditionDescription = defaultWeather.ConditionDescription;
                        ConditionSummary = defaultWeather.ConditionSummary;
                        IconPathData = defaultWeather.IconPathData;
                        return;
                    }
                }
            }
            else
            {
                HasLocationPermission = false;
                LocationName = "Location Permission Needed";
                TemperatureDisplay = "-- °C";
                ConditionDescription = "Location Access Disabled";
                ConditionSummary = "Tap here to grant location permission to view your local weather forecast.";
            }
        }
        catch (Exception)
        {
            LocationName = "Location Unavailable";
            ConditionDescription = "Could not load weather";
            ConditionSummary = "Tap here to refresh weather.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
