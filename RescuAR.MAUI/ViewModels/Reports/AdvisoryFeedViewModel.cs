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
    private DisasterAdvisory? selectedAdvisory = new DisasterAdvisory();
    [ObservableProperty]
    private bool isPopupVisible;

    public AdvisoryFeedViewModel()
    {
        _advisoryService = new AdvisoryService();
        _ = LoadAdvisoriesAsync();
        StartClockTicker();

        // Real-time listener for new admin advisories only
        try
        {
            RealtimeAdvisoryManager.OnNewAdvisoryPushed -= HandleNewAdvisoryPushed;
            RealtimeAdvisoryManager.OnNewAdvisoryPushed += HandleNewAdvisoryPushed;
            RealtimeAdvisoryManager.StartRealtimeListener();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AdvisoryFeedViewModel listener init warning: {ex.Message}");
        }
    }

    private void HandleNewAdvisoryPushed(DisasterAdvisory newAdvisory)
    {
        if (newAdvisory == null) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                SelectedAdvisory = newAdvisory;
                IsPopupVisible = true;
                _ = LoadAdvisoriesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HandleNewAdvisoryPushed error: {ex.Message}");
            }
        });
    }

    private IDispatcherTimer? _clockTimer;

    private void StartClockTicker()
    {
        try
        {
            if (_clockTimer != null) return;
            _clockTimer = Application.Current?.Dispatcher?.CreateTimer();
            if (_clockTimer != null)
            {
                _clockTimer.Interval = TimeSpan.FromSeconds(1);
                _clockTimer.Tick += (s, e) =>
                {
                    try
                    {
                        CurrentDateTimeText = DateTime.Now.ToString("dddd, MMMM d, yyyy • h:mm:ss tt");
                    }
                    catch
                    {
                    }
                };
                _clockTimer.Start();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StartClockTicker error: {ex.Message}");
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
        try
        {
            _allAdvisories = await _advisoryService.GetAdvisoriesAsync() ?? new List<DisasterAdvisory>();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadAdvisoriesAsync error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SelectFilter(string filter)
    {
        SelectedFilter = filter;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (_allAdvisories == null)
                    {
                        Advisories.Clear();
                        return;
                    }

                    var filtered = SelectedFilter switch
                    {
                        "Critical" => _allAdvisories.Where(x => x != null && (
                            x.DisplayAlertLevel.Equals("Critical", StringComparison.OrdinalIgnoreCase) ||
                            x.DisplayAlertLevel.Equals("Warning", StringComparison.OrdinalIgnoreCase) ||
                            x.DisplayAlertLevel.Equals("High", StringComparison.OrdinalIgnoreCase)
                        )).ToList(),
                        "Standby" => _allAdvisories.Where(x => x != null && (
                            x.DisplayAlertLevel.Equals("Standby", StringComparison.OrdinalIgnoreCase) ||
                            x.DisplayAlertLevel.Equals("Low", StringComparison.OrdinalIgnoreCase)
                        )).ToList(),
                        _ => _allAdvisories.Where(x => x != null).ToList()
                    };

                    if (Advisories.Count == filtered.Count && Advisories.SequenceEqual(filtered))
                    {
                        return;
                    }

                    Advisories.Clear();
                    foreach (var item in filtered)
                    {
                        Advisories.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ApplyFilter inner error: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ApplyFilter dispatch error: {ex.Message}");
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
        try
        {
            RealtimeAdvisoryManager.StopAlarmAudio();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StopAlarmAudio error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OpenTranslationMenuAsync()
    {
        try
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OpenTranslationMenu error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task GoToAdvisoriesFeedAsync()
    {
        IsPopupVisible = false;
        try
        {
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("AdvisoryFeedPage");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GoToAdvisoriesFeed error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task NavigateToEvacuationCentersAsync(string? emergencyType = "flood")
    {
        try
        {
            if (Shell.Current != null)
            {
                var param = string.IsNullOrWhiteSpace(emergencyType) ? "flood" : emergencyType;
                await Shell.Current.GoToAsync($"Prepare/EvacuationCenterInfo?type={Uri.EscapeDataString(param)}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NavigateToEvacuationCenters error: {ex.Message}");
        }
    }

    [ObservableProperty]
    private int unreadNotificationsCount = 2;

    [RelayCommand]
    private async Task OpenNotificationsAsync()
    {
        try
        {
            UnreadNotificationsCount = 0;
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("NotificationsPage");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OpenNotifications error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        try
        {
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("..");
            }
        }
        catch
        {
            try
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.GoToAsync("//Home");
                }
            }
            catch
            {
            }
        }
    }

    [RelayCommand]
    private async Task GoBackAsync() => await BackAsync();
}
