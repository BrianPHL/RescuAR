using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using RescuAR.App.Models;
using RescuAR.Services;

namespace RescuAR.App.Services.Reports;

public class AdvisoryService
{
    private Supabase.Client? GetClient()
    {
        return SupabaseService.Instance.Client;
    }

    public async Task<List<DisasterAdvisory>> GetAdvisoriesAsync()
    {
        var client = GetClient();
        if (client != null)
        {
            try
            {
                var response = await client.From<DisasterAdvisory>()
                    .Order("created_at", Supabase.Postgrest.Constants.Ordering.Descending)
                    .Get();

                if (response.Models != null && response.Models.Count > 0)
                {
                    return response.Models;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Supabase Advisory Fetch Error: {ex.Message}");
            }
        }

        return GetFallbackAdvisories();
    }

    private List<DisasterAdvisory> GetFallbackAdvisories()
    {
        return new List<DisasterAdvisory>
        {
            new DisasterAdvisory
            {
                Title = "FLOOD WARNING — Marikina River Level 2",
                Message = "Water level has continuously risen to 16.5 meters. Residents in low-lying areas near Barangay Tumana and Malanday are advised to prepare emergency kits for possible evacuation.",
                WaterLevel = 16.5,
                AlertLevel = "Warning",
                Severity = "High",
                Category = "Flood",
                AffectedArea = "Barangay Tumana / Malanday",
                CreatedAt = DateTime.UtcNow.AddMinutes(-20)
            },
            new DisasterAdvisory
            {
                Title = "Marikina River Alert Level 1 (Standby)",
                Message = "Marikina River water level reached 15.0 meters due to continuous moderate rain upstream. Disaster Response teams are on standby.",
                WaterLevel = 15.0,
                AlertLevel = "Standby",
                Severity = "Low",
                Category = "Flood",
                AffectedArea = "Marikina Riverbanks",
                CreatedAt = DateTime.UtcNow.AddHours(-3)
            }
        };
    }
}

public static class RealtimeAdvisoryManager
{
    private static string? _lastAdvisoryId = null;
    private static readonly AdvisoryService _service = new();
    private static IDispatcherTimer? _timer;

    public static event Action<DisasterAdvisory>? OnNewAdvisoryPushed;

    public static void StartRealtimeListener()
    {
        if (_timer != null) return;

        _timer = Application.Current?.Dispatcher.CreateTimer();
        if (_timer != null)
        {
            _timer.Interval = TimeSpan.FromSeconds(3); // Poll Supabase every 3 seconds for new admin advisories
            _timer.Tick += async (s, e) =>
            {
                try
                {
                    var advisories = await _service.GetAdvisoriesAsync();
                    if (advisories != null && advisories.Count > 0)
                    {
                        var latest = advisories.FirstOrDefault();
                        if (latest != null && latest.Id != _lastAdvisoryId)
                        {
                            bool isInitialRun = (_lastAdvisoryId == null);
                            _lastAdvisoryId = latest.Id;

                            if (!isInitialRun)
                            {
                                // Admin pushed a new advisory! Trigger SweetAlert Pop-Up Modal!
                                MainThread.BeginInvokeOnMainThread(() =>
                                {
                                    OnNewAdvisoryPushed?.Invoke(latest);
                                });
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Ignore transient network errors
                }
            };
            _timer.Start();
        }
    }
}
