using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Maui.Devices.Sensors;
using RescuAR.App.Models;

namespace RescuAR.App.Services.Weather;

public interface IWeatherService
{
    Task<WeatherData> GetWeatherAsync(double latitude, double longitude);
    Task<string> GetLocationNameAsync(double latitude, double longitude);
}

public class WeatherService : IWeatherService
{
    public static IWeatherService Instance { get; set; } = new WeatherService();

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<string> GetLocationNameAsync(double latitude, double longitude)
    {
        try
        {
            var placemarks = await Geocoding.Default.GetPlacemarksAsync(latitude, longitude);
            var placemark = placemarks?.FirstOrDefault();
            if (placemark != null)
            {
                var subLocality = placemark.SubLocality;
                var locality = placemark.Locality;
                var adminArea = placemark.AdminArea;

                if (!string.IsNullOrWhiteSpace(subLocality) && !string.IsNullOrWhiteSpace(locality))
                {
                    return $"{subLocality}, {locality}";
                }
                if (!string.IsNullOrWhiteSpace(locality) && !string.IsNullOrWhiteSpace(adminArea))
                {
                    return $"{locality}, {adminArea}";
                }
                if (!string.IsNullOrWhiteSpace(locality))
                {
                    return locality;
                }
                if (!string.IsNullOrWhiteSpace(placemark.FeatureName))
                {
                    return placemark.FeatureName;
                }
            }
        }
        catch (Exception)
        {
        }

        try
        {
            var geoUrl = $"https://nominatim.openstreetmap.org/reverse?lat={latitude:F4}&lon={longitude:F4}&format=json";
            using var req = new HttpRequestMessage(HttpMethod.Get, geoUrl);
            req.Headers.Add("User-Agent", "RescuAR-MobileApp/1.0");
            var resp = await _httpClient.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("address", out var addr))
                {
                    string city = addr.TryGetProperty("city", out var c) ? c.GetString() ?? "" :
                                 addr.TryGetProperty("town", out var t) ? t.GetString() ?? "" :
                                 addr.TryGetProperty("suburb", out var s) ? s.GetString() ?? "" : "";
                    string state = addr.TryGetProperty("state", out var st) ? st.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(city))
                    {
                        return !string.IsNullOrEmpty(state) ? $"{city}, {state}" : city;
                    }
                }
            }
        }
        catch (Exception)
        {
        }

        return $"{latitude:F2}°, {longitude:F2}°";
    }

    public async Task<WeatherData> GetWeatherAsync(double latitude, double longitude)
    {
        string locationName = await GetLocationNameAsync(latitude, longitude);

        try
        {
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude:F4}&longitude={longitude:F4}&current=temperature_2m,weather_code&timezone=auto";
            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("current", out var current))
                {
                    double temp = current.GetProperty("temperature_2m").GetDouble();
                    int code = current.GetProperty("weather_code").GetInt32();
                    return CreateWeatherData(temp, code, locationName);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RESCUAR_WEATHER_ERROR: {ex}");
#if ANDROID
            Android.Util.Log.Error("RESCUAR_WEATHER", ex.ToString());
#endif
        }

        return CreateWeatherData(24.0, 0, locationName);
    }

    private WeatherData CreateWeatherData(double temp, int code, string locationName)
    {
        var data = new WeatherData
        {
            LocationName = locationName,
            TemperatureCelsius = Math.Round(temp),
            WeatherCode = code
        };

        if (code >= 95)
        {
            data.ConditionDescription = "Thunderstorm";
            data.ConditionSummary = "Thunderstorms expected in your area";
            data.IconPathData = "M19.35,10.04C18.67,6.59,15.64,4,12,4C9.11,4,6.6,5.64,5.35,8.04C2.34,8.36,0,10.91,0,14c0,3.31,2.69,6,6,6h13c2.76,0,5-2.24,5-5C24,12.36,21.95,10.22,19.35,10.04z M11,21.5H9L12,15.5H10L13,9.5";
        }
        else if (code >= 60 || code == 80 || code == 81 || code == 82)
        {
            data.ConditionDescription = code >= 65 ? "Heavy Rain" : "Rain Showers";
            data.ConditionSummary = "Precipitation expected in your area";
            data.IconPathData = "M19.35,10.04C18.67,6.59,15.64,4,12,4C9.11,4,6.6,5.64,5.35,8.04C2.34,8.36,0,10.91,0,14c0,3.31,2.69,6,6,6h13c2.76,0,5-2.24,5-5C24,12.36,21.95,10.22,19.35,10.04z M8,22L6,26M12,22L10,26M16,22L14,26";
        }
        else if (code >= 51 && code <= 55)
        {
            data.ConditionDescription = "Drizzle";
            data.ConditionSummary = "Light drizzle in your area";
            data.IconPathData = "M19.35,10.04C18.67,6.59,15.64,4,12,4C9.11,4,6.6,5.64,5.35,8.04C2.34,8.36,0,10.91,0,14c0,3.31,2.69,6,6,6h13c2.76,0,5-2.24,5-5C24,12.36,21.95,10.22,19.35,10.04z M9,22L8,24M15,22L14,24";
        }
        else if (code >= 45 && code <= 48)
        {
            data.ConditionDescription = "Foggy";
            data.ConditionSummary = "Reduced visibility in your area";
            data.IconPathData = "M19.35,10.04C18.67,6.59,15.64,4,12,4C9.11,4,6.6,5.64,5.35,8.04C2.34,8.36,0,10.91,0,14c0,3.31,2.69,6,6,6h13c2.76,0,5-2.24,5-5C24,12.36,21.95,10.22,19.35,10.04z M3,22H21M5,25H19";
        }
        else if (code >= 1 && code <= 3)
        {
            data.ConditionDescription = code == 3 ? "Overcast" : "Partly Cloudy";
            data.ConditionSummary = "Cloudy skies through the day";
            data.IconPathData = "M19.35,10.04C18.67,6.59,15.64,4,12,4C9.11,4,6.6,5.64,5.35,8.04C2.34,8.36,0,10.91,0,14c0,3.31,2.69,6,6,6h13c2.76,0,5-2.24,5-5C24,12.36,21.95,10.22,19.35,10.04z";
        }
        else
        {
            data.ConditionDescription = "Clear / Sunny";
            data.ConditionSummary = "Clear weather conditions in your area";
            data.IconPathData = "M12,7c-2.76,0-5,2.24-5,5s2.24,5,5,5s5-2.24,5-5S14.76,7,12,7L12,7z M2,13h2c0.55,0,1-0.45,1-1s-0.45-1-1-1H2c-0.55,0-1,0.45-1,1S1.45,13,2,13L2,13z M20,13h2c0.55,0,1-0.45,1-1s-0.45-1-1-1h-2c-0.55,0-1,0.45-1,1S19.45,20,20,13L20,13z M11,2v2c0,0.55,0.45,1,1,1s1-0.45,1-1V2c0-0.55-0.45-1-1-1S11,1.45,11,2L11,2z M11,20v2c0,0.55,0.45,1,1,1s1-0.45,1-1v-2c0-0.55-0.45-1-1-1S11,19.45,11,20L11,20z M5.99,4.58c-0.39-0.39-1.03-0.39-1.41,0s-0.39,1.03,0,1.41l1.06,1.06c0.39,0.39,1.03,0.39,1.41,0s0.39-1.03,0-1.41L5.99,4.58L5.99,4.58z M18.36,16.95c-0.39-0.39-1.03-0.39-1.41,0s-0.39,1.03,0,1.41l1.06,1.06c0.39,0.39,1.03,0.39,1.41,0s0.39-1.03,0-1.41L18.36,16.95L18.36,16.95z M7.05,18.36l-1.06,1.06c-0.39,0.39-0.39,1.02,0,1.41s1.02,0.39,1.41,0l1.06-1.06c0.39-0.39,0.39-1.02,0-1.41S7.44,17.97,7.05,18.36L7.05,18.36z M16.95,5.99l1.06-1.06c0.39-0.39,0.39-1.03,0-1.41s-1.03-0.39-1.41,0l-1.06,1.06c-0.39,0.39-0.39,1.03,0,1.41S16.57,6.38,16.95,5.99L16.95,5.99z";
        }

        return data;
    }
}
