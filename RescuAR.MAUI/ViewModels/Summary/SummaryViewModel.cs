using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;

namespace RescuAR.App.ViewModels.Summary
{
    public partial class ChecklistActionItem : ObservableObject
    {
        [ObservableProperty]
        private string text = string.Empty;

        [ObservableProperty]
        private bool isCompleted;

        public string CheckIconColor => IsCompleted ? "#0A8491" : "#B0B0B0";
        public string TextColor => IsCompleted ? "#000000" : "#555555";

        partial void OnIsCompletedChanged(bool value)
        {
            OnPropertyChanged(nameof(CheckIconColor));
            OnPropertyChanged(nameof(TextColor));
        }
    }

    public partial class SummaryViewModel : ObservableObject
    {
        [ObservableProperty]
        private string riskBadgeText = "● Increased Risk";

        [ObservableProperty]
        private string riskTitle = "2 active advisories within 3 km. Evacuation guidance available.";

        [ObservableProperty]
        private string nearestCenterSummary = "877 meters to nearest evacuation center (Malanday Elementary School)";

        [ObservableProperty]
        private string lastUpdatedText = "Last updated just now";

        [ObservableProperty]
        private string situationAssessmentText = "Heavy rainfall and rising river levels have been reported within your vicinity. Current conditions do not require immediate evacuation, but preparedness measures are strongly recommended.";

        [ObservableProperty]
        private string safeZoneName = "Malanday Elementary School";

        [ObservableProperty]
        private string safeZoneDistance = "877 meters away";

        [ObservableProperty]
        private string safeZoneAddress = "48 Visayas St., Malanday\nMarikina City 1805";

        [ObservableProperty]
        private string safeZoneVerifiedBy = "Marikina LGU";

        [ObservableProperty]
        private string lastArSession = "Never";

        [ObservableProperty]
        private string preparednessProgressText = "6/10 items completed";

        [ObservableProperty]
        private string lastViewedCenterText = "Malanday Element...";

        [ObservableProperty]
        private string lastViewedAdvisoryText = "Water Level in Marik...";

        public ObservableCollection<ChecklistActionItem> StandardActions { get; } = new();
        public ObservableCollection<ChecklistActionItem> WorsenedConditionsActions { get; } = new();

        public SummaryViewModel()
        {
            LoadChecklistActions();
        }

        private void LoadChecklistActions()
        {
            StandardActions.Clear();
            StandardActions.Add(new ChecklistActionItem { Text = "Prepare emergency supplies", IsCompleted = true });
            StandardActions.Add(new ChecklistActionItem { Text = "Charge mobile devices", IsCompleted = true });
            StandardActions.Add(new ChecklistActionItem { Text = "Monitor official advisories", IsCompleted = true });
            StandardActions.Add(new ChecklistActionItem { Text = "Identify nearest evacuation center", IsCompleted = true });

            WorsenedConditionsActions.Clear();
            WorsenedConditionsActions.Add(new ChecklistActionItem { Text = "Proceed to evacuation center", IsCompleted = true });
            WorsenedConditionsActions.Add(new ChecklistActionItem { Text = "Follow AR evacuation guidance", IsCompleted = true });
            WorsenedConditionsActions.Add(new ChecklistActionItem { Text = "Assist vulnerable family members", IsCompleted = true });
        }

        [RelayCommand]
        private void ToggleActionItem(ChecklistActionItem item)
        {
            if (item != null)
            {
                item.IsCompleted = !item.IsCompleted;
            }
        }

        [RelayCommand]
        private async Task GoBackAsync()
        {
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("..");
            }
        }

        [RelayCommand]
        private async Task OpenNotificationsAsync()
        {
            if (Shell.Current != null)
            {
                await Shell.Current.DisplayAlert(
                    "Emergency Alerts",
                    "Push notification monitoring is active for your area.",
                    "OK");
            }
        }

        [RelayCommand]
        private async Task ViewSafeZoneDetailsAsync()
        {
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("Prepare/EvacuationCenterInfo");
            }
        }

        [RelayCommand]
        private async Task StartArEvacuationAsync()
        {
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("//Camera");
            }
        }

        [RelayCommand]
        private async Task OpenPreparednessChecklistAsync()
        {
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("Prepare/Checklist");
            }
        }

        [RelayCommand]
        private async Task OpenLastViewedCenterAsync()
        {
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("Prepare/EvacuationCenterInfo");
            }
        }

        [RelayCommand]
        private async Task OpenLastViewedAdvisoryAsync()
        {
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("AdvisoryFeedPage");
            }
        }
    }
}
