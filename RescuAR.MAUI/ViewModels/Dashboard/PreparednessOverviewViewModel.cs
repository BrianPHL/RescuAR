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
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ActionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ModuleRoute { get; set; } = "//Prepare/Checklist";

    [ObservableProperty]
    public partial string ModuleName { get; set; } = string.Empty;

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
        ModuleRoute = data.ModuleRoute;
        ModuleName = data.ModuleName;
    }

    [RelayCommand]
    private async Task NavigateToPreparationProgressAsync()
    {
        if (Shell.Current != null)
        {
            try
            {
                string route = ModuleRoute;
                if (route.StartsWith("//") && !route.Equals("//Camera") && !route.Equals("//Home"))
                {
                    route = route.Substring(2);
                }
                await Shell.Current.GoToAsync(route);
            }
            catch (Exception)
            {
                await Shell.Current.DisplayAlertAsync(
                    "Link Redirection",
                    $"Redirecting to link reference:\n{ModuleRoute}\n\nTarget Module: {ModuleName}",
                    "OK");
            }
        }
    }
}
