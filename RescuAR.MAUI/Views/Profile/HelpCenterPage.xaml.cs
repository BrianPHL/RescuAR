using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Profile;

namespace RescuAR.App.Views.Profile
{
    public partial class HelpCenterPage : ContentPage
    {
        public HelpCenterPage(HelpCenterViewModel? viewModel = null)
        {
            InitializeComponent();
            BindingContext = viewModel ?? new HelpCenterViewModel();
        }
    }
}
