using Microsoft.Maui.Controls;
using Microsoft.Extensions.DependencyInjection;
using RescuAR.App.Views.Authentication;
using RescuAR.App.ViewModels.Profile;
using System;
using Microsoft.Maui.Storage;

namespace RescuAR.App.Views.Profile
{
    public partial class ProfilePage : ContentPage
    {
        public ProfilePage()
        {
            InitializeComponent();
            BindingContext = new ProfileViewModel();
        }

        private async void OnPersonalInformationTapped(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(PersonalInformationPage));
        }

        private async void OnHealthInformationTapped(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(HealthInformationPage));
        }

        private async void OnSafetyCircleTapped(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(SafetyCircleSettingsPage));
        }

        private async void OnEmergencyContactsTapped(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(EmergencyContactsPage));
        }

        private async void OnAppSettingsTapped(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(AppSettingsPage));
        }

        private async void OnHelpCenterTapped(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(HelpCenterPage));
        }

        private async void OnPrivacyPolicyTapped(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(PrivacyPolicyPage));
        }

        private async void OnTermsConditionsTapped(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(TermsConditionsPage));
        }

        private async void OnSystemInformationTapped(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(SystemInformationPage));
        }

        private async void OnSignOutClicked(object? sender, EventArgs e)
        {
            bool confirm = await DisplayAlert("Sign Out", "Are you sure you want to sign out?", "Yes", "No");
            if (confirm)
            {
                Preferences.Default.Set("IsLoggedIn", false);

                try
                {
                    var authService = Application.Current?.Handler?.MauiContext?.Services.GetService<RescuAR.App.Services.Authentication.AuthenticationService>();
                    if (authService != null)
                    {
                        await authService.SignOutAsync();
                    }
                }
                catch { }

                var loginPage = Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<LoginPage>();
                if (loginPage != null && Application.Current?.Windows.Count > 0)
                {
                    Application.Current.Windows[0].Page = loginPage;
                }
            }
        }
    }
}
