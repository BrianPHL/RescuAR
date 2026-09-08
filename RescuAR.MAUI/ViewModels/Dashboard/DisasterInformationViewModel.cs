using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using RescuAR.App.Services.Dashboard;

namespace RescuAR.App.ViewModels.Dashboard;

public partial class DisasterInformationViewModel : ObservableObject
{
    private readonly IDashboardDataService _dataService;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _actionText = string.Empty;

    [ObservableProperty]
    private string _moduleRoute = "AdvisoryFeedPage";

    [ObservableProperty]
    private string _moduleName = string.Empty;

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
        ModuleRoute = "AdvisoryFeedPage";
        ModuleName = data.ModuleName;
    }

    [RelayCommand]
    private void NavigateToDisasterUpdates()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Shell.Current != null)
            {
                try
                {
                    await Shell.Current.Navigation.PushAsync(new Views.Dashboard.AdvisoryFeedPage());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AdvisoryFeed Nav Error: {ex.Message}");
                    await Shell.Current.GoToAsync("AdvisoryFeedPage");
                }
            }
        });
    }
}
