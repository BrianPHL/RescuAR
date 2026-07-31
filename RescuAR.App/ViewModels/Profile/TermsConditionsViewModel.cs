using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using RescuAR.App.Models;

namespace RescuAR.App.ViewModels.Profile
{
    /// <summary>
    /// ViewModel for the Terms &amp; Conditions page.
    /// Manages the list of terms sections.
    /// </summary>
    public partial class TermsConditionsViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<PrivacyPolicyItem> _termsItems = new();

        public TermsConditionsViewModel()
        {
            TermsItems = BuildTermsItems();
        }

        [RelayCommand]
        private async Task GoBack()
        {
            await Shell.Current.GoToAsync("..");
        }

        /// <summary>
        /// Builds all the static terms data matching the reference design.
        /// </summary>
        private static ObservableCollection<PrivacyPolicyItem> BuildTermsItems()
        {
            return new ObservableCollection<PrivacyPolicyItem>
            {
                new()
                {
                    Number = 1,
                    Title = "Use of the Application",
                    Description = "This application is intended to assist users in disaster preparedness and evacuation. It should be used as a support tool, not as a sole source of decision-making during emergencies."
                },
                new()
                {
                    Number = 2,
                    Title = "Accuracy of Information",
                    Description = "While the app uses verified data sources, real-time conditions may change rapidly. Users are advised to remain aware of their surroundings at all times."
                },
                new()
                {
                    Number = 3,
                    Title = "AR Guidance Limitations",
                    Description = "Augmented Reality (AR) navigation depends on device sensors and environmental conditions.",
                    BulletPoints = new ObservableCollection<string>
                    {
                        "Accuracy may be affected by poor lighting, obstructions, or signal loss",
                        "Directions provided should be followed with caution"
                    }
                },
                new()
                {
                    Number = 4,
                    Title = "Connectivity Requirements",
                    Description = "Some features require internet access, including hazard updates and route recalculations. Limited functionality may be available offline."
                },
                new()
                {
                    Number = 5,
                    Title = "User Responsibility",
                    Description = "Users are responsible for:",
                    BulletPoints = new ObservableCollection<string>
                    {
                        "Staying alert during evacuation",
                        "Following official instructions from authorities",
                        "Using the app appropriately in emergency situations"
                    }
                },
                new()
                {
                    Number = 6,
                    Title = "Limitation of Liability",
                    Description = "The developers are not liable for any damages, injuries, or losses resulting from the use or inability to use the application."
                },
                new()
                {
                    Number = 7,
                    Title = "Updates and Changes",
                    Description = "The application may be updated to improve performance and features. Continued use signifies acceptance of any changes."
                }
            };
        }
    }
}
