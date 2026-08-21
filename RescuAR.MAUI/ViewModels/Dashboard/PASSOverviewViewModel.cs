using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using RescuAR.App.Services.Dashboard;

namespace RescuAR.App.ViewModels.Dashboard;

public partial class PASSOverviewViewModel : ObservableObject
{
    private readonly IDashboardDataService _dataService;

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ScoreText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ButtonText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ModuleRoute { get; set; } = "//Prepare/PASS";

    [ObservableProperty]
    public partial string ModuleName { get; set; } = string.Empty;

    public PASSOverviewViewModel() : this(DashboardDataService.Instance)
    {
    }

    public PASSOverviewViewModel(IDashboardDataService dataService)
    {
        _dataService = dataService;
        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        var data = await _dataService.GetPASSDataAsync();
        Title = data.Title;
        ScoreText = $"{data.ScorePercentage}% prepared";
        Description = data.Description;
        ButtonText = data.ButtonText;
        ModuleRoute = data.ModuleRoute;
        ModuleName = data.ModuleName;
    }

    [RelayCommand]
    private async Task TakeAssessmentAsync()
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
