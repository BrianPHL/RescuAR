using Microsoft.Maui.Controls;

namespace RescuAR.App.Views.Profile
{
    public partial class AppSettingsPage : ContentPage
    {
        public AppSettingsPage()
        {
            InitializeComponent();
            BindingContext = new RescuAR.App.ViewModels.Profile.AppSettingsViewModel();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (BindingContext is RescuAR.App.ViewModels.Profile.AppSettingsViewModel vm)
                await vm.InitializeAsync();
        }
    }
}
