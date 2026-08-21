using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using RescuAR.App.Views.Authentication;
using RescuAR.App.Services.Authentication;

namespace RescuAR.App.ViewModels.Authentication
{
    public partial class RegistrationViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly AuthenticationService _authService;

        [ObservableProperty]
        private string _firstName = string.Empty;

        [ObservableProperty]
        private string _middleName = string.Empty;

        [ObservableProperty]
        private string _lastName = string.Empty;

        [ObservableProperty]
        private string _email = string.Empty;

        [ObservableProperty]
        private string _password = string.Empty;

        [ObservableProperty]
        private string _confirmPassword = string.Empty;

        [ObservableProperty]
        private bool _isTermsAccepted = false;

        [ObservableProperty]
        private bool _isPasswordVisible = false;

        [ObservableProperty]
        private bool _isConfirmPasswordVisible = false;

        public bool IsPasswordHidden => !IsPasswordVisible;
        public bool IsConfirmPasswordHidden => !IsConfirmPasswordVisible;

        public string PasswordToggleIcon => IsPasswordVisible ? "👁" : "🙈";
        public string ConfirmPasswordToggleIcon => IsConfirmPasswordVisible ? "👁" : "🙈";

        partial void OnIsPasswordVisibleChanged(bool value)
        {
            OnPropertyChanged(nameof(IsPasswordHidden));
            OnPropertyChanged(nameof(PasswordToggleIcon));
        }

        partial void OnIsConfirmPasswordVisibleChanged(bool value)
        {
            OnPropertyChanged(nameof(IsConfirmPasswordHidden));
            OnPropertyChanged(nameof(ConfirmPasswordToggleIcon));
        }

        [ObservableProperty]
        private bool _isLoading = false;

        public bool IsNotLoading => !IsLoading;

        partial void OnIsLoadingChanged(bool value)
        {
            OnPropertyChanged(nameof(IsNotLoading));
        }

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        public RegistrationViewModel(IServiceProvider serviceProvider, AuthenticationService authService)
        {
            _serviceProvider = serviceProvider;
            _authService = authService;
        }

        [RelayCommand]
        private void TogglePasswordVisibility()
        {
            IsPasswordVisible = !IsPasswordVisible;
        }

        [RelayCommand]
        private void ToggleConfirmPasswordVisibility()
        {
            IsConfirmPasswordVisible = !IsConfirmPasswordVisible;
        }

        [RelayCommand]
        private async Task CreateAccount()
        {
            if (IsLoading) return;

            ErrorMessage = string.Empty;
            OnPropertyChanged(nameof(HasError));

            // Validate Fields
            if (string.IsNullOrWhiteSpace(FirstName))
            {
                ErrorMessage = "Full Name (First Name) is required.";
                OnPropertyChanged(nameof(HasError));
                return;
            }

            if (string.IsNullOrWhiteSpace(LastName))
            {
                ErrorMessage = "Last Name is required.";
                OnPropertyChanged(nameof(HasError));
                return;
            }

            if (string.IsNullOrWhiteSpace(Email))
            {
                ErrorMessage = "Email Address is required.";
                OnPropertyChanged(nameof(HasError));
                return;
            }

            if (!IsValidEmail(Email))
            {
                ErrorMessage = "Please enter a valid email address.";
                OnPropertyChanged(nameof(HasError));
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Password is required.";
                OnPropertyChanged(nameof(HasError));
                return;
            }

            if (Password.Length < 6)
            {
                ErrorMessage = "Password must be at least 6 characters.";
                OnPropertyChanged(nameof(HasError));
                return;
            }

            if (Password != ConfirmPassword)
            {
                ErrorMessage = "Passwords do not match.";
                OnPropertyChanged(nameof(HasError));
                return;
            }

            if (!IsTermsAccepted)
            {
                ErrorMessage = "You must agree to the Terms & Conditions and Privacy Policy.";
                OnPropertyChanged(nameof(HasError));
                return;
            }

            IsLoading = true;
            try
            {
                // Register via Supabase
                await _authService.SignUpWithEmailAsync(Email.Trim(), Password, FirstName.Trim(), LastName.Trim(), string.IsNullOrWhiteSpace(MiddleName) ? null : MiddleName.Trim());

                // Navigate to Success Page
                var successPage = _serviceProvider.GetRequiredService<RegistrationSuccessPage>();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (Application.Current != null)
                    {
                        Preferences.Default.Set("HasSignedUp", true);
                        Application.Current.MainPage = successPage;
                    }
                });
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message ?? "An error occurred during registration. Please try again.";
                OnPropertyChanged(nameof(HasError));
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void Back()
        {
            var onboardingPage = _serviceProvider.GetRequiredService<OnboardingPage>();
            if (onboardingPage.BindingContext is OnboardingViewModel onboardingVm)
            {
                onboardingVm.SetSlideIndex(3); // Go to Entry Screen
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Application.Current != null)
                {
                    Application.Current.MainPage = onboardingPage;
                }
            });
        }

        [RelayCommand]
        private void GoToSignIn()
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

        private bool IsValidEmail(string email)
        {
            try
            {
                return Regex.IsMatch(email,
                    @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
                    RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }
}
