using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using RescuAR.App.Services.Dashboard;

namespace RescuAR.App.ViewModels.Dashboard;

public partial class DisasterInformationViewModel : ObservableObject
{
    private readonly IDashboardDataService _dataService;

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ActionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ModuleRoute { get; set; } = "//Reports/AdvisoryFeed";

    [ObservableProperty]
    public partial string ModuleName { get; set; } = string.Empty;

    public DisasterInformationViewModel() : this(DashboardDataService.Instance)
    {
    }

    public DisasterInformationViewModel(IDashboardDataService dataService)
    {
        _dataService = dataService;
        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        var data = await _dataService.GetDisasterInfoAsync();
        Title = data.Title;
        Description = data.Description;
        ActionText = data.ActionText;
        ModuleRoute = data.ModuleRoute;
        ModuleName = data.ModuleName;
    }

    [RelayCommand]
    private async Task NavigateToDisasterUpdatesAsync()
    {
        if (Shell.Current != null)
        {
            try
            {
                await Shell.Current.GoToAsync(ModuleRoute);
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
