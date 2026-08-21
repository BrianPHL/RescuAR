using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using RescuAR.App.Services.Dashboard;

namespace RescuAR.App.ViewModels.Dashboard;

public partial class SafetyCircleOverviewViewModel : ObservableObject
{
    private readonly IDashboardDataService _dataService;

    [ObservableProperty]
    private string circle1Name = string.Empty;

    [ObservableProperty]
    private string circle1Status = string.Empty;

    [ObservableProperty]
    private string circle2Name = string.Empty;

    [ObservableProperty]
    private string circle2Status = string.Empty;

    [ObservableProperty]
    private string actionText = string.Empty;

    [ObservableProperty]
    private string moduleRoute = "//Map/SafetyCircle";

    [ObservableProperty]
    private string moduleName = string.Empty;

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
        if (data.Groups.Count >= 2)
        {
            Circle1Name = data.Groups[0].Name;
            Circle1Status = data.Groups[0].StatusText;
            Circle2Name = data.Groups[1].Name;
            Circle2Status = data.Groups[1].StatusText;
        }
        ActionText = data.ActionText;
        ModuleRoute = data.ModuleRoute;
        ModuleName = data.ModuleName;
    }

    [RelayCommand]
    private async Task NavigateToSafetyCircleAsync()
    {
        if (Shell.Current != null)
        {
            try
            {
                await Shell.Current.GoToAsync(ModuleRoute);
            }
            catch (Exception)
            {
                await Shell.Current.DisplayAlert(
                    "Link Redirection",
                    $"Redirecting to link reference:\n{ModuleRoute}\n\nTarget Module: {ModuleName}",
                    "OK");
            }
        }
    }
}
