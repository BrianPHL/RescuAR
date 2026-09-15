using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using RescuAR.App.Services.Dashboard;

namespace RescuAR.App.ViewModels.Dashboard;

public partial class PreparednessOverviewViewModel : ObservableObject
{
    private readonly IDashboardDataService _dataService;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _actionText = string.Empty;

    [ObservableProperty]
    private string _moduleRoute = "PreparePage";

    [ObservableProperty]
    private string _moduleName = string.Empty;

    public PreparednessOverviewViewModel() : this(DashboardDataService.Instance)
    {
    }

    public PreparednessOverviewViewModel(IDashboardDataService dataService)
    {
        _dataService = dataService;
        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        var data = await _dataService.GetPreparednessDataAsync();
        Title = $"{data.PercentReady}% Ready";
        Subtitle = $"{data.PreparedItems} of {data.TotalItems} items prepared";
        ActionText = data.ActionText;
        ModuleRoute = "PreparePage";
        ModuleName = data.ModuleName;
    }

    [RelayCommand]
    private async Task NavigateToPreparationProgressAsync()
    {
        if (Shell.Current != null)
        {
            try
            {
                await Shell.Current.GoToAsync("PreparePage");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
            }
        }
    }
}
