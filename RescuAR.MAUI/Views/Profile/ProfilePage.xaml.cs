using Microsoft.Maui.Controls;
using Microsoft.Extensions.DependencyInjection;
using RescuAR.App.Views.Authentication;

namespace RescuAR.App.Views.Profile
{
    public partial class ProfilePage : ContentPage
    {
        public ProfilePage()
        {
            InitializeComponent();
        }

        private async void OnSignOutClicked(object sender, EventArgs e)
        {
            bool confirm = await DisplayAlert("Sign Out", "Are you sure you want to sign out?", "Yes", "No");
            if (confirm)
            {
                Preferences.Default.Set("IsLoggedIn", false);

                // Try to sign out of Supabase if possible (don't block on error)
                try
                {
                    var authService = Application.Current?.Handler?.MauiContext?.Services.GetService<Services.Authentication.AuthenticationService>();
                    if (authService != null)
                    {
                        await authService.SignOutAsync();
                    }
                }
                catch { }

                var loginPage = Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<LoginPage>();
                if (loginPage != null && Application.Current != null)
                {
                    Application.Current.Windows[0].Page = loginPage;
                }
            }
        }
    }
}
