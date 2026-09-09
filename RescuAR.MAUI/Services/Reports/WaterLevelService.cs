using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using RescuAR.App.Models;
using RescuAR.Services;

namespace RescuAR.App.Services.Reports;

public static class RealtimeWaterLevelManager
{
    private static int? _lastLogId = null;
    private static IDispatcherTimer? _timer;

    public static event Action<MonitoringStation>? OnWaterLevelUpdated;

    public static async Task<MonitoringStation?> GetLatestWaterLevelAsync()
    {
        var client = SupabaseService.Instance.Client;
        if (client == null) return null;

        try
        {
            var response = await client.From<MonitoringStation>()
                .Filter("station_name", Supabase.Postgrest.Constants.Operator.ILike, "%Sto. Ni%o%")
                .Limit(1)
                .Get();

            return response.Models?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WaterLevel Fetch Error: {ex.Message}");
            return null;
        }
    }

    public static void StartRealtimeListener()
    {
        if (_timer != null) return;

        _timer = Application.Current?.Dispatcher.CreateTimer();
        if (_timer != null)
        {
            _timer.Interval = TimeSpan.FromSeconds(3); // Poll every 3 seconds
            _timer.Tick += async (s, e) =>
            {
                try
                {
                    var latest = await GetLatestWaterLevelAsync();
                    if (latest != null && latest.Id != _lastLogId)
                    {
                        bool isInitialRun = (_lastLogId == null);
                        _lastLogId = latest.Id;

                        if (!isInitialRun)
                        {
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                OnWaterLevelUpdated?.Invoke(latest);
                            });
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
