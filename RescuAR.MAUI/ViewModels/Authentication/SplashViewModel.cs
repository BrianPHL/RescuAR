using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using RescuAR.App.Views.Authentication;

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
            // Simulate loading safety resources
            await Task.Delay(2500);

            // Navigate to RegistrationPage
            var registrationPage = _serviceProvider.GetRequiredService<RegistrationPage>();
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Application.Current != null)
                {
                    Application.Current.MainPage = registrationPage;
                }
            });
        }
    }
}

