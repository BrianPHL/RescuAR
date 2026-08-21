using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RescuAR.App.Models;

namespace RescuAR.App.Services.AreaStatus;

public interface IAreaStatusService
{
    AreaStatusInfo GetCurrentAreaStatus();
    void CycleRiskLevel();
    (EvacuationCenter Center, double DistanceInMeters) GetNearestEvacuationCenter(double userLat, double userLon);
    Task<(string CenterName, double DistanceMeters)> GetRealNearestEvacuationCenterAsync(double userLat, double userLon);
}

public class AreaStatusService : IAreaStatusService
{
    public static IAreaStatusService Instance { get; set; } = new AreaStatusService();

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private int _riskCycleIndex = 1; // 0 = Low, 1 = Moderate, 2 = High, 3 = Critical

    public AreaStatusInfo GetCurrentAreaStatus()
    {
        var status = new AreaStatusInfo
        {
            UpdatedAt = DateTime.Now.AddMinutes(-3)
        };

        switch (_riskCycleIndex)
        {
            case 0:
                status.RiskLevel = FloodRiskLevel.Low;
                status.ActiveAdvisoriesCount = 0;
                status.AlertRangeKm = 5.0;
                status.RecommendedAction = "No immediate action required";
                break;
            case 1:
                status.RiskLevel = FloodRiskLevel.Moderate;
                status.ActiveAdvisoriesCount = 2;
                status.AlertRangeKm = 3.0;
                status.RecommendedAction = "Prepare for possible evacuation";
                break;
            case 2:
                status.RiskLevel = FloodRiskLevel.High;
                status.ActiveAdvisoriesCount = 4;
                status.AlertRangeKm = 1.5;
                status.RecommendedAction = "Prepare for immediate evacuation";
                break;
            case 3:
                status.RiskLevel = FloodRiskLevel.Critical;
                status.ActiveAdvisoriesCount = 6;
                status.AlertRangeKm = 0.5;
                status.RecommendedAction = "Evacuate immediately to safe shelter";
                break;
        }

        return status;
    }

    public void CycleRiskLevel()
    {
        _riskCycleIndex = (_riskCycleIndex + 1) % 4;
    }

    public async Task<(string CenterName, double DistanceMeters)> GetRealNearestEvacuationCenterAsync(double userLat, double userLon)
    {
        // 1. Try real OpenStreetMap Overpass query for emergency shelter / barangay hall / school near user coordinates
        try
        {
            var query = $"[out:json][timeout:5];(node(around:5000,{userLat:F4},{userLon:F4})[amenity~\"shelter|townhall|community_centre|school\"];way(around:5000,{userLat:F4},{userLon:F4})[amenity~\"shelter|townhall|community_centre|school\"];);out center 5;";
            var url = $"https://overpass-api.de/api/interpreter?data={Uri.EscapeDataString(query)}";
            
            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("elements", out var elements) && elements.GetArrayLength() > 0)
                {
                    string bestName = string.Empty;
                    double minDistance = double.MaxValue;

                    foreach (var elem in elements.EnumerateArray())
                    {
                        double lat = elem.TryGetProperty("lat", out var l) ? l.GetDouble() :
                                    elem.TryGetProperty("center", out var c) && c.TryGetProperty("lat", out var cl) ? cl.GetDouble() : 0;
                        double lon = elem.TryGetProperty("lon", out var ln) ? ln.GetDouble() :
                                    elem.TryGetProperty("center", out var c2) && c2.TryGetProperty("lon", out var cln) ? cln.GetDouble() : 0;

                        if (lat == 0 || lon == 0) continue;

                        string name = string.Empty;
                        if (elem.TryGetProperty("tags", out var tags))
                        {
                            if (tags.TryGetProperty("name", out var n)) name = n.GetString() ?? "";
                            else if (tags.TryGetProperty("amenity", out var a)) name = $"{a.GetString()} Shelter";
                        }

                        if (string.IsNullOrWhiteSpace(name)) name = "Local Evacuation Shelter";

                        double dist = CalculateDistance(userLat, userLon, lat, lon);
                        if (dist < minDistance)
                        {
                            minDistance = dist;
                            bestName = name;
                        }
                    }

                    if (!string.IsNullOrEmpty(bestName) && minDistance < double.MaxValue)
                    {
                        return (bestName, minDistance);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Fallback to local repository distance calculation
        }

        // 2. Fallback: Calculate true distance to regional evacuation centers repository
        var (center, distanceMeters) = GetNearestEvacuationCenter(userLat, userLon);
        if (center != null)
        {
            return (center.Name, distanceMeters);
        }

        return ("Local Evacuation Center", 320);
    }

    public (EvacuationCenter Center, double DistanceInMeters) GetNearestEvacuationCenter(double userLat, double userLon)
    {
        var centers = EvacuationCenterRepository.GetEvacuationCenters();
        if (centers == null || !centers.Any())
        {
            return (null!, 0);
        }

        EvacuationCenter nearest = null!;
        double minDistance = double.MaxValue;

        foreach (var center in centers)
        {
            double dist = CalculateDistance(userLat, userLon, center.Latitude, center.Longitude);
            if (dist < minDistance)
            {
                minDistance = dist;
                nearest = center;
            }
        }

        return (nearest, minDistance);
    }

    private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var r = 6371e3; // Earth radius in meters
        var phi1 = lat1 * Math.PI / 180;
        var phi2 = lat2 * Math.PI / 180;
        var deltaPhi = (lat2 - lat1) * Math.PI / 180;
        var deltaLambda = (lon2 - lon1) * Math.PI / 180;

        var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) *
                Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return r * c;
    }
}
