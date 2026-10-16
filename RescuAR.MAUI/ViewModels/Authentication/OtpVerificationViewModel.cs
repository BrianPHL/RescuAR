using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using RescuAR.App.Services.Authentication;

namespace RescuAR.App.ViewModels.Authentication
{
    [QueryProperty(nameof(Email), "email")]
    public partial class OtpVerificationViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly AuthenticationService _authService;

        [ObservableProperty]
        private string _email = string.Empty;

        [ObservableProperty]
        private string _digit1 = string.Empty;
        [ObservableProperty]
        private string _digit2 = string.Empty;
        [ObservableProperty]
        private string _digit3 = string.Empty;
        [ObservableProperty]
        private string _digit4 = string.Empty;
        [ObservableProperty]
        private string _digit5 = string.Empty;
        [ObservableProperty]
        private string _digit6 = string.Empty;

        public Models.ResidentProfileDraft? Draft { get; set; }
        private string? _verifiedUserId;

        public string OtpCode => $"{Digit1}{Digit2}{Digit3}{Digit4}{Digit5}{Digit6}";

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private bool _isSuccessPopupVisible = false;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
        public bool IsNotLoading => !IsLoading;

        partial void OnIsLoadingChanged(bool value)
        {
            OnPropertyChanged(nameof(IsNotLoading));
        }

        public OtpVerificationViewModel(IServiceProvider serviceProvider, AuthenticationService authService)
        {
            _serviceProvider = serviceProvider;
            _authService = authService;
        }

        [RelayCommand]
        private async Task VerifyOtpAsync()
        {
            if (IsLoading) return;
            if (!System.Text.RegularExpressions.Regex.IsMatch(OtpCode, @"^[0-9]{6}$"))
            {
                ErrorMessage = "Please enter a valid 6-digit code.";
                OnPropertyChanged(nameof(HasError));
                return;
            }

            IsLoading = true;
            ErrorMessage = string.Empty;
            OnPropertyChanged(nameof(HasError));

            try
            {
                var client = await RescuAR.Services.SupabaseService.Instance.GetClientAsync()
                    ?? throw new InvalidOperationException("Supabase is not configured.");
                if (client != null)
                {
                    // Verify the OTP via Supabase
                    var session = _verifiedUserId != null && client.Auth.CurrentUser?.Id == _verifiedUserId
                        ? client.Auth.CurrentSession
                        : await client.Auth.VerifyOTP(Email, OtpCode, Supabase.Gotrue.Constants.EmailOtpType.Signup);
                    if (session?.User != null && !string.IsNullOrWhiteSpace(session.AccessToken))
                    {
                        _verifiedUserId = session.User.Id;
                        if (Draft != null) await RescuAR.App.Services.Profile.UserProfileService.Instance.SaveDraftAsync(Draft);
                        // Show Custom Styled Popup
                        IsSuccessPopupVisible = true;
                    }
                    else
                    {
                        ErrorMessage = "Verification failed. Please check your code and try again.";
                        OnPropertyChanged(nameof(HasError));
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message ?? "Invalid OTP code. Please try again.";
                OnPropertyChanged(nameof(HasError));
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ContinueToAddressAsync()
        {
            IsSuccessPopupVisible = false;
            try { await AuthenticationNavigation.CompleteSignInAsync(_serviceProvider); }
            catch (Exception ex) { ErrorMessage = ex.Message; OnPropertyChanged(nameof(HasError)); }
        }
    }
}
