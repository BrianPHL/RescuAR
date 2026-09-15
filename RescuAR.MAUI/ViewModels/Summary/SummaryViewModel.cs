using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using RescuAR.App.Models;
using RescuAR.App.Services.AreaStatus;
using RescuAR.App.Services.Reports;

namespace RescuAR.App.ViewModels.Summary;

public partial class ChecklistActionItem : ObservableObject
{
    [ObservableProperty]
    private string text = string.Empty;

    [ObservableProperty]
    private bool isCompleted;

    public string CheckIconColor => IsCompleted ? "#0A8491" : "#CBD5E1";
    public string TextColor => IsCompleted ? "#0F172A" : "#64748B";

    partial void OnIsCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(CheckIconColor));
        OnPropertyChanged(nameof(TextColor));
    }
}

public partial class SummaryViewModel : ObservableObject
{
    private readonly AdvisoryService _advisoryService;
    private readonly IAreaStatusService _statusService;

    [ObservableProperty]
    private string riskBadgeText = "Normal Status";

    [ObservableProperty]
    private string riskTitle = "No Active Emergency Advisories";

    [ObservableProperty]
    private string nearestCenterSummary = "Nearest Evacuation Hub: Marikina City Hall";

    [ObservableProperty]
    private string lastUpdatedText = "Updated just now";

    [ObservableProperty]
    private string situationAssessmentText = "Current weather and river level conditions are normal. Stay informed via Marikina LGU broadcasts.";

    [ObservableProperty]
    private string safeZoneName = "Malanday Elementary School";

    [ObservableProperty]
    private string safeZoneDistance = "0.5 km away";

    [ObservableProperty]
    private string safeZoneAddress = "48 Visayas St., Malanday, Marikina City 1805";

    [ObservableProperty]
    private string safeZoneVerifiedBy = "Verified by Marikina LGU";

    [ObservableProperty]
    private string lastArSession = "Ready to practice";

    [ObservableProperty]
    private string preparednessProgressText = "6 of 10 items prepared";

    [ObservableProperty]
    private string lastViewedCenterText = "Malanday Evacuation Hub";

    [ObservableProperty]
    private string lastViewedAdvisoryText = "Marikina River Gauge";

    // Dynamic Severity Colors
    [ObservableProperty]
    private string badgeBgColor = "#DCFCE7";

    [ObservableProperty]
    private string badgeTextColor = "#166534";

    [ObservableProperty]
    private string cardBgColor = "#FFFFFF";

    public ObservableCollection<ChecklistActionItem> StandardActions { get; } = new();
    public ObservableCollection<ChecklistActionItem> WorsenedConditionsActions { get; } = new();

    public double UserLatitude { get; set; } = 14.6340;
    public double UserLongitude { get; set; } = 121.0990;

    public SummaryViewModel()
    {
        _advisoryService = new AdvisoryService();
        _statusService = AreaStatusService.Instance;

        // Load real live summary data from Supabase
        _ = LoadLiveSummaryAsync();

        // Subscribe to real-time admin advisory updates from Supabase
        RealtimeAdvisoryManager.OnNewAdvisoryPushed += (newAdvisory) =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await LoadLiveSummaryAsync();
            });
        };
    }

    [RelayCommand]
    public async Task LoadLiveSummaryAsync()
    {
        try
        {
            // 1. Fetch real GPS coordinates
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
            catch (Exception) { }

            // 2. Query live advisories from Supabase
            var advisories = await _advisoryService.GetAdvisoriesAsync();
            var topAdvisory = advisories?.FirstOrDefault();

            if (topAdvisory != null)
            {
                RiskTitle = topAdvisory.DisplayTitleText;
                SituationAssessmentText = topAdvisory.DisplayMessageText;
                LastUpdatedText = $"Updated {topAdvisory.TimeAgoText}";

                string severity = topAdvisory.DisplayAlertLevel.ToLower();
                if (severity.Contains("critical") || severity.Contains("level 3") || severity.Contains("evacuate") || severity.Contains("high"))
                {
                    RiskBadgeText = "CRITICAL EMERGENCY ALERT";
                    BadgeBgColor = "#FEE2E2";
                    BadgeTextColor = "#DC2626";
                    CardBgColor = "#FEF2F2";
                }
                else if (severity.Contains("warning") || severity.Contains("level 2") || severity.Contains("alarm") || severity.Contains("medium") || severity.Contains("moderate"))
                {
                    RiskBadgeText = "WARNING — ELEVATED RISK";
                    BadgeBgColor = "#FEF3C7";
                    BadgeTextColor = "#D97706";
                    CardBgColor = "#FFFBEB";
                }
                else
                {
                    RiskBadgeText = "STANDBY ADVISORY";
                    BadgeBgColor = "#E0F2FE";
                    BadgeTextColor = "#0369A1";
                    CardBgColor = "#F0F9FF";
                }

                // Load dynamic recommendations checklist from Supabase
                PopulateChecklistFromAdvisory(topAdvisory);
            }
            else
            {
                RiskBadgeText = "LOW RISK / NORMAL";
                BadgeBgColor = "#DCFCE7";
                BadgeTextColor = "#166534";
                CardBgColor = "#FFFFFF";
                PopulateDefaultChecklist();
            }

            // 3. Fetch nearest safe zone evacuation center dynamically for user's GPS position
            var (centerName, distanceMeters) = await _statusService.GetRealNearestEvacuationCenterAsync(UserLatitude, UserLongitude);
            if (!string.IsNullOrWhiteSpace(centerName))
            {
                SafeZoneName = centerName;
                SafeZoneDistance = distanceMeters >= 1000 
                    ? $"{distanceMeters / 1000:F1} km away" 
                    : $"{(int)distanceMeters} meters away";
                
                NearestCenterSummary = $"Nearest Evacuation Center: {centerName} ({SafeZoneDistance})";
                SafeZoneAddress = $"{centerName}, Marikina City";
                SafeZoneVerifiedBy = "Verified by Marikina Disaster Risk Reduction Management Office (DRRMO)";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SummaryViewModel Load Error: {ex.Message}");
            PopulateDefaultChecklist();
        }
    }

    private void PopulateChecklistFromAdvisory(DisasterAdvisory advisory)
    {
        StandardActions.Clear();
        WorsenedConditionsActions.Clear();

        // Standard Severity Recommendations
        if (!string.IsNullOrWhiteSpace(advisory.DisplayActionPlan))
        {
            var lines = advisory.DisplayActionPlan.Split(new[] { '\n', '•', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                string clean = line.Trim();
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    StandardActions.Add(new ChecklistActionItem { Text = clean, IsCompleted = true });
                }
            }
        }

        if (StandardActions.Count == 0)
        {
            StandardActions.Add(new ChecklistActionItem { Text = "Pack PASS Go-Bag with essential water & food", IsCompleted = true });
            StandardActions.Add(new ChecklistActionItem { Text = "Charge mobile devices and emergency flashlights", IsCompleted = true });
            StandardActions.Add(new ChecklistActionItem { Text = "Monitor live Marikina River gauge updates", IsCompleted = true });
            StandardActions.Add(new ChecklistActionItem { Text = "Confirm family evacuation meeting point", IsCompleted = true });
        }

        // Escalation actions if conditions worsen
        string escalationText = advisory.DisplayEscalationActionsText;
        if (!string.IsNullOrWhiteSpace(escalationText))
        {
            var lines = escalationText.Split(new[] { '\n', '•', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                string clean = line.Trim();
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    WorsenedConditionsActions.Add(new ChecklistActionItem { Text = clean, IsCompleted = false });
                }
            }
        }

        if (WorsenedConditionsActions.Count == 0)
        {
            WorsenedConditionsActions.Add(new ChecklistActionItem { Text = "Initiate immediate evacuation to nearest safe zone", IsCompleted = false });
            WorsenedConditionsActions.Add(new ChecklistActionItem { Text = "Activate AR Evacuation Route Guidance on camera", IsCompleted = false });
            WorsenedConditionsActions.Add(new ChecklistActionItem { Text = "Shut off main electrical breaker and LPG gas valve", IsCompleted = false });
            WorsenedConditionsActions.Add(new ChecklistActionItem { Text = "Contact emergency hotline for rescue assistance", IsCompleted = false });
        }
    }

    private void PopulateDefaultChecklist()
    {
        StandardActions.Clear();
        WorsenedConditionsActions.Clear();

        StandardActions.Add(new ChecklistActionItem { Text = "Keep emergency hotlines saved on your mobile device", IsCompleted = true });
        StandardActions.Add(new ChecklistActionItem { Text = "Maintain PASS Go-Bag readiness (flashlight, water, first-aid)", IsCompleted = true });
        StandardActions.Add(new ChecklistActionItem { Text = "Monitor weather forecasts & Marikina LGU advisories", IsCompleted = true });
        StandardActions.Add(new ChecklistActionItem { Text = "Familiarize yourself with your neighborhood AR evacuation route", IsCompleted = true });

        WorsenedConditionsActions.Add(new ChecklistActionItem { Text = "Proceed immediately to designated evacuation hub", IsCompleted = false });
        WorsenedConditionsActions.Add(new ChecklistActionItem { Text = "Follow AR camera navigation indicators", IsCompleted = false });
        WorsenedConditionsActions.Add(new ChecklistActionItem { Text = "Assist elderly and vulnerable family members", IsCompleted = false });
    }

    [RelayCommand]
    private void ToggleActionItem(ChecklistActionItem item)
    {
        if (item != null)
        {
            item.IsCompleted = !item.IsCompleted;
        }
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("..");
        }
    }

    [RelayCommand]
    private async Task OpenNotificationsAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.DisplayAlert(
                "Emergency Broadcasts",
                "Real-time notifications are active for Marikina River water levels and LGU advisories.",
                "OK");
        }
    }

    [RelayCommand]
    private async Task ViewSafeZoneDetailsAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("Prepare/EvacuationCenterInfo");
        }
    }

    [RelayCommand]
    private async Task StartArEvacuationAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("//Camera");
        }
    }

    [RelayCommand]
    private async Task OpenPreparednessChecklistAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("Prepare/Checklist");
        }
    }

    [RelayCommand]
    private async Task OpenLastViewedCenterAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("Prepare/EvacuationCenterInfo");
        }
    }

    [RelayCommand]
    private async Task OpenLastViewedAdvisoryAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("AdvisoryFeedPage");
        }
    }
}
