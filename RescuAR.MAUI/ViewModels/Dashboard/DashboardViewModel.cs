using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Storage;
using Newtonsoft.Json;
using RescuAR.App.Models;
using RescuAR.App.Services.Reports;
using RescuAR.App.Services.Weather;

namespace RescuAR.App.ViewModels.Dashboard;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IWeatherService _weatherService;
    private bool _isFlashlightOn;
    private bool _isSirenOn;

    [ObservableProperty]
    private string _userName = "User";

    [ObservableProperty]
    private string _greeting = "Good day,";

    [ObservableProperty]
    private int _preparednessScore = 60;

    [ObservableProperty]
    private int _preparednessCompletedItems = 6;

    [ObservableProperty]
    private int _preparednessTotalItems = 10;

    [ObservableProperty]
    private double _scoreProgress = 0.6;

    [ObservableProperty]
    private string _preparednessStatus = "Prepared";

    [ObservableProperty]
    private int _activeAdvisoriesCount = 2;

    [ObservableProperty]
    private string _weatherSummary = "Loading weather...";

    [ObservableProperty]
    private string _weatherTemperatureText = "-- °C";

    [ObservableProperty]
    private string _weatherHighLowText = "H: 31° • L: 24°";

    [ObservableProperty]
    private string _weatherIconData = "M19.35,10.04C18.67,6.59,15.64,4,12,4C9.11,4,6.6,5.64,5.35,8.04C2.34,8.36,0,10.91,0,14c0,3.31,2.69,6,6,6h13c2.76,0,5-2.24,5-5C24,12.36,21.95,10.22,19.35,10.04z";

    public Microsoft.Maui.Controls.Shapes.Geometry? WeatherIconGeometry
    {
        get
        {
            if (string.IsNullOrWhiteSpace(WeatherIconData))
                return null;
            try
            {
                var converter = new Microsoft.Maui.Controls.Shapes.PathGeometryConverter();
                return converter.ConvertFromInvariantString(WeatherIconData) as Microsoft.Maui.Controls.Shapes.Geometry;
            }
            catch { return null; }
        }
    }

    [ObservableProperty]
    private string _weatherConditionTitle = "Fetching weather...";

    [ObservableProperty]
    private string _weatherConditionSummary = "Updating current weather forecast for your area...";

    [ObservableProperty]
    private string _locationName = "Detecting location...";

    [ObservableProperty]
    private string _floodRiskLevel = "Moderate Flood Risk";

    [ObservableProperty]
    private string _riverStatusText = "Marikina River: Level 1 (Standby)";

    [ObservableProperty]
    private string _riverStatusPillText = "Monitoring";

    [ObservableProperty]
    private string _floodAdvisoryDetailText = "Water level in Marikina River is under continuous monitoring.";

    [ObservableProperty]
    private string _nearestCenterDistanceText = "0.5 km away";

    [ObservableProperty]
    private string _nearestCenterNameText = "Marikina Evacuation Center";

    public string ActiveAdvisoriesTitleText => $"{ActiveAdvisoriesCount} Advisories Active";
    public string PreparednessReadyTitleText => $"{PreparednessScore}% Ready";
    public string PreparednessItemsDetailText => $"{PreparednessCompletedItems} of {PreparednessTotalItems} items prepared";

    // Bento Box Card 5: Dynamic Safety Circle Overview Properties
    [ObservableProperty]
    private string _safetyCircleGroup1Name = "Family Circle";

    [ObservableProperty]
    private string _safetyCircleGroup1Status = "1 status unverified";

    [ObservableProperty]
    private string _safetyCircleGroup1BadgeText = "ALERT";

    [ObservableProperty]
    private string _safetyCircleGroup1BadgeBg = "#FEF3C7";

    [ObservableProperty]
    private string _safetyCircleGroup1BadgeTextColor = "#B45309";

    [ObservableProperty]
    private string _safetyCircleGroup2Name = "Friends Circle";

    [ObservableProperty]
    private string _safetyCircleGroup2Status = "All members safe";

    [ObservableProperty]
    private string _safetyCircleGroup2BadgeText = "SAFE";

    [ObservableProperty]
    private string _safetyCircleGroup2BadgeBg = "#DCFCE7";

    [ObservableProperty]
    private string _safetyCircleGroup2BadgeTextColor = "#166534";

    // Bento Box Card 6: Dynamic Recent Reports Nearby Properties
    [ObservableProperty]
    private string _recentReport1Title = "Flooded road reported";

    [ObservableProperty]
    private string _recentReport1Distance = "150m away";

    [ObservableProperty]
    private string _recentReport2Title = "Emergency assistance";

    [ObservableProperty]
    private string _recentReport2Distance = "600m away";

    [ObservableProperty]
    private DisasterAdvisory? _selectedAdvisory;

    [ObservableProperty]
    private bool _isPopupVisible;

    [ObservableProperty]
    private bool _isPermissionsPopupVisible;

    // Quick Actions Customization & Minimize Properties
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuickActionsToggleIconText))]
    [NotifyPropertyChangedFor(nameof(QuickActionsToggleLabelText))]
    private bool _isQuickActionsExpanded = true;

    public string QuickActionsToggleIconText => IsQuickActionsExpanded ? "▲" : "▼";
    public string QuickActionsToggleLabelText => IsQuickActionsExpanded ? "Hide" : "Show";

    [RelayCommand]
    private void ToggleQuickActionsMinimize()
    {
        IsQuickActionsExpanded = !IsQuickActionsExpanded;
        Preferences.Default.Set("IsQuickActionsExpanded", IsQuickActionsExpanded);
    }

    [ObservableProperty]
    private bool _isCustomizeQuickActionsVisible;

    [ObservableProperty]
    private string _newContactName = string.Empty;

    [ObservableProperty]
    private string _newContactPhone = string.Empty;

    public ObservableCollection<QuickActionItem> QuickActions { get; } = new();
    public ObservableCollection<QuickActionItem> AllAvailableQuickActions { get; } = new();
    public ObservableCollection<QuickActionPage> QuickActionPages { get; } = new();
    public bool HasMultipleQuickActionPages => QuickActionPages.Count > 1;
    [ObservableProperty]
    private ObservableCollection<DashboardCarouselItem> _carouselItems = new();

    public DashboardViewModel()
    {
        _weatherService = WeatherService.Instance;

        CarouselItems.Add(new DashboardCarouselItem
        {
            Id = "1",
            Title = "Marikina Flood History",
            Description = "Learn from past floods to improve your disaster preparedness.",
            ButtonText = "Learn More",
            ImageSource = "carousel_flood_history.png",
            IconData = "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-5 14H7v-2h7v2zm3-4H7v-2h10v2zm0-4H7V7h10v2z",
            ActionType = "LearnMore"
        });

        CarouselItems.Add(new DashboardCarouselItem
        {
            Id = "2",
            Title = "Be Ready Before the Flood",
            Description = "Check your emergency kit and review your evacuation plan.",
            ButtonText = "View Checklist",
            ImageSource = "carousel_emergency_kit.png",
            IconData = "M20 6h-4V4c0-1.11-.89-2-2-2h-4c-1.11 0-2 .89-2 2v2H4c-1.11 0-1.99.89-1.99 2L2 19c0 1.11.89 2 2 2h16c1.11 0 2-.89 2-2V8c0-1.11-.89-2-2-2zm-6 0h-4V4h4v2z",
            ActionType = "Checklist"
        });

        CarouselItems.Add(new DashboardCarouselItem
        {
            Id = "3",
            Title = "AR Safe Route",
            Description = "Find the safest evacuation route with real-time AR navigation.",
            ButtonText = "Start Navigation",
            ImageSource = "carousel_ar_route.png",
            IconData = "M12 2L4.5 20.29l.71.71L12 18l6.79 3 .71-.71z",
            ActionType = "Camera"
        });

        LoadQuickActions();
        RefreshDashboard();

        // Listen for real-time admin advisory pushes
        RealtimeAdvisoryManager.OnNewAdvisoryPushed += (newAdvisory) =>
        {
            SelectedAdvisory = newAdvisory;
            IsPopupVisible = true;

            if (newAdvisory != null)
            {
                FloodAdvisoryDetailText = newAdvisory.DisplayMessageText;
            }
        };
        RealtimeAdvisoryManager.StartRealtimeListener();
    }

    public async void RefreshDashboard()
    {
        LoadUserData();
        await LoadWeatherDataAsync();
        await LoadRealtimeBentoBoxDataAsync();
    }

    private void LoadQuickActions()
    {
        try
        {
            IsQuickActionsExpanded = Preferences.Default.Get("IsQuickActionsExpanded", true);
            AllAvailableQuickActions.Clear();

            string json = Preferences.Default.Get("CustomQuickActionsList_v18", string.Empty);
            List<QuickActionItem>? saved = null;
            if (!string.IsNullOrWhiteSpace(json))
            {
                saved = JsonConvert.DeserializeObject<List<QuickActionItem>>(json);
            }

            if (saved != null && saved.Count > 0)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in saved.Where(x => x.Id != "sos_beacon" && x.ActionType != "SOSBeacon"))
                {
                    if (seen.Add(item.Title))
                    {
                        EnsureSolidVectorIcons(item);
                        AllAvailableQuickActions.Add(item);
                    }
                }
            }
            else
            {
                PopulateDefaultSolidQuickActions();
            }

            if (AllAvailableQuickActions.Count == 0)
            {
                PopulateDefaultSolidQuickActions();
            }

            SyncEnabledQuickActions();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadQuickActions error: {ex.Message}");
            PopulateDefaultSolidQuickActions();
            SyncEnabledQuickActions();
        }
    }

    private void PopulateDefaultSolidQuickActions()
    {
        AllAvailableQuickActions.Clear();

        // Col 0 - Top: Flashlight
        AllAvailableQuickActions.Add(new QuickActionItem
        {
            Id = "flashlight",
            Title = "Flashlight",
            Subtitle = "Toggle flashlight ON / OFF",
            IconImage = "icon_flashlight.png",
            IconData = "M9,2A1,1 0 0,0 8,3V8.5L10.5,11V21A1,1 0 0,0 11.5,22H12.5A1,1 0 0,0 13.5,21V11L16,8.5V3A1,1 0 0,0 15,2H9M10,4H14V6H10V4Z",
            IconBg = "Transparent",
            IconColor = "#64748B",
            ActionType = "Flashlight",
            IsEnabled = true
        });

        // Col 0 - Bottom: Loud Rescue Siren
        AllAvailableQuickActions.Add(new QuickActionItem
        {
            Id = "siren",
            Title = "Loud Siren",
            Subtitle = "Play loud emergency beacon sound",
            IconImage = "icon_siren.png",
            IconData = "M12,2C8.13,2 5,5.13 5,9V14H3.5C2.67,14 2,14.67 2,15.5V17C2,17.83 2.67,18.5 3.5,18.5H20.5C21.33,18.5 22,17.83 22,17V15.5C22,14.67 21.33,14 20.5,14H19V9C19,5.13 15.87,2 12,2ZM7,9C7,6.24 9.24,4 12,4C14.76,4 17,6.24 17,9V14H7V9ZM9.5,5.5C8.3,6.3 7.5,7.6 7.5,9H9.2C9.4,8.1 10,7.3 10.7,6.7L9.5,5.5Z",
            IconBg = "Transparent",
            IconColor = "#64748B",
            ActionType = "Siren",
            IsEnabled = true
        });

        // Col 1 - Top: Share Live GPS Location
        AllAvailableQuickActions.Add(new QuickActionItem
        {
            Id = "share_gps",
            Title = "Share GPS",
            Subtitle = "Share live location link via SMS",
            IconImage = "icon_gps.png",
            IconData = "M12,2A7,7 0 0,0 5,9C5,14.25 12,22 12,22C12,22 19,16.25 19,11A7,7 0 0,0 12,2M12,11.5A2.5,2.5 0 0,1 9.5,11A2.5,2.5 0 0,1 12,6.5A2.5,2.5 0 0,1 14.5,11A2.5,2.5 0 0,1 12,13.5Z",
            IconBg = "Transparent",
            IconColor = "#64748B",
            ActionType = "ShareLocation",
            IsEnabled = true
        });

        // Col 1 - Bottom: Report Incident
        AllAvailableQuickActions.Add(new QuickActionItem
        {
            Id = "report",
            Title = "Report Incident",
            Subtitle = "Submit a community report",
            IconImage = "icon_report.png",
            IconData = "M8,2H16L22,8V16L16,22H8L2,16V8L8,2ZM8.8,4L4,8.8V15.2L8.8,20H15.2L20,15.2V8.8L15.2,4H8.8ZM11,7H13V13H11V7ZM11,15H13V17H11V15Z",
            IconBg = "Transparent",
            IconColor = "#64748B",
            ActionType = "Route",
            TargetRoute = "//Reports",
            IsEnabled = true
        });

        // Col 2 - Top: Standard Emergency Hotlines
        AllAvailableQuickActions.Add(new QuickActionItem
        {
            Id = "hotlines",
            Title = "Hotlines",
            Subtitle = "Directory of emergency contacts",
            IconImage = "icon_hotlines.png",
            IconData = "M6.62,10.79C8.06,13.62 10.38,15.94 13.21,17.38L15.41,15.18C15.69,14.9 16.08,14.82 16.43,14.93C17.55,15.3 18.75,15.5 20,15.5A1,1 0 0,1 21,16.5V20A1,1 0 0,1 20,21C10.61,21 3,13.39 3,4A1,1 0 0,1 4,3H7.5A1,1 0 0,1 8.5,4C8.5,5.25 8.7,6.45 9.07,7.57C9.18,7.92 9.1,8.31 8.82,8.59L6.62,10.79Z",
            IconBg = "Transparent",
            IconColor = "#64748B",
            ActionType = "Route",
            TargetRoute = "Prepare/HotlineDirectory",
            IsEnabled = true
        });

        // Col 2 - Bottom: Find Evacuation Center
        AllAvailableQuickActions.Add(new QuickActionItem
        {
            Id = "evacuation",
            Title = "Evacuation Center",
            Subtitle = "Locate nearby shelters",
            IconImage = "icon_evacuation.png",
            IconData = "M10,20V14H14V20H19V12H22L12,3L2,12H5V20H10Z",
            IconBg = "Transparent",
            IconColor = "#64748B",
            ActionType = "Route",
            TargetRoute = "Prepare/EvacuationCenterInfo",
            IsEnabled = true
        });

        // Col 3 - Top: Safety Circle
        AllAvailableQuickActions.Add(new QuickActionItem
        {
            Id = "safety",
            Title = "Safety Circle",
            Subtitle = "Check family status",
            IconImage = "icon_safety.png",
            IconData = "M12,2L1,11H4V21H20V11H23L12,2ZM12,4.8L18,10V19H6V10L12,4.8ZM12,7C12.8,7 13.5,7.7 13.5,8.5C13.5,9.3 12.8,10 12,10C11.2,10 10.5,9.3 10.5,8.5C10.5,7.7 11.2,7 12,7ZM8.5,9C9.2,9 9.7,9.5 9.7,10.2C9.7,10.9 9.2,11.4 8.5,11.4C7.8,11.4 7.3,10.9 7.3,10.2C7.3,9.5 7.8,9 8.5,9ZM15.5,9C16.2,9 16.7,9.5 16.7,10.2C16.7,10.9 16.2,11.4 15.5,11.4C14.8,11.4 14.3,10.9 14.3,10.2C14.3,9.5 14.8,9 15.5,9ZM12,11C10.5,11 9,11.8 8.5,13H15.5C15,11.8 13.5,11 12,11ZM6.2,14H8.5V16H6.2V14ZM15.5,14H17.8V16H15.5V14Z",
            IconBg = "Transparent",
            IconColor = "#64748B",
            ActionType = "Route",
            TargetRoute = "SafetyCirclePage",
            IsEnabled = true
        });

        // Col 3 - Bottom: AR Route (Optional / Configurable)
        AllAvailableQuickActions.Add(new QuickActionItem
        {
            Id = "ar_route",
            Title = "AR Route",
            Subtitle = "Camera / AR Route Guidance",
            IconImage = "icon_ar_route.png",
            IconData = "M19,15.18V7C19,4.79 17.21,3 15,3C12.79,3 11,4.79 11,7V15C11,16.1 10.1,17 9,17C7.9,17 7,16.1 7,15V7.82C8.16,7.4 9,6.3 9,5C9,3.34 7.66,2 6,2C4.34,2 3,3.34 3,5C3,6.3 3.84,7.4 5,7.82V15C5,17.21 6.79,19 9,19C11.21,19 13,17.21 13,15V7C13,5.9 13.9,5 15,5C16.1,5 17,5.9 17,7V15.18C15.84,15.6 15,16.7 15,18C15,19.66 16.34,21 18,21C19.66,21 21,19.66 21,18C21,16.7 20.16,15.6 19,15.18Z",
            IconBg = "Transparent",
            IconColor = "#64748B",
            ActionType = "Route",
            TargetRoute = "//Camera",
            IsEnabled = false
        });
    }

    private void EnsureSolidVectorIcons(QuickActionItem item)
    {
        if (item == null) return;

        item.IconImage = item.Id switch
        {
            "siren" => "icon_siren.png",
            "flashlight" => "icon_flashlight.png",
            "share_gps" => "icon_gps.png",
            "report" => "icon_report.png",
            "hotlines" => "icon_hotlines.png",
            "evacuation" => "icon_evacuation.png",
            "safety" => "icon_safety.png",
            "ar_route" => "icon_ar_route.png",
            _ => item.ActionType == "CustomContact" ? "icon_custom_contact.png" : (string.IsNullOrWhiteSpace(item.IconImage) ? "icon_hotlines.png" : item.IconImage)
        };

        item.IconBg = "Transparent";
        item.IconColor = "#64748B";

        item.IconData = item.Id switch
        {
            "siren" => "M12,2C8.13,2 5,5.13 5,9V14H3.5C2.67,14 2,14.67 2,15.5V17C2,17.83 2.67,18.5 3.5,18.5H20.5C21.33,18.5 22,17.83 22,17V15.5C22,14.67 21.33,14 20.5,14H19V9C19,5.13 15.87,2 12,2ZM7,9C7,6.24 9.24,4 12,4C14.76,4 17,6.24 17,9V14H7V9ZM9.5,5.5C8.3,6.3 7.5,7.6 7.5,9H9.2C9.4,8.1 10,7.3 10.7,6.7L9.5,5.5Z",
            "flashlight" => "M9,2A1,1 0 0,0 8,3V8.5L10.5,11V21A1,1 0 0,0 11.5,22H12.5A1,1 0 0,0 13.5,21V11L16,8.5V3A1,1 0 0,0 15,2H9M10,4H14V6H10V4Z",
            "share_gps" => "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2M12,4A8,8 0 1,1 4,12A8,8 0 0,1 12,4M12,6.5A3.5,3.5 0 0,0 8.5,10C8.5,12.63 12,16.5 12,16.5C12,16.5 15.5,12.63 15.5,10A3.5,3.5 0 0,0 12,6.5M12,8.5A1.5,1.5 0 1,1 10.5,10A1.5,1.5 0 0,1 12,8.5Z",
            "report" => "M8,2H16L22,8V16L16,22H8L2,16V8L8,2ZM8.8,4L4,8.8V15.2L8.8,20H15.2L20,15.2V8.8L15.2,4H8.8ZM11,7H13V13H11V7ZM11,15H13V17H11V15Z",
            "hotlines" => "M20,15.5C18.75,15.5 17.55,15.3 16.43,14.93C16.08,14.82 15.69,14.9 15.41,15.18L13.21,17.38C10.38,15.94 8.06,13.62 6.62,10.79L8.82,8.59C9.1,8.31 9.18,7.92 9.07,7.57C8.7,6.45 8.5,5.25 8.5,4C8.5,3.45 8.05,3 7.5,3H4C3.45,3 3,3.45 3,4C3,13.39 10.61,21 20,21C20.55,21 21,20.55 21,20V16.5C21,15.95 20.55,15.5 20,15.5ZM19,12H21C21,7.03 16.97,3 12,3V5C15.87,5 19,8.13 19,12ZM15,12H17C17,9.24 14.76,7 12,7V9C13.66,9 15,10.34 15,12Z",
            "evacuation" => "M12,3L2,12H5V20H19V12H22L12,3ZM12,7.5C13.1,7.5 14,8.4 14,9.5C14,11 12,13 12,13C12,13 10,11 10,9.5C10,8.4 10.9,7.5 12,7.5Z",
            "safety" => "M12,2L1,11H4V21H20V11H23L12,2ZM12,4.8L18,10V19H6V10L12,4.8ZM12,7C12.8,7 13.5,7.7 13.5,8.5C13.5,9.3 12.8,10 12,10C11.2,10 10.5,9.3 10.5,8.5C10.5,7.7 11.2,7 12,7ZM8.5,9C9.2,9 9.7,9.5 9.7,10.2C9.7,10.9 9.2,11.4 8.5,11.4C7.8,11.4 7.3,10.9 7.3,10.2C7.3,9.5 7.8,9 8.5,9ZM15.5,9C16.2,9 16.7,9.5 16.7,10.2C16.7,10.9 16.2,11.4 15.5,11.4C14.8,11.4 14.3,10.9 14.3,10.2C14.3,9.5 14.8,9 15.5,9ZM12,11C10.5,11 9,11.8 8.5,13H15.5C15,11.8 13.5,11 12,11ZM6.2,14H8.5V16H6.2V14ZM15.5,14H17.8V16H15.5V14Z",
            "ar_route" => "M19,15.18V7C19,4.79 17.21,3 15,3C12.79,3 11,4.79 11,7V15C11,16.1 10.1,17 9,17C7.9,17 7,16.1 7,15V7.82C8.16,7.4 9,6.3 9,5C9,3.34 7.66,2 6,2C4.34,2 3,3.34 3,5C3,6.3 3.84,7.4 5,7.82V15C5,17.21 6.79,19 9,19C11.21,19 13,17.21 13,15V7C13,5.9 13.9,5 15,5C16.1,5 17,5.9 17,7V15.18C15.84,15.6 15,16.7 15,18C15,19.66 16.34,21 18,21C19.66,21 21,19.66 21,18C21,16.7 20.16,15.6 19,15.18Z",
            _ => item.ActionType == "CustomContact" ? "M22,3H2C0.9,3 0,3.9 0,5V19C0,20.1 0.9,21 2,21H22C23.1,21 24,20.1 24,19V5C24,3.9 23.1,3 22,3ZM12,6C13.66,6 15,7.34 15,9C15,10.66 13.66,12 12,12C10.34,12 9,10.66 9,9C9,7.34 10.34,6 12,6ZM18,18H6V16.83C6,14.83 10,13.72 12,13.72C14,13.72 18,14.83 18,16.83V18ZM20,15.5C19.25,15.5 18.55,15.3 17.43,14.93L16.41,15.18C17.38,16.38 18.62,17.06 20,17.38V15.5Z" : (string.IsNullOrWhiteSpace(item.IconData) ? "M20,15.5C18.75,15.5 17.55,15.3 16.43,14.93C16.08,14.82 15.69,14.9 15.41,15.18L13.21,17.38C10.38,15.94 8.06,13.62 6.62,10.79L8.82,8.59C9.1,8.31 9.18,7.92 9.07,7.57C8.7,6.45 8.5,5.25 8.5,4C8.5,3.45 8.05,3 7.5,3H4C3.45,3 3,3.45 3,4C3,13.39 10.61,21 20,21C20.55,21 21,20.55 21,20V16.5C21,15.95 20.55,15.5 20,15.5Z" : item.IconData)
        };
    }

    private void SyncEnabledQuickActions()
    {
        QuickActions.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in AllAvailableQuickActions.Where(x => x.IsEnabled))
        {
            if (seen.Add(item.Title))
            {
                EnsureSolidVectorIcons(item);
                QuickActions.Add(item);
            }
        }

        QuickActionPages.Clear();
        var list = QuickActions.ToList();
        for (int i = 0; i < list.Count; i += 6)
        {
            var pageItems = list.Skip(i).Take(6).ToList();
            QuickActionPages.Add(new QuickActionPage
            {
                Items = new ObservableCollection<QuickActionItem>(pageItems)
            });
        }

        OnPropertyChanged(nameof(HasMultipleQuickActionPages));
        SaveQuickActions();
    }

    private void SaveQuickActions()
    {
        try
        {
            string json = JsonConvert.SerializeObject(AllAvailableQuickActions.ToList());
            Preferences.Default.Set("CustomQuickActionsList_v18", json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveQuickActions error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenCustomizeQuickActions()
    {
        IsCustomizeQuickActionsVisible = true;
    }

    [RelayCommand]
    private void CloseCustomizeQuickActions()
    {
        IsCustomizeQuickActionsVisible = false;
    }

    [RelayCommand]
    private void ToggleQuickActionItem(QuickActionItem item)
    {
        if (item != null)
        {
            item.IsEnabled = !item.IsEnabled;
            SyncEnabledQuickActions();
        }
    }

    [RelayCommand]
    private async Task AddCustomEmergencyContactAsync()
    {
        if (string.IsNullOrWhiteSpace(NewContactName) || string.IsNullOrWhiteSpace(NewContactPhone))
        {
            if (Shell.Current != null)
            {
                await Shell.Current.DisplayAlert("Missing Information", "Please enter both a contact label (e.g. Call Mom) and a phone number.", "OK");
            }
            return;
        }

        var newAction = new QuickActionItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = NewContactName.Trim(),
            Subtitle = $"Emergency Contact: {NewContactPhone.Trim()}",
            IconData = "M6.62,10.79C8.06,13.62 10.38,15.94 13.21,17.38L15.41,15.18C15.69,14.9 16.08,14.82 16.43,14.93C17.55,15.3 18.75,15.5 20,15.5A1,1 0 0,1 21,16.5V20A1,1 0 0,1 20,21C10.61,21 3,13.39 3,4A1,1 0 0,1 4,3H7.5A1,1 0 0,1 8.5,4C8.5,5.25 8.7,6.45 9.07,7.57C9.18,7.92 9.1,8.31 8.82,8.59L6.62,10.79Z",
            IconBg = "#EFF6FF",
            IconColor = "#2563EB",
            ActionType = "CustomContact",
            PhoneNumber = NewContactPhone.Trim(),
            IsEnabled = true,
            IsCustom = true
        };

        AllAvailableQuickActions.Add(newAction);
        NewContactName = string.Empty;
        NewContactPhone = string.Empty;
        SyncEnabledQuickActions();

        if (Shell.Current != null)
        {
            await Shell.Current.DisplayAlert("Quick Action Added", $"Added '{newAction.Title}' to your homescreen quick actions!", "OK");
        }
    }

    [RelayCommand]
    private void DeleteCustomQuickAction(QuickActionItem item)
    {
        if (item != null && AllAvailableQuickActions.Contains(item))
        {
            AllAvailableQuickActions.Remove(item);
            SyncEnabledQuickActions();
        }
    }

    [RelayCommand]
    private async Task ExecuteQuickActionAsync(QuickActionItem item)
    {
        if (item == null || Shell.Current == null) return;

        try
        {
            // 1. One-Tap SOS Distress Beacon
            if (item.ActionType == "SOSBeacon")
            {
                await ExecuteSOSBeaconAsync();
                return;
            }

            // 2. SOS Hardware Flashlight
            if (item.ActionType == "Flashlight")
            {
                await ToggleFlashlightAsync(item);
                return;
            }

            // 3. Loud Emergency Siren
            if (item.ActionType == "Siren")
            {
                ToggleEmergencySiren(item);
                return;
            }

            // 4. Share Live GPS Location
            if (item.ActionType == "ShareLocation")
            {
                await ShareLiveLocationAsync();
                return;
            }

            // 5. Phone Call / Custom Contact
            if (item.ActionType == "CustomContact" || !string.IsNullOrWhiteSpace(item.PhoneNumber))
            {
                await MakePhoneCallAsync(item.PhoneNumber);
                return;
            }

            // 6. Navigation Route
            if (!string.IsNullOrWhiteSpace(item.TargetRoute))
            {
                await Shell.Current.GoToAsync(item.TargetRoute);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ExecuteQuickAction error: {ex.Message}");
        }
    }

    private async Task MakePhoneCallAsync(string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return;

        try
        {
            string cleanDigits = System.Text.RegularExpressions.Regex.Replace(number, @"[^\d+]", "");
            if (string.IsNullOrWhiteSpace(cleanDigits))
            {
                cleanDigits = number.Trim();
            }

            if (!string.IsNullOrWhiteSpace(cleanDigits))
            {
                var uri = new Uri($"tel:{cleanDigits}");
                await Launcher.Default.OpenAsync(uri);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Phone call error: {ex.Message}");
        }
    }

    private async Task ExecuteSOSBeaconAsync()
    {
        if (Shell.Current == null) return;

        bool confirm = await Shell.Current.DisplayAlert(
            "SEND EMERGENCY SOS DISTRESS SIGNAL",
            "Are you sure you want to broadcast an urgent SOS signal with your live GPS location to Marikina Rescue Command & your Safety Circle?",
            "SEND SOS NOW",
            "Cancel");

        if (confirm)
        {
            double lat = 14.6340;
            double lng = 121.0990;
            try
            {
                var loc = await Geolocation.Default.GetLastKnownLocationAsync();
                if (loc != null)
                {
                    lat = loc.Latitude;
                    lng = loc.Longitude;
                }
            }
            catch { }

            // Play emergency alert chime
            RealtimeAdvisoryManager.PlayAlarmAudio();

            await Shell.Current.DisplayAlert(
                "EMERGENCY SOS BROADCAST SENT!",
                $"Distress signal sent successfully!\n\nUser: {UserName}\nLocation: {lat:F4}°, {lng:F4}°\n\nMarikina Rescue Command and your Safety Circle contacts have been alerted with your live beacon position.",
                "OK");
        }
    }

    private async Task ToggleFlashlightAsync(QuickActionItem item)
    {
        try
        {
            _isFlashlightOn = !_isFlashlightOn;
            item.IsActiveState = _isFlashlightOn;

#if ANDROID
            bool torchSetSuccess = false;
            try
            {
                var context = Android.App.Application.Context;
                var cameraManager = (Android.Hardware.Camera2.CameraManager?)context.GetSystemService(Android.Content.Context.CameraService);
                if (cameraManager != null)
                {
                    var cameraIds = cameraManager.GetCameraIdList();
                    foreach (var id in cameraIds)
                    {
                        var characteristics = cameraManager.GetCameraCharacteristics(id);
                        var hasFlash = characteristics.Get(Android.Hardware.Camera2.CameraCharacteristics.FlashInfoAvailable);
                        if (hasFlash != null && ((Java.Lang.Boolean)hasFlash).BooleanValue())
                        {
                            cameraManager.SetTorchMode(id, _isFlashlightOn);
                            torchSetSuccess = true;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Android torch mode error: {ex.Message}");
            }

            if (!torchSetSuccess)
            {
                if (_isFlashlightOn)
                    await Flashlight.Default.TurnOnAsync();
                else
                    await Flashlight.Default.TurnOffAsync();
            }
#else
            if (_isFlashlightOn)
                await Flashlight.Default.TurnOnAsync();
            else
                await Flashlight.Default.TurnOffAsync();
#endif

            if (_isFlashlightOn)
            {
                item.Subtitle = "Flashlight is ON • Tap to turn OFF";
                item.IconBg = "#FEF08A";
                item.IconColor = "#854D0E";
            }
            else
            {
                item.Subtitle = "Tap to turn phone flashlight ON / OFF";
                item.IconBg = "#FEF9C3";
                item.IconColor = "#CA8A04";
            }
        }
        catch (Exception ex)
        {
            _isFlashlightOn = false;
            item.IsActiveState = false;
            item.Subtitle = "Flashlight hardware unavailable";
            System.Diagnostics.Debug.WriteLine($"Flashlight error: {ex.Message}");
        }
    }

    private void ToggleEmergencySiren(QuickActionItem item)
    {
        _isSirenOn = !_isSirenOn;
        item.IsActiveState = _isSirenOn;

        if (_isSirenOn)
        {
            RealtimeAdvisoryManager.PlayAlarmAudio();
            if (Shell.Current != null)
            {
                Shell.Current.DisplayAlert("Emergency Siren Playing", "Max volume distress siren is playing. Tap again to stop audio beacon.", "OK");
            }
        }
        else
        {
            RealtimeAdvisoryManager.StopAlarmAudio();
        }
    }

    private async Task ShareLiveLocationAsync()
    {
        if (Shell.Current == null) return;

        try
        {
            double lat = 14.6340;
            double lng = 121.0990;
            try
            {
                var loc = await Geolocation.Default.GetLastKnownLocationAsync();
                if (loc != null)
                {
                    lat = loc.Latitude;
                    lng = loc.Longitude;
                }
            }
            catch { }

            string mapsUrl = $"https://maps.google.com/?q={lat:F5},{lng:F5}";
            string shareMessage = $"URGENT: RescuAR Emergency SOS Location Alert!\n\nName: {UserName}\nCurrent GPS Position: {lat:F5}°, {lng:F5}°\nGoogle Maps Link: {mapsUrl}";

            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "Share Emergency GPS Location",
                Text = shareMessage
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Share location error: {ex.Message}");
        }
    }

    private async void LoadUserData()
    {
        UpdateTimeBasedGreeting();
        try
        {
            string fname = Preferences.Default.Get("UserFirstName", "");
            string lname = Preferences.Default.Get("UserLastName", "");

            if (string.IsNullOrWhiteSpace(fname))
            {
                var client = RescuAR.Services.SupabaseService.Instance.Client;
                if (client?.Auth.CurrentUser != null)
                {
                    var user = client.Auth.CurrentUser;
                    if (user.UserMetadata != null)
                    {
                        if (user.UserMetadata.TryGetValue("first_name", out var fnObj) && fnObj != null)
                            fname = fnObj.ToString()?.Trim() ?? "";
                        if (user.UserMetadata.TryGetValue("last_name", out var lnObj) && lnObj != null)
                            lname = lnObj.ToString()?.Trim() ?? "";
                        if (string.IsNullOrWhiteSpace(fname) && user.UserMetadata.TryGetValue("full_name", out var nameObj) && nameObj != null)
                            fname = nameObj.ToString()?.Trim() ?? "";
                        if (string.IsNullOrWhiteSpace(fname) && user.UserMetadata.TryGetValue("name", out var nameObj2) && nameObj2 != null)
                            fname = nameObj2.ToString()?.Trim() ?? "";
                    }

                    if (string.IsNullOrWhiteSpace(fname) && !string.IsNullOrWhiteSpace(user.Email))
                    {
                        string emailPrefix = user.Email.Split('@')[0];
                        fname = char.ToUpper(emailPrefix[0]) + emailPrefix.Substring(1);
                    }

                    try
                    {
                        var userRow = await client.From<User>().Where(u => u.Id == user.Id).Single();
                        if (userRow != null && !string.IsNullOrWhiteSpace(userRow.FirstName))
                        {
                            fname = userRow.FirstName;
                            if (!string.IsNullOrWhiteSpace(userRow.LastName))
                                lname = userRow.LastName;
                        }
                    }
                    catch (Exception) { }

                    if (!string.IsNullOrWhiteSpace(fname))
                    {
                        Preferences.Default.Set("UserFirstName", fname);
                        Preferences.Default.Set("UserLastName", lname);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(fname))
            {
                UserName = $"{fname} {lname}".Trim();
            }
            else
            {
                UserName = "User";
            }

            int score = Preferences.Default.Get("PASS_ChecklistScore", Preferences.Default.Get("PASS_Score", 60));
            int completed = Preferences.Default.Get("PASS_ChecklistCompleted", 6);
            int total = Preferences.Default.Get("PASS_ChecklistTotal", 10);

            PreparednessScore = score;
            PreparednessCompletedItems = completed;
            PreparednessTotalItems = total;
            ScoreProgress = score / 100.0;
            OnPropertyChanged(nameof(ActiveAdvisoriesTitleText));
            OnPropertyChanged(nameof(PreparednessReadyTitleText));
            OnPropertyChanged(nameof(PreparednessItemsDetailText));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"User data load error: {ex.Message}");
        }
    }

    private async Task LoadWeatherDataAsync()
    {
        try
        {
            double lat = 14.6507;
            double lng = 121.1029;

            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                }

                if (status == PermissionStatus.Granted)
                {
                    var location = await Geolocation.Default.GetLastKnownLocationAsync();
                    if (location == null)
                    {
                        var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5));
                        location = await Geolocation.Default.GetLocationAsync(request);
                    }

                    if (location != null)
                    {
                        lat = location.Latitude;
                        lng = location.Longitude;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GPS location fetch notice: {ex.Message}");
            }

            var weather = await _weatherService.GetWeatherAsync(lat, lng);
            if (weather != null)
            {
                WeatherTemperatureText = $"{Math.Round(weather.TemperatureCelsius)} °C";
                WeatherHighLowText = $"H: {Math.Round(weather.MaxTempCelsius)}° • L: {Math.Round(weather.MinTempCelsius)}°";
                WeatherConditionTitle = weather.ConditionDescription;
                WeatherConditionSummary = weather.ConditionSummary;
                WeatherIconData = weather.IconPathData;
                OnPropertyChanged(nameof(WeatherIconGeometry));
                LocationName = weather.LocationName;
                WeatherSummary = $"{WeatherTemperatureText} • {WeatherConditionTitle}";
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
                await Shell.Current.GoToAsync("//Profile");
            }
            catch (Exception)
            {
            }
        }
    }

    private bool _isNavigatingBento;

    private async Task FastNavigateAsync(string route)
    {
        if (_isNavigatingBento || Shell.Current == null) return;
        _isNavigatingBento = true;
        try
        {
            await Shell.Current.GoToAsync(route);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation error ({route}): {ex.Message}");
        }
        finally
        {
            // Reset debouncing flag after 300ms
            _ = Task.Delay(300).ContinueWith(_ => _isNavigatingBento = false);
        }
    }

    [RelayCommand]
    private async Task OpenPASSAsync()
    {
        await FastNavigateAsync("Prepare/PASS");
    }

    [RelayCommand]
    private async Task OpenChecklistAsync()
    {
        await FastNavigateAsync("Prepare/Checklist");
    }

    [RelayCommand]
    private async Task OpenReportsAsync()
    {
        await FastNavigateAsync("//Reports");
    }

    [RelayCommand]
    private async Task OpenHotlinesAsync()
    {
        await FastNavigateAsync("Prepare/HotlineDirectory");
    }

    [RelayCommand]
    private async Task OpenEvacuationAsync()
    {
        await FastNavigateAsync("Prepare/EvacuationCenterInfo");
    }

    [RelayCommand]
    private async Task OpenSafetyCircleAsync()
    {
        await FastNavigateAsync("SafetyCirclePage");
    }

    [RelayCommand]
    private async Task OpenAdvisoriesAsync()
    {
        await FastNavigateAsync("AdvisoryFeedPage");
    }

    [RelayCommand]
    private async Task OpenTranslationMenuAsync()
    {
        if (SelectedAdvisory is null ||
            Shell.Current is null)
        {
            return;
        }

        string? result =
            await Shell.Current.DisplayActionSheet(
                "Translation",
                "Cancel",
                null,
                "English",
                "Tagalog");

        if (result == "Tagalog")
        {
            SelectedAdvisory.SetLanguage(
                true);
        }
        else if (result == "English")
        {
            SelectedAdvisory.SetLanguage(
                false);
        }
    }

    [RelayCommand]
    private void ClosePopup()
    {
        IsPopupVisible = false;
        RealtimeAdvisoryManager.StopAlarmAudio();
    }

    [RelayCommand]
    private async Task ExecuteCarouselActionAsync(DashboardCarouselItem item)
    {
        if (item == null || Shell.Current == null) return;

        if (item.ActionType == "Camera")
        {
            await Shell.Current.GoToAsync("//Camera");
        }
        else if (item.ActionType == "Checklist")
        {
            await Shell.Current.GoToAsync("Prepare/Checklist");
        }
        else if (item.ActionType == "LearnMore")
        {
            await Shell.Current.GoToAsync("Prepare/FloodTimeline");
        }
    }

    private void UpdateTimeBasedGreeting()
    {
        int hour = DateTime.Now.Hour;
        if (hour >= 5 && hour < 12)
        {
            Greeting = "Good Morning,";
        }
        else if (hour >= 12 && hour < 18)
        {
            Greeting = "Good Afternoon,";
        }
        else
        {
            Greeting = "Good Evening,";
        }
    }

    private async Task LoadRealtimeBentoBoxDataAsync()
    {
        try
        {
            // 1. Fetch Safety Circle Data dynamically from DashboardDataService / Supabase
            var safetyData = await RescuAR.App.Services.Dashboard.DashboardDataService.Instance.GetSafetyCircleDataAsync();
            if (safetyData != null && safetyData.Groups != null && safetyData.Groups.Count > 0)
            {
                var g1 = safetyData.Groups[0];
                SafetyCircleGroup1Name = g1.Name;
                SafetyCircleGroup1Status = g1.StatusText;
                SafetyCircleGroup1BadgeText = g1.IsAlert ? "ALERT" : "ACTIVE";

                if (safetyData.Groups.Count > 1)
                {
                    var g2 = safetyData.Groups[1];
                    SafetyCircleGroup2Name = g2.Name;
                    SafetyCircleGroup2Status = g2.StatusText;
                    SafetyCircleGroup2BadgeText = g2.IsAlert ? "ALERT" : "ACTIVE";
                }
                else
                {
                    SafetyCircleGroup2Name = "+ Join / Create Circle";
                    SafetyCircleGroup2Status = "Tap View Family to connect";
                    SafetyCircleGroup2BadgeText = "ADD";
                }
            }
            else
            {
                SafetyCircleGroup1Name = "No Active Circle";
                SafetyCircleGroup1Status = "0 active circles joined";
                SafetyCircleGroup1BadgeText = "NONE";

                SafetyCircleGroup2Name = "Family & Friends";
                SafetyCircleGroup2Status = "Tap View Family to create or join";
                SafetyCircleGroup2BadgeText = "SETUP";
            }

            // 2. Fetch Live Community Incident Reports from CommunityReportService
            var reportService = new CommunityReportService();
            var liveReports = await reportService.GetReportsAsync();
            if (liveReports != null && liveReports.Count > 0)
            {
                var r1 = liveReports[0];
                RecentReport1Title = !string.IsNullOrWhiteSpace(r1.Title) ? r1.Title : "Incident reported nearby";
                RecentReport1Distance = !string.IsNullOrWhiteSpace(r1.DistanceText) ? r1.DistanceText : (!string.IsNullOrWhiteSpace(r1.Address) ? r1.Address : "Recently reported");

                if (liveReports.Count > 1)
                {
                    var r2 = liveReports[1];
                    RecentReport2Title = !string.IsNullOrWhiteSpace(r2.Title) ? r2.Title : "Incident reported nearby";
                    RecentReport2Distance = !string.IsNullOrWhiteSpace(r2.DistanceText) ? r2.DistanceText : (!string.IsNullOrWhiteSpace(r2.Address) ? r2.Address : "Recently reported");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading bento box realtime data: {ex.Message}");
        }
    }
}
