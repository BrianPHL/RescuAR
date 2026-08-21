using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using RescuAR.App.Models;
using RescuAR.App.Services.AreaStatus;
using RescuAR.App.Services.Reports;

namespace RescuAR.App.ViewModels.Dashboard;

public partial class AreaStatusOverviewViewModel : ObservableObject
{
    private readonly IAreaStatusService _statusService;
    private readonly AdvisoryService _advisoryService;

    [ObservableProperty]
    public partial string RiskTitle { get; set; } = "Critical Flood Risk";

    [ObservableProperty]
    public partial string AdvisoriesText { get; set; } = "1 active advisory within 0.5 km. Evacuation guidance available.";

    [ObservableProperty]
    public partial string NearestCenterName { get; set; } = "Marikina City Hall";

    [ObservableProperty]
    public partial string NearestCenterDistance { get; set; } = "0.5km";

    [ObservableProperty]
    public partial string RecommendedAction { get; set; } = "Evacuate immediately to safe shelter";

    [ObservableProperty]
    public partial string UpdatedText { get; set; } = "Just now";

    // Styling bindings for exact match
    [ObservableProperty]
    public partial string BackgroundColor { get; set; } = "#FEE2E2";

    [ObservableProperty]
    public partial string TextColor { get; set; } = "#991B1B";

    [ObservableProperty]
    public partial string SubtitleColor { get; set; } = "#B91C1C";

    [ObservableProperty]
    public partial string LabelColor { get; set; } = "#64748B";

    [ObservableProperty]
    public partial string LinkColor { get; set; } = "#0A8491";

    // Dynamic User Coordinates
    public double UserLatitude { get; set; } = 14.6340;
    public double UserLongitude { get; set; } = 121.0990;

    public AreaStatusOverviewViewModel() : this(AreaStatusService.Instance)
    {
    }

    public AreaStatusOverviewViewModel(IAreaStatusService statusService)
    {
        _statusService = statusService;
        _advisoryService = new AdvisoryService();

        // Subscribe to real-time admin advisory updates from Supabase
        RealtimeAdvisoryManager.OnNewAdvisoryPushed += (newAdvisory) =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await LoadRealDataAsync();
            });
        };
        
        // Load data on startup
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(200);
            await LoadRealDataAsync();
        });
    }

    [RelayCommand]
    public async Task LoadRealDataAsync()
    {
        try
        {
            // 1. Fetch real GPS location
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (status == PermissionStatus.Granted)
                {
                    var lastLoc = await Geolocation.Default.GetLastKnownLocationAsync();
                    if (lastLoc != null)
                    {
                        UserLatitude = lastLoc.Latitude;
                        UserLongitude = lastLoc.Longitude;
                    }
                }
            }
            catch (Exception)
            {
                // Permission not granted or acquiring
            }

            // 2. Fetch live advisories from Supabase
            var advisories = await _advisoryService.GetAdvisoriesAsync();
            if (advisories != null && advisories.Count > 0)
            {
                var top = advisories.FirstOrDefault();
                if (top != null)
                {
                    int totalCount = advisories.Count;
                    AdvisoriesText = $"{totalCount} active advisory{(totalCount > 1 ? "ies" : "")} for {top.DisplayAffectedArea}. Evacuation guidance available.";
                    
                    if (top.HasActionPlan)
                    {
                        RecommendedAction = top.DisplayActionPlan;
                    }
                    else
                    {
                        RecommendedAction = top.DisplayAlertLevel.ToLower() switch
                        {
                            "critical" or "level 3" or "evacuate" or "high" or "high severity" => "Evacuate immediately to safe shelter",
                            "warning" or "level 2" or "alarm" or "medium" or "moderate" => "Secure electrical outlets & prepare go-bags",
                            _ => "Monitor water levels & stay alert"
                        };
                    }

                    UpdatedText = top.TimeAgoText;

                    // Update RiskTitle & Theme Colors dynamically based on Supabase advisory
                    string alertLevelLower = top.DisplayAlertLevel.ToLower();
                    if (alertLevelLower.Contains("critical") || alertLevelLower.Contains("level 3") || alertLevelLower.Contains("evacuate") || alertLevelLower.Contains("high"))
                    {
                        RiskTitle = "Critical Flood Risk";
                        UpdateThemeColors(FloodRiskLevel.Critical);
                    }
                    else if (alertLevelLower.Contains("warning") || alertLevelLower.Contains("level 2") || alertLevelLower.Contains("alarm") || alertLevelLower.Contains("medium") || alertLevelLower.Contains("moderate"))
                    {
                        RiskTitle = "Moderate Flood Risk";
                        UpdateThemeColors(FloodRiskLevel.Moderate);
                    }
                    else
                    {
                        RiskTitle = "Low Flood Risk";
                        UpdateThemeColors(FloodRiskLevel.Low);
                    }
                }
            }
            else
            {
                // Fallback to local status
                var areaStatus = _statusService.GetCurrentAreaStatus();
                RiskTitle = $"{areaStatus.RiskLevel} Flood Risk";
                AdvisoriesText = $"{areaStatus.ActiveAdvisoriesCount} active advisories within 0.5 km.";
                RecommendedAction = areaStatus.RecommendedAction;
                UpdatedText = "Just now";
                UpdateThemeColors(areaStatus.RiskLevel);
            }

            // 3. Fetch real nearest evacuation center dynamically for user's GPS coordinates
            var (centerName, distanceMeters) = await _statusService.GetRealNearestEvacuationCenterAsync(UserLatitude, UserLongitude);
            
            if (!string.IsNullOrWhiteSpace(centerName))
            {
                NearestCenterName = centerName.Length > 20 
                    ? centerName.Substring(0, 19) + "..." 
                    : centerName;

                NearestCenterDistance = distanceMeters >= 1000 
                    ? $"{distanceMeters / 1000:F1}km" 
                    : $"{(int)distanceMeters}m";
            }
            else
            {
                NearestCenterName = "Marikina City Hall";
                NearestCenterDistance = "0.5km";
            }
        }
        catch (Exception)
        {
            RiskTitle = "Critical Flood Risk";
            AdvisoriesText = "1 active advisory within 0.5 km. Evacuation guidance available.";
            RecommendedAction = "Evacuate immediately to safe shelter";
            UpdatedText = "Just now";
            UpdateThemeColors(FloodRiskLevel.Critical);
        }
    }

    private void UpdateThemeColors(FloodRiskLevel riskLevel)
    {
        switch (riskLevel)
        {
            case FloodRiskLevel.Low:
                BackgroundColor = "#DCFCE7"; // Soft pastel green
                TextColor = "#166534"; // Dark forest green
                SubtitleColor = "#15803D"; // Green text
                LabelColor = "#64748B"; // Slate gray
                LinkColor = "#0A8491"; // Teal/cyan
                break;

            case FloodRiskLevel.Moderate:
                BackgroundColor = "#FEF3C7"; // Soft amber-yellow
                TextColor = "#92400E"; // Dark amber-brown
                SubtitleColor = "#B45309"; // Amber text
                LabelColor = "#64748B"; // Slate gray
                LinkColor = "#0A8491"; // Teal/cyan
                break;

            case FloodRiskLevel.High:
            case FloodRiskLevel.Critical:
                BackgroundColor = "#FEE2E2"; // Soft pink-red
                TextColor = "#991B1B"; // Dark red
                SubtitleColor = "#B91C1C"; // Red text
                LabelColor = "#64748B"; // Slate gray
                LinkColor = "#0A8491"; // Teal/cyan
                break;
        }
    }

    [RelayCommand]
    private async Task NavigateToEvacuationCenterAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("//Map");
        }
    }

    [RelayCommand]
    private async Task ViewEmergencySummaryAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("AdvisoryFeedPage");
        }
    }

    [RelayCommand]
    private async Task NavigateToSummaryAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("AdvisoryFeedPage");
        }
    }

    [RelayCommand]
    private async Task CycleRiskLevelAsync()
    {
        _statusService.CycleRiskLevel();
        await LoadRealDataAsync();
    }
}
