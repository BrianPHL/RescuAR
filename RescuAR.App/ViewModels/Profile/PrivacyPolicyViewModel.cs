using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using RescuAR.App.Models;

namespace RescuAR.App.ViewModels.Profile
{
    /// <summary>
    /// ViewModel for the Privacy Policy page.
    /// Manages the list of policy sections.
    /// </summary>
    public partial class PrivacyPolicyViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<PrivacyPolicyItem> _policyItems = new();

        public PrivacyPolicyViewModel()
        {
            PolicyItems = BuildPolicyItems();
        }

        [RelayCommand]
        private async Task GoBack()
        {
            await Shell.Current.GoToAsync("..");
        }

        /// <summary>
        /// Builds all the static privacy policy data matching the reference design.
        /// </summary>
        private static ObservableCollection<PrivacyPolicyItem> BuildPolicyItems()
        {
            return new ObservableCollection<PrivacyPolicyItem>
            {
                new()
                {
                    Number = 1,
                    Title = "Information We Collect",
                    Description = "We collect limited data necessary to provide evacuation guidance:",
                    BulletPoints = new ObservableCollection<string>
                    {
                        "Location Data – to determine your position and nearby evacuation routes",
                        "Camera Access – to enable Augmented Reality (AR) navigation",
                        "Motion & Orientation Data – to align AR directions with your movement",
                        "Device Information – for system performance and compatibility"
                    }
                },
                new()
                {
                    Number = 2,
                    Title = "How We Use Your Information",
                    Description = "Your data is used solely to:",
                    BulletPoints = new ObservableCollection<string>
                    {
                        "Provide real-time evacuation guidance",
                        "Display hazard alerts and safe routes",
                        "Improve navigation accuracy and system performance"
                    }
                },
                new()
                {
                    Number = 3,
                    Title = "Data Storage and Security",
                    Description = "We do not store personal location data permanently. All data is used in real-time and handled securely to prevent unauthorized access."
                },
                new()
                {
                    Number = 4,
                    Title = "Data Sharing",
                    Description = "We do not sell or share your personal data with third parties. Data may only be used with verified sources for disaster-related updates."
                },
                new()
                {
                    Number = 5,
                    Title = "User Control",
                    Description = "You may disable permissions (location, camera, motion) at any time through your device settings. However, doing so may limit core app functionality."
                },
                new()
                {
                    Number = 6,
                    Title = "Updates to This Policy",
                    Description = "This policy may be updated to reflect improvements in the system. Continued use of the app means you accept these updates."
                }
            };
        }
    }
}
