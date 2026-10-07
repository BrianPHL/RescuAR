using Microsoft.Maui.Controls;

namespace RescuAR.App.Views.Profile
{
    public partial class SystemInformationPage : ContentPage
    {
        public SystemInformationPage()
        {
            InitializeComponent();
            BindingContext = new RescuAR.App.ViewModels.Profile.SystemInformationViewModel();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (BindingContext is RescuAR.App.ViewModels.Profile.SystemInformationViewModel vm) await vm.RefreshAsync();
        }
    }
}
