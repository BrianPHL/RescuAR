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
        private async Task SelectAccount(string email)
        {
            if (IsLoading) return;

            IsLoading = true;
            ErrorMessage = string.Empty;
            OnPropertyChanged(nameof(HasError));

            try
            {
                // In a simulated or actual Google Auth flow, selecting a cached account 
                // signs the user in.
                // We'll perform a quick mock delay, then route to the Dashboard (AppShell).
                await Task.Delay(1500);

                string firstName = "User";
                string lastName = "";

                if (email == "qblcpasco@tip.edu.ph")
                {
                    firstName = "Brian Lawrence";
                    lastName = "Pasco";
                }
                else if (email == "frances@example-email.com")
                {
                    firstName = "Frances";
                    lastName = "Pasco";
                }
                else if (!string.IsNullOrWhiteSpace(email))
                {
                    string[] parts = email.Split('@');
                    if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                    {
                        firstName = char.ToUpper(parts[0][0]) + parts[0].Substring(1);
                    }
                }

                Preferences.Default.Set("UserFirstName", firstName);
                Preferences.Default.Set("UserLastName", lastName);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (Application.Current != null)
                    {
                        Preferences.Default.Set("IsLoggedIn", true);
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
                });
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
        private async Task UseAnotherAccount()
        {
            if (IsLoading) return;

            IsLoading = true;
            ErrorMessage = string.Empty;
            OnPropertyChanged(nameof(HasError));

            try
            {
                // Trigger the actual Supabase OAuth Google authentication flow (with browser + 2FA)
                await _authService.SignInWithGoogleAsync();

                // Navigate to Permissions or Dashboard upon success
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (Application.Current != null)
                    {
                        Preferences.Default.Set("IsLoggedIn", true);
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
                });
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
