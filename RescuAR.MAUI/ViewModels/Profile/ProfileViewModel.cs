using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Media;
using RescuAR.App.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace RescuAR.App.ViewModels.Profile
{
    public partial class ProfileViewModel : ObservableObject
    {
        [ObservableProperty]
        private User _currentUser = new();

        [ObservableProperty]
        private SafetyCircle? _currentCircle;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotInCircle))]
        private bool _isInCircle;

        [ObservableProperty]
        private string _fullName = string.Empty;

        [ObservableProperty]
        private string _avatarUrl = string.Empty;

        public bool IsNotInCircle => !IsInCircle;

        public List<string> BloodTypes { get; } = new() { "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-" };

        public ProfileViewModel()
        {
            _ = LoadUserProfileAsync();

            RescuAR.App.Services.Reports.RealtimeAdvisoryManager.OnNewAdvisoryPushed += (newAdvisory) =>
            {
                if (RescuAR.MAUI.Services.Settings.AppPreferences.IsEnabled(RescuAR.MAUI.Services.Settings.AppSetting.ForegroundAdvisories))
                {
                    SelectedAdvisory = newAdvisory;
                    IsPopupVisible = true;
                }
            };
            RescuAR.MAUI.Services.Settings.AppPreferences.ObserveWeak(this, static (vm, setting) =>
            {
                if (setting == RescuAR.MAUI.Services.Settings.AppSetting.ForegroundAdvisories &&
                    !RescuAR.MAUI.Services.Settings.AppPreferences.IsEnabled(setting))
                    MainThread.BeginInvokeOnMainThread(() => vm.IsPopupVisible = false);
            });
        }

        private Task LoadUserProfileAsync() => RefreshProfileAsync();

        [RelayCommand]
        private async Task SaveProfileAsync()
        {
            var client = RescuAR.Services.SupabaseService.Instance.Client;
            if (client != null)
            {
                try
                {
                    // Use Upsert so if the user record doesn't exist in the custom table yet, it gets created.
                    await client.From<User>().Upsert(CurrentUser);
                }
                catch (Exception ex)
                {
                    if (Shell.Current != null)
                    {
                        await Shell.Current.DisplayAlert("Error", $"Failed to save profile: {ex.Message}", "OK");
                    }
                    return;
                }
            }
            if (Shell.Current != null)
            {
                await Shell.Current.DisplayAlert("Success", "Profile updated successfully!", "OK");
            }
        }

        [RelayCommand]
        private void CreateCircle()
        {
            var newId = "CIR-" + System.Guid.NewGuid().ToString().Substring(0, 5).ToUpper();
            CurrentCircle = new SafetyCircle
            {
                CircleId = newId,
                CircleName = $"{CurrentUser.FirstName}'s Safety Circle",
                InviteLink = $"rescuar://circle/join?id={newId}"
            };
            CurrentUser.CircleId = CurrentCircle.CircleId;
            IsInCircle = true;
        }

        [RelayCommand]
        private void LeaveCircle()
        {
            CurrentCircle = null;
            CurrentUser.CircleId = string.Empty;
            IsInCircle = false;
        }

        [RelayCommand]
        private async Task ShareInviteLinkAsync()
        {
            if (CurrentCircle == null) return;

            await Clipboard.Default.SetTextAsync(CurrentCircle.InviteLink);
            if (Shell.Current != null)
            {
                await Shell.Current.DisplayAlert("Copied", "Invite link copied to clipboard!", "OK");
            }
        }

        [RelayCommand]
        private async Task ProcessDeepLinkAsync()
        {
            if (Shell.Current != null)
            {
                bool accept = await Shell.Current.DisplayAlert(
                    "Circle Invitation",
                    "Brian invited you to join 'Brian's Safety Circle'. Do you want to join?",
                    "Accept",
                    "Decline");

                if (accept)
                {
                    CurrentCircle = new SafetyCircle
                    {
                        CircleId = "CIR-BRIAN",
                        CircleName = "Brian's Safety Circle",
                        InviteLink = "rescuar://circle/join?id=CIR-BRIAN"
                    };
                    CurrentUser.CircleId = CurrentCircle.CircleId;
                    IsInCircle = true;
                }
            }
        }

        // --- Advisory Popup ---
        [ObservableProperty]
        private RescuAR.App.Models.DisasterAdvisory? _selectedAdvisory;

        [ObservableProperty]
        private bool _isPopupVisible;

        [RelayCommand]
        private void ClosePopup()
        {
            IsPopupVisible = false;
            SelectedAdvisory = null;
            RescuAR.App.Services.Reports.RealtimeAdvisoryManager.StopAlarmAudio();
        }

        [RelayCommand]
        private async Task GoToAdvisoriesFeedAsync()
        {
            ClosePopup();
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("AdvisoryFeedPage");
            }
        }
    }
}
