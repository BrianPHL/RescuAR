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

            RescuAR.App.Services.Reports.RealtimeAdvisoryManager.OnAdvisoryPopupRequested += (newAdvisory) =>
            {
                SelectedAdvisory = newAdvisory;
                IsPopupVisible = true;
            };
        }


        public async Task LoadUserProfileAsync()
        {
            try
            {
                var profiles = Services.Profile.UserProfileService.Instance;
                CurrentUser = await profiles.LoadAsync();
                FullName = string.IsNullOrWhiteSpace(CurrentUser.Username)
                    ? $"{CurrentUser.FirstName} {CurrentUser.LastName}".Trim() : CurrentUser.Username;
                var local = profiles.GetLocal("AvatarPath");
                AvatarUrl = File.Exists(local) ? local : CurrentUser.AvatarUrl;
            }
            catch (Exception ex)
            {
                CurrentUser = new User(); FullName = AvatarUrl = string.Empty;
                if (Shell.Current != null) await Shell.Current.DisplayAlert("Could not load profile", ex.Message, "OK");
            }
        }

        [RelayCommand]
        private async Task ChangeAvatarAsync()
        {
            try
            {
                if (Shell.Current == null) return;

                string action = await Shell.Current.DisplayActionSheet("Update Profile Picture", "Cancel", null, "Take a Picture", "Choose from Gallery");
                if (string.IsNullOrEmpty(action) || action == "Cancel") return;

                FileResult? result = null;

                if (action == "Take a Picture")
                {
                    var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
                    if (status != PermissionStatus.Granted)
                    {
                        status = await Permissions.RequestAsync<Permissions.Camera>();
                        if (status != PermissionStatus.Granted)
                        {
                            await Shell.Current.DisplayAlert("Permission Denied", "Camera permission is required to take a picture.", "OK");
                            return;
                        }
                    }
                    if (MediaPicker.Default.IsCaptureSupported)
                    {
                        result = await MediaPicker.Default.CapturePhotoAsync();
                    }
                }
                else if (action == "Choose from Gallery")
                {
                    // Android's system photo picker does not require broad
                    // storage/photo-library permission for this operation.
                    result = await MediaPicker.Default.PickPhotoAsync();
                }

                if (result != null)
                {
                    bool confirm = await Shell.Current.DisplayAlert("Confirm Upload", "Do you want to use this image as your profile picture?", "Yes", "No");
                    if (!confirm) return;


                    var profiles = Services.Profile.UserProfileService.Instance;
                    var client = await profiles.RequireClientAsync();
                    var id = profiles.UserId;
                    using var stream = await result.OpenReadAsync();
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream);
                    var bytes = memoryStream.ToArray();
                    var extension = Path.GetExtension(result.FileName).ToLowerInvariant();
                    if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp"))
                        throw new InvalidOperationException("Choose a JPEG, PNG or WebP image.");
                    var fileName = $"{id}-{Guid.NewGuid():N}{extension}";
                    var localPath = Path.Combine(FileSystem.AppDataDirectory, fileName);
                    await File.WriteAllBytesAsync(localPath, bytes);
                    if (id != profiles.UserId) throw new InvalidOperationException("Your account changed. Reopen this screen.");
                    profiles.SetLocal("AvatarPath", localPath);
                    AvatarUrl = localPath;
                    try
                    {
                        await client.Storage.From("avatars").Upload(bytes, fileName, new Supabase.Storage.FileOptions { Upsert = true });
                        var publicUrl = client.Storage.From("avatars").GetPublicUrl(fileName);
                        if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                            throw new InvalidOperationException("The avatar service returned an invalid URL.");
                        if (id != profiles.UserId) throw new InvalidOperationException("Your account changed. Reopen this screen.");
                        CurrentUser = await profiles.PatchAsync((x => x.AvatarUrl, publicUrl));
                        await Shell.Current.DisplayAlert("Saved", "Your profile picture was saved to your account.", "OK");
                    }
                    catch (Exception ex)
                    {
                        await Shell.Current.DisplayAlert("Saved on this device", $"Your picture is visible on this device, but was not saved to your account: {ex.Message}", "OK");
                    }
                }
            }
            catch (Exception ex)
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.DisplayAlert("Error", $"Could not update avatar: {ex.Message}", "OK");
                }
            }
        }

        [RelayCommand]
        private async Task SaveProfileAsync()
        {

            try
            {
                var profiles = Services.Profile.UserProfileService.Instance;
                if (CurrentUser.Id != profiles.UserId) throw new InvalidOperationException("Reopen your profile before saving.");
                // This command serves the dev health form. Other editors own their own fields.
                CurrentUser = await profiles.PatchAsync((x => x.HealthCardNumber, CurrentUser.HealthCardNumber),
                    (x => x.BloodType, CurrentUser.BloodType), (x => x.Allergies, CurrentUser.Allergies),
                    (x => x.MaintenanceMedications, CurrentUser.MaintenanceMedications),
                    (x => x.AverageBloodPressure, CurrentUser.AverageBloodPressure),
                    (x => x.DisabilityOrSpecialNeeds, CurrentUser.DisabilityOrSpecialNeeds),
                    (x => x.IsOrganDonor, CurrentUser.IsOrganDonor));
                if (Shell.Current != null) await Shell.Current.DisplayAlert("Saved", "Health information saved to your account.", "OK");
            }
            catch (Exception ex)
            {
                if (Shell.Current != null) await Shell.Current.DisplayAlert("Changes not saved", ex.Message, "OK");
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
