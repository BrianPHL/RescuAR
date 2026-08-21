using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using RescuAR.App.Models;
using RescuAR.App.Services.Reports;

namespace RescuAR.App.ViewModels.Reports;

public partial class AdvisoryFeedViewModel : ObservableObject
{
    private readonly AdvisoryService _advisoryService;
    private List<DisasterAdvisory> _allAdvisories = new();

    public ObservableCollection<DisasterAdvisory> Advisories { get; } = new();

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    public partial string SelectedFilter { get; set; } = "All";

    [ObservableProperty]
    public partial string LatestWaterLevelText { get; set; } = "16.5 m";

    [ObservableProperty]
    public partial string CurrentAlertStatus { get; set; } = "Level 2 — Warning";

    [ObservableProperty]
    public partial string CurrentDateTimeText { get; set; } = DateTime.Now.ToString("dddd, MMMM d, yyyy • h:mm:ss tt");

    [ObservableProperty]
    public partial DisasterAdvisory? SelectedAdvisory { get; set; }

    [ObservableProperty]
    public partial bool IsPopupVisible { get; set; }

    public AdvisoryFeedViewModel()
    {
        _advisoryService = new AdvisoryService();
        _ = LoadAdvisoriesAsync();
        StartClockTicker();

        // Real-time listener for new admin advisories
        RealtimeAdvisoryManager.OnNewAdvisoryPushed += (newAdvisory) =>
        {
            SelectedAdvisory = newAdvisory;
            IsPopupVisible = true;
            _ = LoadAdvisoriesAsync();
        };

        RealtimeAdvisoryManager.StartRealtimeListener();
    }

    private void StartClockTicker()
    {
        var timer = Application.Current?.Dispatcher.CreateTimer();
        if (timer != null)
        {
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += (s, e) =>
            {
                CurrentDateTimeText = DateTime.Now.ToString("dddd, MMMM d, yyyy • h:mm:ss tt");
            };
            timer.Start();
        }
    }

    [RelayCommand]
    public async Task RefreshAdvisoriesAsync()
    {
        IsRefreshing = true;
        await LoadAdvisoriesAsync();
        IsRefreshing = false;
    }

    private async Task LoadAdvisoriesAsync()
    {
        _allAdvisories = await _advisoryService.GetAdvisoriesAsync();

        if (_allAdvisories.Count > 0)
        {
            var top = _allAdvisories.FirstOrDefault();
            if (top != null)
            {
                LatestWaterLevelText = $"{top.WaterLevel:F1} m";
                CurrentAlertStatus = $"{top.DisplayAlertLevel} Alert";
            }
        }

        ApplyFilter();
    }

    [RelayCommand]
    private void SelectFilter(string filter)
    {
        SelectedFilter = filter;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Advisories.Clear();
        var filtered = SelectedFilter switch
        {
            "Critical" => _allAdvisories.Where(x => x.DisplayAlertLevel.Equals("Critical", StringComparison.OrdinalIgnoreCase) || x.DisplayAlertLevel.Equals("Warning", StringComparison.OrdinalIgnoreCase) || x.DisplayAlertLevel.Equals("High", StringComparison.OrdinalIgnoreCase)),
            "Standby" => _allAdvisories.Where(x => x.DisplayAlertLevel.Equals("Standby", StringComparison.OrdinalIgnoreCase) || x.DisplayAlertLevel.Equals("Low", StringComparison.OrdinalIgnoreCase)),
            _ => _allAdvisories
        };

        foreach (var item in filtered)
        {
            Advisories.Add(item);
        }
    }

    [RelayCommand]
    private void ShowAdvisoryDetail(DisasterAdvisory advisory)
    {
        if (advisory != null)
        {
            SelectedAdvisory = advisory;
            IsPopupVisible = true;
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

    [RelayCommand]
    private async Task NavigateToEvacuationCentersAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("Prepare/EvacuationCenterInfo");
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
}
