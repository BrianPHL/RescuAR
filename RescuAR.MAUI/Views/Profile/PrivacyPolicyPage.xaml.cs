using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Profile;

namespace RescuAR.App.Views.Profile
{
    public partial class PrivacyPolicyPage : ContentPage
    {
        public PrivacyPolicyPage(PrivacyPolicyViewModel? viewModel = null)
        {
            InitializeComponent();
            BindingContext = viewModel ?? new PrivacyPolicyViewModel();
        }
    }
}
