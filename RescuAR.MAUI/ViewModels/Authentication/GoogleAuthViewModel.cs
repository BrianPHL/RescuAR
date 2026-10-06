using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using RescuAR.App.Views.Authentication;
using RescuAR.App.Services.Authentication;

using RescuAR.MAUI;

namespace RescuAR.App.ViewModels.Authentication
{
    public partial class GoogleAuthViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly AuthenticationService _authService;

        [ObservableProperty]
        private string _appName = "RescuAR";

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        public bool IsNotLoading => !IsLoading;

        partial void OnIsLoadingChanged(bool value)
        {
            OnPropertyChanged(nameof(IsNotLoading));
        }

        public GoogleAuthViewModel(IServiceProvider serviceProvider, AuthenticationService authService)
        {
            _serviceProvider = serviceProvider;
            _authService = authService;
        }

        [RelayCommand]
        private async Task UseAnotherAccount()
        {
            if (IsLoading) return;

            IsLoading = true;
            ErrorMessage = string.Empty;
            OnPropertyChanged(nameof(HasError));

            try
            {
                var session = await _authService.SignInWithGoogleAsync();
                if (string.IsNullOrWhiteSpace(session.User?.Id) || string.IsNullOrWhiteSpace(session.AccessToken))
                    throw new InvalidOperationException("Google did not return an authenticated account.");
                await AuthenticationNavigation.CompleteSignInAsync(_serviceProvider);
            }
            catch (OperationCanceledException)
            {
                ErrorMessage = "Google Authentication was cancelled.";
                OnPropertyChanged(nameof(HasError));
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message ?? "Failed to authenticate with Google.";
                OnPropertyChanged(nameof(HasError));
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void Cancel()
        {
            var loginPage = _serviceProvider.GetRequiredService<LoginPage>();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Application.Current != null)
                {
                    AuthenticationNavigation.TrySetRootPage(new NavigationPage(loginPage));
                }
            });
        }
    }
}
