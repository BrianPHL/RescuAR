using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Profile;

namespace RescuAR.App.Views.Profile
{
    public partial class SafetyCircleSettingsPage : ContentPage
    {
        public SafetyCircleSettingsViewModel ViewModel { get; }

        public SafetyCircleSettingsPage(SafetyCircleSettingsViewModel viewModel)
        {
            ViewModel = viewModel;
            InitializeComponent();
            BindingContext = ViewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await ViewModel.RefreshAsync();
        }
    }
}
