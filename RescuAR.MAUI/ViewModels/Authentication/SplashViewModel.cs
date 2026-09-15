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

        [ObservableProperty]
        private string _quoteText = "";

        private static readonly string[] SafetyQuotes = new[]
        {
            "\"Preparedness is the only way we can combat a natural disaster.\"",
            "\"By failing to prepare, you are preparing to fail.\" - Benjamin Franklin",
            "\"An ounce of prevention is worth a pound of cure.\" - Benjamin Franklin",
            "\"Safety is a state of mind, accidents are an absence of mind.\"",
            "\"Expect the best, plan for the worst, and prepare to be surprised.\" - Denis Waitley"
        };

        public SplashViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _quoteText = SafetyQuotes[new Random().Next(SafetyQuotes.Length)];
        }

        public async Task InitializeAsync()
        {
            await Task.Delay(2000);

            bool isLoggedIn = Preferences.Default.Get("IsLoggedIn", false);
            bool hasSignedUp = Preferences.Default.Get("HasSignedUp", false);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Application.Current != null)
                {
                    if (isLoggedIn)
                    {
                        bool hasPermissions = Preferences.Default.Get("HasCompletedPermissions", false);
                        if (hasPermissions)
                        {
                            AuthenticationNavigation.TrySetRootPage(new AppShell());
                        }
                        else
                        {
                            var permissionsPage = _serviceProvider.GetRequiredService<PermissionsPage>();
                            AuthenticationNavigation.TrySetRootPage(permissionsPage);
                        }
                    }
                    else if (hasSignedUp)
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



