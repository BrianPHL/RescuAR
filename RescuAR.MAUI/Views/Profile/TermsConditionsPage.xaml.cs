using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Profile;

namespace RescuAR.App.Views.Profile
{
    public partial class TermsConditionsPage : ContentPage
    {
        public TermsConditionsPage(TermsConditionsViewModel? viewModel = null)
        {
            InitializeComponent();
            BindingContext = viewModel ?? new TermsConditionsViewModel();
        }
    }
}
