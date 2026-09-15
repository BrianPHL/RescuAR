using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using RescuAR.App.Services.Dashboard;

namespace RescuAR.App.ViewModels.Prepare;

public partial class PrepareViewModel : ObservableObject
{
    private readonly IDashboardDataService _dataService;

    [ObservableProperty]
    private string _title = "Prepare Page";

    [ObservableProperty]
    private string _subtitle = "Essential tools, emergency checklists, safety guides, and shelter information.";

    [ObservableProperty]
    private int _percentReady = 60;

    [ObservableProperty]
    private int _preparedItems = 6;

    [ObservableProperty]
    private int _totalItems = 10;

    [ObservableProperty]
    private double _progressFraction = 0.6;

    [ObservableProperty]
    private string _preparednessStatus = "60% Ready • 6 of 10 Items Prepared";

    public PrepareViewModel() : this(DashboardDataService.Instance)
    {
    }

    public PrepareViewModel(IDashboardDataService dataService)
    {
        _dataService = dataService;
        _ = LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        try
        {
            var data = await _dataService.GetPreparednessDataAsync();
            PercentReady = data.PercentReady;
            PreparedItems = data.PreparedItems;
            TotalItems = data.TotalItems;
            ProgressFraction = TotalItems > 0 ? (double)PreparedItems / TotalItems : 0.0;
            PreparednessStatus = $"{PercentReady}% Ready • {PreparedItems} of {TotalItems} Items Prepared";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading prepare data: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task NavigateToRouteAsync(string? route)
    {
        if (string.IsNullOrWhiteSpace(route) || Shell.Current == null)
            return;

        try
        {
            await Shell.Current.GoToAsync(route);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation error to {route}: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OpenChecklistAsync()
    {
        await NavigateToRouteAsync("Prepare/Checklist");
    }

    [RelayCommand]
    private async Task OpenAssessmentAsync()
    {
        // Household Hazard Assessment linked to PASSViewModel (Prepare/PASS)
        await NavigateToRouteAsync("Prepare/PASS");
    }

    [RelayCommand]
    private async Task OpenEvacuationCenterAsync()
    {
        await NavigateToRouteAsync("Prepare/EvacuationCenterInfo");
    }

    [RelayCommand]
    private async Task OpenHotlineDirectoryAsync()
    {
        await NavigateToRouteAsync("Prepare/HotlineDirectory");
    }

    [RelayCommand]
    private async Task OpenPreparednessGuideAsync()
    {
        await NavigateToRouteAsync("Prepare/PreparednessGuide");
    }

    [RelayCommand]
    private async Task OpenPracticeARAsync()
    {
        // AR Evacuation Practice routed directly to Camera
        await NavigateToRouteAsync("//Camera");
    }
}
