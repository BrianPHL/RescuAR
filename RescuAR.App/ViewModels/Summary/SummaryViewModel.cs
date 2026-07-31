using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;

namespace RescuAR.App.ViewModels.Summary
{
    public partial class SummaryViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _overviewTitle = "2 active advisories within 3 km. Evacuation guidance available.";
        
        [ObservableProperty]
        private string _overviewSubtitle = "877 meters to nearest evacuation center (Malanday Elementary School)";
        
        [ObservableProperty]
        private string _lastUpdated = "Last updated just now";

        [ObservableProperty]
        private string _situationAssessmentText = "Heavy rainfall and rising river levels have been reported within your vicinity. Current conditions do not require immediate evacuation, but preparedness measures are strongly recommended.";

        [ObservableProperty]
        private string _nearestSafeZoneName = "Malanday Elementary School";

        [ObservableProperty]
        private string _nearestSafeZoneDistance = "877 meters away";

        [ObservableProperty]
        private string _nearestSafeZoneAddress = "48 Visayas St., Malanday\nMarikina City 1805";

        [ObservableProperty]
        private string _nearestSafeZoneVerifiedBy = "Marikina LGU";

        public ObservableCollection<string> ImmediateActions { get; } = new()
        {
            "Prepare emergency supplies",
            "Charge mobile devices",
            "Monitor official advisories",
            "Identify nearest evacuation center"
        };

        public ObservableCollection<string> ContingencyActions { get; } = new()
        {
            "Proceed to evacuation center",
            "Follow AR evacuation guidance",
            "Assist vulnerable family members"
        };

        [RelayCommand]
        private async Task GoBack()
        {
            await Shell.Current.GoToAsync("..");
        }

        [RelayCommand]
        private async Task ViewMoreDetails()
        {
            await Shell.Current.GoToAsync(nameof(Views.Summary.NearbySafeLocationsPage));
        }

        [RelayCommand]
        private async Task StartPracticeAR()
        {
            // Navigation to AR practice (keeping this as stub since AR is a separate core feature)
        }

        [RelayCommand]
        private async Task ViewPreparednessProgress()
        {
            await Shell.Current.GoToAsync(nameof(Views.Summary.RecommendedActionPage));
        }

        [RelayCommand]
        private async Task ViewLastEvacuationCenter()
        {
            await Shell.Current.GoToAsync(nameof(Views.Summary.ActivityHistoryPage));
        }

        [RelayCommand]
        private async Task ViewLastAdvisory()
        {
            await Shell.Current.GoToAsync(nameof(Views.Summary.SituationAsessmentPage));
        }
    }
}
