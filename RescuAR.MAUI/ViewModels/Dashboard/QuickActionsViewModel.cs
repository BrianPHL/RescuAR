using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;

namespace RescuAR.App.ViewModels.Dashboard;

public partial class QuickActionItem : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string IconData { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TargetRoute { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ModuleName { get; set; } = string.Empty;
}

public partial class QuickActionsViewModel : ObservableObject
{
    public ObservableCollection<QuickActionItem> Actions { get; } = new();

    public QuickActionsViewModel()
    {
        LoadActions();
    }

    private void LoadActions()
    {
        Actions.Add(new QuickActionItem
        {
            Title = "Report Incident",
            Subtitle = "Report / Create a Report",
            IconData = "M12,2L1,21H23L12,2M12,6L19.8,20H4.2L12,6M11,10V14H13V10H11M11,16V18H13V16H11Z",
            TargetRoute = "//Reports/CommunityPosting",
            ModuleName = "Report Incident Module"
        });

        Actions.Add(new QuickActionItem
        {
            Title = "Emergency Hotlines",
            Subtitle = "Prepare / Emergency Hotlines",
            IconData = "M12,2A10,10 0 0,0 2,12V17A3,3 0 0,0 5,20H8V12H4V12A8,8 0 0,1 12,4A8,8 0 0,1 20,12V12H16V20H19A3,3 0 0,0 22,17V12A10,10 0 0,0 12,2Z",
            TargetRoute = "//Prepare/HotlineDirectory",
            ModuleName = "Emergency Hotlines Directory"
        });

        Actions.Add(new QuickActionItem
        {
            Title = "Find Nearest Evacuation Center",
            Subtitle = "Prepare / Evacuation Centers",
            IconData = "M12,2C8.13,2 5,5.13 5,9C5,14.25 12,22 12,22C12,22 19,14.25 19,9C19,5.13 15.87,2 12,2M12,11.5A2.5,2.5 0 0,1 9.5,9A2.5,2.5 0 0,1 12,6.5A2.5,2.5 0 0,1 14.5,9A2.5,2.5 0 0,1 12,11.5Z",
            TargetRoute = "//Prepare/EvacuationCenterInfo",
            ModuleName = "Evacuation Center Info"
        });

        Actions.Add(new QuickActionItem
        {
            Title = "Safety Circle",
            Subtitle = "Map / Safety Circle",
            IconData = "M16,13C15.71,13 15.42,13.04 15.15,13.11C14.07,12.38 12.77,12 11.33,12C7.03,12 3,15.5 3,19H19.67C18.66,17.2 17.5,15.2 16,13Z M11.33,4A4,4 0 0,0 7.33,8A4,4 0 0,0 11.33,12A4,4 0 0,0 15.33,8A4,4 0 0,0 11.33,4Z",
            TargetRoute = "//Map/SafetyCircle",
            ModuleName = "Safety Circle Module"
        });
    }

    [RelayCommand]
    private async Task ExecuteActionAsync(QuickActionItem action)
    {
        if (action == null) return;

        if (Shell.Current != null)
        {
            try
            {
                string route = action.TargetRoute;
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
                    $"Redirecting to link reference:\n{action.TargetRoute}\n\nTarget Module: {action.ModuleName}",
                    "OK");
            }
        }
    }

    [RelayCommand]
    private async Task EditQuickActionsAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.DisplayAlertAsync(
                "Quick Actions",
                "Customize Quick Actions layout...",
                "OK");
        }
    }
}
