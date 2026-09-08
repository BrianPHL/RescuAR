using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using RescuAR.App.Services.Dashboard;

namespace RescuAR.App.ViewModels.Dashboard;

public partial class SafetyCircleOverviewViewModel : ObservableObject
{
    private readonly IDashboardDataService _dataService;

    public ObservableCollection<SafetyCircleGroupItem> Groups { get; } = new();

    public string Circle1Name =>
        Groups.Count > 0
            ? Groups[0].Name
            : "No circle available";

    public string Circle1Status =>
        Groups.Count > 0
            ? Groups[0].StatusText
            : "No member status";

    public string Circle2Name =>
        Groups.Count > 1
            ? Groups[1].Name
            : "No second circle";

    public string Circle2Status =>
        Groups.Count > 1
            ? Groups[1].StatusText
            : "No member status";

    [ObservableProperty]
    private string _actionText = string.Empty;

    [ObservableProperty]
    private string _moduleRoute = "SafetyCirclePage";

    [ObservableProperty]
    private string _moduleName = string.Empty;

    public SafetyCircleOverviewViewModel() : this(DashboardDataService.Instance)
    {
    }

    public SafetyCircleOverviewViewModel(IDashboardDataService dataService)
    {
        _dataService = dataService;
        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        var data = await _dataService.GetSafetyCircleDataAsync();
        Groups.Clear();
        foreach (var group in data.Groups)
        {
            Groups.Add(group);
        }
        ActionText = data.ActionText;
        ModuleRoute = "SafetyCirclePage";
        ModuleName = data.ModuleName;

        OnPropertyChanged(
            nameof(Circle1Name));
        OnPropertyChanged(
            nameof(Circle1Status));
        OnPropertyChanged(
            nameof(Circle2Name));
        OnPropertyChanged(
            nameof(Circle2Status));
    }

    [RelayCommand]
    private async Task NavigateToSafetyCircleAsync()
    {
        if (Shell.Current != null)
        {
            try
            {
                await Shell.Current.GoToAsync("SafetyCirclePage");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
            }
        }
    }
}
