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
    private bool isRefreshing;
    [ObservableProperty]
    private string selectedFilter = "All";
    [ObservableProperty]
    private string latestWaterLevelText = "16.5 m";
    [ObservableProperty]
    private string currentAlertStatus = "Level 2 — Warning";
    [ObservableProperty]
    private string currentDateTimeText = DateTime.Now.ToString("dddd, MMMM d, yyyy • h:mm:ss tt");
    [ObservableProperty]
    private DisasterAdvisory? selectedAdvisory;
    [ObservableProperty]
    private bool isPopupVisible;
    public AdvisoryFeedViewModel()
    {
        _advisoryService = new AdvisoryService();
        _ = LoadAdvisoriesAsync();
        _ = LoadInitialWaterLevelAsync();
        StartClockTicker();

        // Real-time listener for new admin advisories
        RealtimeAdvisoryManager.OnNewAdvisoryPushed += (newAdvisory) =>
        {
            SelectedAdvisory = newAdvisory;
            IsPopupVisible = true;
            _ = LoadAdvisoriesAsync();
        };
        RealtimeAdvisoryManager.StartRealtimeListener();

        // Real-time listener for Sto. Nino Water Level
        RealtimeWaterLevelManager.OnWaterLevelUpdated += UpdateWaterLevelUI;
        RealtimeWaterLevelManager.StartRealtimeListener();
    }

    private async Task LoadInitialWaterLevelAsync()
    {
        var latestLog = await RealtimeWaterLevelManager.GetLatestWaterLevelAsync();
        if (latestLog != null)
        {
            UpdateWaterLevelUI(latestLog);
        }
    }

    private void UpdateWaterLevelUI(MonitoringStation station)
    {
        LatestWaterLevelText = $"{station.Level:F1} m";
        
        if (station.Level >= 18.0)
            CurrentAlertStatus = "Level 3 — Critical";
        else if (station.Level >= 16.0)
            CurrentAlertStatus = "Level 2 — Warning";
        else if (station.Level >= 15.0)
            CurrentAlertStatus = "Level 1 — Standby";
        else
            CurrentAlertStatus = "Low Alert";
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
        RealtimeAdvisoryManager.StopAlarmAudio();
    }

    [RelayCommand]
    private async Task OpenTranslationMenuAsync()
    {
        if (SelectedAdvisory == null || Shell.Current == null) return;

        string result = await Shell.Current.DisplayActionSheet("Translation", "Cancel", null, "English", "Tagalog");
        if (result == "Tagalog")
        {
            SelectedAdvisory.SetLanguage(true);
        }
        else if (result == "English")
        {
            SelectedAdvisory.SetLanguage(false);
        }
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
    private async Task NavigateToEvacuationCentersAsync(string? emergencyType = "flood")
    {
        if (Shell.Current != null)
        {
            var param = string.IsNullOrWhiteSpace(emergencyType) ? "flood" : emergencyType;
            await Shell.Current.GoToAsync($"Prepare/EvacuationCenterInfo?type={Uri.EscapeDataString(param)}");
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
