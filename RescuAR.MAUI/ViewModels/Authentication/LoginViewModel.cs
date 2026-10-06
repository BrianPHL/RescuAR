using System;
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
    public partial class LoginViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly AuthenticationService _authService;

        [ObservableProperty]
        private string _email = string.Empty;

        [ObservableProperty]
        private string _password = string.Empty;

        [ObservableProperty]
        private bool _isPasswordVisible = false;

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

        public bool IsPasswordHidden => !IsPasswordVisible;

        // SVG Paths for Eye and Eye-Off
        private const string EyeIcon = "M12,9A3,3 0 0,0 9,12A3,3 0 0,0 12,15A3,3 0 0,0 15,12A3,3 0 0,0 12,9M12,17C8.13,17 4.79,14.65 3.32,11.5C4.79,8.35 8.13,6 12,6C15.87,6 19.21,8.35 20.68,11.5C19.21,14.65 15.87,17 12,17M12,4.5C7,4.5 2.73,7.61 1,11.5C2.73,15.39 7,18.5 12,18.5C17,18.5 21.27,15.39 23,11.5C21.27,7.61 17,4.5 12,4.5Z";
        private const string EyeOffIcon = "M11.83,9L15,12.16C15,12.11 15,12.05 15,12A3,3 0 0,0 12,9C11.94,9 11.89,9 11.83,9M7.53,9.8L9.08,11.35C9.03,11.54 9,11.76 9,12A3,3 0 0,0 12,15C12.24,15 12.46,14.97 12.65,14.92L14.2,16.47C13.53,16.8 12.79,17 12,17C8.13,17 4.79,14.65 3.32,11.5C4.38,9.45 6.09,7.9 8.15,7.03L7.53,9.8M2,4.27L4.28,6.55L4.73,7C3.08,8.3 1.78,10 1,11.5C2.73,15.39 7,18.5 12,18.5C13.84,18.5 15.58,18.11 17.15,17.43L17.59,17.87L19.73,20L21,18.73L3.27,3L2,4.27M12,4.5C17,4.5 21.27,7.61 23,11.5C22.25,13 21.14,14.33 19.8,15.34L18.42,13.96C19.46,13.1 20.25,12 20.68,11.5C19.21,8.35 15.87,6 12,6C11.12,6 10.26,6.15 9.46,6.43L8.09,5.06C9.28,4.7 10.6,4.5 12,4.5Z";

        public string PasswordToggleIcon => IsPasswordVisible ? EyeIcon : EyeOffIcon;

        partial void OnIsPasswordVisibleChanged(bool value)
        {
            OnPropertyChanged(nameof(IsPasswordHidden));
            OnPropertyChanged(nameof(PasswordToggleIcon));
        }

        public LoginViewModel(IServiceProvider serviceProvider, AuthenticationService authService)
        {
            _serviceProvider = serviceProvider;
            _authService = authService;
            IsRememberMe = Preferences.Default.Get("RememberMe", false);
            if (IsRememberMe) Email = Preferences.Default.Get("SavedLoginEmail", string.Empty);
        }

        [ObservableProperty] private bool _isRememberMe;
        [ObservableProperty] private bool _isResetPasswordModalVisible;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsResetEmailStep))]
        [NotifyPropertyChangedFor(nameof(IsResetCodeStep))] private int _resetStep = 1;
        [ObservableProperty] private string _resetEmail = string.Empty;
        [ObservableProperty] private string _resetOtpCode = string.Empty;
        [ObservableProperty] private string _resetNewPassword = string.Empty;
        [ObservableProperty] private string _resetConfirmPassword = string.Empty;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasResetError))] private string _resetErrorMessage = string.Empty;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsResetNotLoading))] private bool _isResetLoading;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsResetEmailStep))]
        [NotifyPropertyChangedFor(nameof(IsResetCodeStep))] private bool _isResetSuccess;
        public bool HasResetError => !string.IsNullOrEmpty(ResetErrorMessage);
        public bool IsResetNotLoading => !IsResetLoading;
        public bool IsResetEmailStep => ResetStep == 1 && !IsResetSuccess;
        public bool IsResetCodeStep => ResetStep == 2 && !IsResetSuccess;

        partial void OnIsRememberMeChanged(bool value)
        {
            Preferences.Default.Set("RememberMe", value);
            if (!value) Preferences.Default.Remove("SavedLoginEmail");
        }

        [RelayCommand] private void ToggleRememberMe() => IsRememberMe = !IsRememberMe;

        [RelayCommand]
        private void TogglePasswordVisibility()
        {
            IsPasswordVisible = !IsPasswordVisible;
        }

        [RelayCommand]
        private async Task Login()
        {
            if (IsLoading) return;

            ErrorMessage = string.Empty;
            OnPropertyChanged(nameof(HasError));

            if (string.IsNullOrWhiteSpace(Email))
            {
                ErrorMessage = "Email Address is required.";
                OnPropertyChanged(nameof(HasError));
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Password is required.";
                OnPropertyChanged(nameof(HasError));
                return;
            }

            IsLoading = true;
            try
            {
                // Authenticate with email/password using Supabase
                var session = await _authService.SignInWithEmailAsync(Email.Trim(), Password);

                if (string.IsNullOrWhiteSpace(session.User?.Id) || string.IsNullOrWhiteSpace(session.AccessToken))
                    throw new InvalidOperationException("The authenticated account is unavailable.");
                if (IsRememberMe) Preferences.Default.Set("SavedLoginEmail", Email.Trim());
                else Preferences.Default.Remove("SavedLoginEmail");
                Password = string.Empty;
                await AuthenticationNavigation.CompleteSignInAsync(_serviceProvider);
            }
            catch (Exception ex)
            {
                string msg = ex.Message ?? string.Empty;

                if (msg.Contains("Email not confirmed", StringComparison.OrdinalIgnoreCase))
                {
                    ErrorMessage = "Please confirm your email address via the link sent to your inbox before logging in.";
                }
                else if (msg.Contains("invalid_credentials", StringComparison.OrdinalIgnoreCase) ||
                         msg.Contains("Invalid login credentials", StringComparison.OrdinalIgnoreCase))
                {
                    ErrorMessage = "Invalid email or password. Please check your credentials and try again.";
                }
                else
                {
                    ErrorMessage = string.IsNullOrWhiteSpace(msg)
                        ? "Failed to log in. Please check your internet connection or credentials."
                        : msg;
                }

                OnPropertyChanged(nameof(HasError));
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void GoogleSignIn()
        {
            var googleAuthPage = _serviceProvider.GetRequiredService<GoogleAuthPage>();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Application.Current != null)
                {
                    AuthenticationNavigation.TrySetRootPage(googleAuthPage);
                }
            });
        }

        [RelayCommand]
        private void GoToSignUp()
        {
            var registrationPage = _serviceProvider.GetRequiredService<RegistrationPage>();
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (AuthenticationNavigation.RootPage is NavigationPage navPage)
                {
                    await navPage.PushAsync(registrationPage);
                }
            });
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
        private void OpenResetPasswordModal()
        {
            ResetEmail = Email.Trim(); ResetOtpCode = ResetNewPassword = ResetConfirmPassword = ResetErrorMessage = "";
            ResetStep = 1; IsResetSuccess = false; IsResetPasswordModalVisible = true;
        }

        [RelayCommand]
        private async Task CloseResetPasswordModal()
        {
            if (IsResetLoading) return;
            IsResetPasswordModalVisible = false;
            ResetOtpCode = ResetNewPassword = ResetConfirmPassword = "";
            await RescuAR.Services.SupabaseService.Instance.SetSessionPersistenceAsync(true);
        }

        [RelayCommand]
        private async Task SendResetOtp()
        {
            if (IsResetLoading) return;
            if (!Regex.IsMatch(ResetEmail.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            { ResetErrorMessage = "Enter a valid email address."; return; }
            IsResetLoading = true; ResetErrorMessage = "";
            try
            {
                var client = await RescuAR.Services.SupabaseService.Instance.GetClientAsync()
                    ?? throw new InvalidOperationException("Supabase is not configured.");
                await client.Auth.ResetPasswordForEmail(ResetEmail.Trim());
                ResetStep = 2;
            }
            catch (Exception ex) { ResetErrorMessage = ex.Message; }
            finally { IsResetLoading = false; }
        }

        [RelayCommand]
        private async Task ConfirmResetPassword()
        {
            if (IsResetLoading) return;
            if (!Regex.IsMatch(ResetOtpCode, @"^[0-9]{6}$"))
            { ResetErrorMessage = "Enter the six-digit recovery code."; return; }
            if (ResetNewPassword.Length < 10 || !Regex.IsMatch(ResetNewPassword, @"[A-Z]") ||
                !Regex.IsMatch(ResetNewPassword, @"\d") || !Regex.IsMatch(ResetNewPassword, @"[!@#$%^&*()]"))
            { ResetErrorMessage = "Use at least 10 characters, an uppercase letter, a number and a symbol (!@#$%^&*())."; return; }
            if (ResetNewPassword != ResetConfirmPassword)
            { ResetErrorMessage = "Passwords do not match."; return; }
            IsResetLoading = true; ResetErrorMessage = "";
            try
            {
                var cloud = RescuAR.Services.SupabaseService.Instance;
                var client = await cloud.GetClientAsync() ?? throw new InvalidOperationException("Supabase is not configured.");
                // A recovery session must never become a normal persisted app login.
                await cloud.SetSessionPersistenceAsync(false);
                var session = await client.Auth.VerifyOTP(ResetEmail.Trim(), ResetOtpCode, Supabase.Gotrue.Constants.EmailOtpType.Recovery);
                if (string.IsNullOrWhiteSpace(session?.AccessToken) || session?.User == null)
                    throw new InvalidOperationException("The recovery code could not be verified.");
                try
                {
                    var updated = await client.Auth.Update(new Supabase.Gotrue.UserAttributes { Password = ResetNewPassword });
                    if (updated == null) throw new InvalidOperationException("The password was not updated.");
                    IsResetSuccess = true;
                    ResetOtpCode = ResetNewPassword = ResetConfirmPassword = "";
                }
                finally
                {
                    try { await _authService.SignOutAsync(); }
                    catch { /* ClearSessionAsync in SignOutAsync still removes the local session. */ }
                }
            }
            catch (Exception ex) { ResetErrorMessage = ex.Message; }
            finally
            {
                await RescuAR.Services.SupabaseService.Instance.ClearSessionAsync();
                RescuAR.App.Services.Profile.UserProfileService.ClearIdentity();
                IsResetLoading = false;
            }
        }
    }
}
