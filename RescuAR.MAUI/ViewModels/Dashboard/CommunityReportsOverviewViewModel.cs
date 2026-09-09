using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using RescuAR.App.Services.Dashboard;

namespace RescuAR.App.ViewModels.Dashboard;

public partial class CommunityReportsOverviewViewModel : ObservableObject
{
    private readonly IDashboardDataService _dataService;

    [ObservableProperty]
    private string _report1Title = string.Empty;

    [ObservableProperty]
    private string _report1Distance = string.Empty;

    [ObservableProperty]
    private string _report2Title = string.Empty;

    [ObservableProperty]
    private string _report2Distance = string.Empty;

    [ObservableProperty]
    private string _actionText = string.Empty;

    [ObservableProperty]
    private string _moduleRoute = "//Reports";

    [ObservableProperty]
    private string _moduleName = string.Empty;

    public CommunityReportsOverviewViewModel() : this(DashboardDataService.Instance)
    {
    }

    public CommunityReportsOverviewViewModel(IDashboardDataService dataService)
    {
        _dataService = dataService;
        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        var data = await _dataService.GetCommunityReportsDataAsync();
        if (data.Reports.Count >= 2)
        {
            Report1Title = data.Reports[0].Title;
            Report1Distance = data.Reports[0].DistanceText;
            Report2Title = data.Reports[1].Title;
            Report2Distance = data.Reports[1].DistanceText;
        }
        ActionText = data.ActionText;
        ModuleRoute = data.ModuleRoute;
        ModuleName = data.ModuleName;
    }

    [RelayCommand]
    private async Task NavigateToReportsAsync()
    {
        if (Shell.Current != null)
        {
            try
            {
                foreach (var item in Shell.Current.Items)
                {
                    foreach (var section in item.Items)
                    {
                        foreach (var content in section.Items)
                        {
                            if (content.Route == "Reports" || content.Title == "Reports")
                            {
                                Shell.Current.CurrentItem = content;
                                return;
                            }
                        }
                    }
                }
                await Shell.Current.GoToAsync("//Reports");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
            }
        }
    }
}
