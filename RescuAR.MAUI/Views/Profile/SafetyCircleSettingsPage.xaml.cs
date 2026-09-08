using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Profile;

namespace RescuAR.App.Views.Profile
{
    public partial class SafetyCircleSettingsPage : ContentPage
    {
        public SafetyCircleSettingsPage()
        {
            InitializeComponent();
            BindingContext = new ProfileViewModel(); // Shared logic for Profile
        }
    }
}
