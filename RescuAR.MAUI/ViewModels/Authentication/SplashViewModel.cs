using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using RescuAR.App.Views.Authentication;

using RescuAR.App.Services.Authentication;

using RescuAR.MAUI;

namespace RescuAR.App.ViewModels.Authentication
{
    public partial class SplashViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;

        [ObservableProperty]
        private string _statusText = "Loading safety resources...";

        [ObservableProperty]
        private string _versionText = "v0.0.1a";

        public SplashViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task InitializeAsync()
        {
            await Task.Delay(2000);

            bool hasSignedUp = Preferences.Default.Get("HasSignedUp", false);
            try
            {
                var client = await RescuAR.Services.SupabaseService.Instance.GetClientAsync();
                if (!string.IsNullOrWhiteSpace(client?.Auth.CurrentSession?.AccessToken) &&
                    !string.IsNullOrWhiteSpace(client.Auth.CurrentUser?.Id))
                {
                    await AuthenticationNavigation.CompleteSignInAsync(_serviceProvider);
                    return;
                }
            }
            catch { StatusText = "Sign in to reconnect to your account."; }
            RescuAR.App.Services.Profile.UserProfileService.ClearIdentity();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Application.Current != null)
                {
                    if (hasSignedUp)
                    {
                        var loginPage = _serviceProvider.GetRequiredService<LoginPage>();
                        AuthenticationNavigation.TrySetRootPage(new NavigationPage(loginPage));
                    }
                    else
                    {
                        var onboardingPage = _serviceProvider.GetRequiredService<OnboardingPage>();
                        AuthenticationNavigation.TrySetRootPage(new NavigationPage(onboardingPage));
                    }
                }
            });
        }
    }
}
