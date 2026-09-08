using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Profile;

namespace RescuAR.App.Views.Profile
{
    public partial class EmergencyContactsPage : ContentPage
    {
        public EmergencyContactsPage()
        {
            InitializeComponent();
            BindingContext = new ProfileViewModel();
        }
    }
}
