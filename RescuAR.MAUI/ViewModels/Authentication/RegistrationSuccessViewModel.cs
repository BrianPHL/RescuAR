using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using RescuAR.App.Views.Authentication;

namespace RescuAR.App.ViewModels.Authentication
{
    public partial class RegistrationSuccessViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;

        public RegistrationSuccessViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [RelayCommand]
        private void Continue()
        {
            var loginPage = _serviceProvider.GetRequiredService<LoginPage>();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Application.Current != null)
                {
                    Application.Current.MainPage = loginPage;
                }
            });
        }
    }
}
