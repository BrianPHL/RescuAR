using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using RescuAR.App.Models;

namespace RescuAR.App.ViewModels.Dashboard;

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
            Title = "Flashlight",
            Subtitle = "Tap to turn phone flashlight ON / OFF",
            IconData = "M9,2A1,1 0 0,0 8,3V8.5L10.5,11V21A1,1 0 0,0 11.5,22H12.5A1,1 0 0,0 13.5,21V11L16,8.5V3A1,1 0 0,0 15,2H9M10,4H14V6H10V4Z",
            ActionType = "Flashlight"
        });

        Actions.Add(new QuickActionItem
        {
            Title = "Report Incident",
            Subtitle = "Report / Community Reports Feed",
            IconData = "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20M11,15H13V17H11V15M11,11H13V13H11V11Z",
            TargetRoute = "//Reports"
        });

        Actions.Add(new QuickActionItem
        {
            Title = "Emergency Hotlines",
            Subtitle = "Prepare / Emergency Hotlines",
            IconData = "M6.62,10.79C8.06,13.62 10.38,15.94 13.21,17.38L15.41,15.18C15.69,14.9 16.08,14.82 16.43,14.93C17.55,15.3 18.75,15.5 20,15.5A1,1 0 0,1 21,16.5V20A1,1 0 0,1 20,21C10.61,21 3,13.39 3,4A1,1 0 0,1 4,3H7.5A1,1 0 0,1 8.5,4C8.5,5.25 8.7,6.45 9.07,7.57C9.18,7.92 9.1,8.31 8.82,8.59L6.62,10.79Z",
            TargetRoute = "Prepare/HotlineDirectory"
        });

        Actions.Add(new QuickActionItem
        {
            Title = "Find Nearest Evacuation Center",
            Subtitle = "Prepare / Evacuation Centers",
            IconData = "M10,20V14H14V20H19V12H22L12,3L2,12H5V20H10Z",
            TargetRoute = "Prepare/EvacuationCenterInfo"
        });

        Actions.Add(new QuickActionItem
        {
            Title = "Safety Circle",
            Subtitle = "Map / Safety Circle",
            IconData = "M16,13C15.71,13 15.42,13.04 15.15,13.11C14.07,12.38 12.77,12 11.33,12C7.03,12 3,15.5 3,19H19.67C18.66,17.2 17.5,15.2 16,13Z M11.33,4A4,4 0 0,0 7.33,8A4,4 0 0,0 11.33,12A4,4 0 0,0 15.33,8A4,4 0 0,0 11.33,4Z",
            TargetRoute = "SafetyCirclePage"
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
                if (action.TargetRoute == "//Reports" || action.TargetRoute == "Reports")
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
                }
                await Shell.Current.GoToAsync(action.TargetRoute);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task EditQuickActionsAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.DisplayAlert(
                "Quick Actions",
                "Customize Quick Actions layout...",
                "OK");
        }
    }
}
