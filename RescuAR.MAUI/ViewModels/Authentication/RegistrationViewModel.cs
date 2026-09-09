using System;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        private string _contactNumber = string.Empty;

        [ObservableProperty]
        private string _password = string.Empty;

        [ObservableProperty]
        private string _confirmPassword = string.Empty;

        [ObservableProperty]
        private bool _hasMinLength;

        [ObservableProperty]
        private bool _hasSpecialChar;

        [ObservableProperty]
        private bool _hasDigit;

        [ObservableProperty]
        private bool _hasUpperCase;

        partial void OnPasswordChanged(string value)
        {
            if (value == null) value = string.Empty;
            HasMinLength = value.Length >= 10;
            HasSpecialChar = Regex.IsMatch(value, @"[!@#$%^&*()]");
            HasDigit = Regex.IsMatch(value, @"\d");
            HasUpperCase = Regex.IsMatch(value, @"[A-Z]");
        }

        [ObservableProperty]
        private bool _isTermsAccepted = false;

        [ObservableProperty]
        private bool _isPasswordVisible = false;

        [ObservableProperty]
        private bool _isConfirmPasswordVisible = false;

        public bool IsPasswordHidden => !IsPasswordVisible;
        public bool IsConfirmPasswordHidden => !IsConfirmPasswordVisible;

        // SVG Paths for Eye and Eye-Off
        private const string EyeIcon = "M12,9A3,3 0 0,0 9,12A3,3 0 0,0 12,15A3,3 0 0,0 15,12A3,3 0 0,0 12,9M12,17C8.13,17 4.79,14.65 3.32,11.5C4.79,8.35 8.13,6 12,6C15.87,6 19.21,8.35 20.68,11.5C19.21,14.65 15.87,17 12,17M12,4.5C7,4.5 2.73,7.61 1,11.5C2.73,15.39 7,18.5 12,18.5C17,18.5 21.27,15.39 23,11.5C21.27,7.61 17,4.5 12,4.5Z";
        private const string EyeOffIcon = "M11.83,9L15,12.16C15,12.11 15,12.05 15,12A3,3 0 0,0 12,9C11.94,9 11.89,9 11.83,9M7.53,9.8L9.08,11.35C9.03,11.54 9,11.76 9,12A3,3 0 0,0 12,15C12.24,15 12.46,14.97 12.65,14.92L14.2,16.47C13.53,16.8 12.79,17 12,17C8.13,17 4.79,14.65 3.32,11.5C4.38,9.45 6.09,7.9 8.15,7.03L7.53,9.8M2,4.27L4.28,6.55L4.73,7C3.08,8.3 1.78,10 1,11.5C2.73,15.39 7,18.5 12,18.5C13.84,18.5 15.58,18.11 17.15,17.43L17.59,17.87L19.73,20L21,18.73L3.27,3L2,4.27M12,4.5C17,4.5 21.27,7.61 23,11.5C22.25,13 21.14,14.33 19.8,15.34L18.42,13.96C19.46,13.1 20.25,12 20.68,11.5C19.21,8.35 15.87,6 12,6C11.12,6 10.26,6.15 9.46,6.43L8.09,5.06C9.28,4.7 10.6,4.5 12,4.5Z";

        public string PasswordToggleIcon => IsPasswordVisible ? EyeIcon : EyeOffIcon;
        public string ConfirmPasswordToggleIcon => IsConfirmPasswordVisible ? EyeIcon : EyeOffIcon;

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
                await ShowErrorAsync("Full Name (First Name) is required.");
                return;
            }

            if (string.IsNullOrWhiteSpace(LastName))
            {
                await ShowErrorAsync("Last Name is required.");
                return;
            }

            if (string.IsNullOrWhiteSpace(Email))
            {
                await ShowErrorAsync("Email Address is required.");
                return;
            }

            if (!IsValidEmail(Email))
            {
                await ShowErrorAsync("Please enter a valid email address.");
                return;
            }

            if (string.IsNullOrWhiteSpace(ContactNumber))
            {
                await ShowErrorAsync("Contact Number is required.");
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                await ShowErrorAsync("Password is required.");
                return;
            }

            if (!HasMinLength || !HasSpecialChar || !HasDigit || !HasUpperCase)
            {
                await ShowErrorAsync("Please meet all the password requirements.");
                return;
            }

            if (Password != ConfirmPassword)
            {
                await ShowErrorAsync("Passwords do not match.");
                return;
            }

            if (!IsTermsAccepted)
            {
                await ShowErrorAsync("You must agree to the Terms & Conditions and Privacy Policy.");
                return;
            }

            IsLoading = true;
            try
            {
                // Register via Supabase
                var session = await _authService.SignUpWithEmailAsync(Email.Trim(), Password, FirstName.Trim(), LastName.Trim(), string.IsNullOrWhiteSpace(MiddleName) ? null : MiddleName.Trim(), ContactNumber.Trim());

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    // If email confirmation is disabled in Supabase, the user is logged in immediately
                    if (session != null && !string.IsNullOrEmpty(session.AccessToken))
                    {
                        if (Application.Current != null)
                        {
                            AuthenticationNavigation.TrySetRootPage(new AppShell());
                        }
                    }
                    else if (AuthenticationNavigation.RootPage is NavigationPage navPage)
                    {
                        var otpPage = _serviceProvider.GetRequiredService<OtpVerificationPage>();
                        var vm = (OtpVerificationViewModel)otpPage.BindingContext;
                        vm.Email = Email.Trim();
                        await navPage.PushAsync(otpPage);
                    }
                });
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                if (!string.IsNullOrEmpty(msg) && msg.Trim().StartsWith("{"))
                {
                    try
                    {
                        var json = JsonDocument.Parse(msg);
                        if (json.RootElement.TryGetProperty("msg", out var msgProp) || json.RootElement.TryGetProperty("message", out msgProp) || json.RootElement.TryGetProperty("error_description", out msgProp))
                        {
                            msg = msgProp.GetString();
                        }
                    }
                    catch { }
                }
                
                string finalError = msg ?? "An error occurred during registration. Please try again.";
                await ShowErrorAsync(finalError);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ShowErrorAsync(string message)
        {
            ErrorMessage = message;
            OnPropertyChanged(nameof(HasError));
            
            if (AuthenticationNavigation.RootPage != null)
            {
                await AuthenticationNavigation.RootPage!.DisplayAlert("Registration Error", message, "OK");
            }
        }

        [RelayCommand]
        private void Back()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (AuthenticationNavigation.RootPage is NavigationPage navPage)
                {
                    await navPage.PopAsync();
                }
            });
        }

        [RelayCommand]
        private void GoToSignIn()
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (AuthenticationNavigation.RootPage is NavigationPage navPage)
                {
                    // If we came from login, pop back. If not, maybe push login or go back to root
                    var loginPage = _serviceProvider.GetRequiredService<LoginPage>();
                    await navPage.PushAsync(loginPage);
                }
            });
        }

        [RelayCommand]
        private void GoToTerms()
        {
            var termsPage = _serviceProvider.GetRequiredService<TermsAndConditionsPage>();
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (AuthenticationNavigation.RootPage is NavigationPage navPage)
                {
                    await navPage.PushAsync(termsPage);
                }
            });
        }

        [RelayCommand]
        private void GoToPrivacy()
        {
            var privacyPage = _serviceProvider.GetRequiredService<PrivacyPolicyPage>();
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (AuthenticationNavigation.RootPage is NavigationPage navPage)
                {
                    await navPage.PushAsync(privacyPage);
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
