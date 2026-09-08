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
                SelectedAdvisory = newAdvisory;
                IsPopupVisible = true;
            };
        }

        private async Task LoadUserProfileAsync()
        {
            try
            {
                var client = RescuAR.Services.SupabaseService.Instance.Client;
                if (client != null && client.Auth.CurrentSession != null)
                {
                    var authUser = client.Auth.CurrentSession.User;
                    Models.User? dbUser = null;
                    
                    try
                    {
                        // Try to get the user from the custom User table
                        dbUser = await client.From<Models.User>().Where(x => x.Id == authUser.Id).Single();
                    }
                    catch { } // Ignore if not found, we will use Auth metadata

                    if (dbUser != null)
                    {
                        CurrentUser = dbUser;
                    }
                    else
                    {
                        // Fallback to extracting from the Auth session metadata
                        var user = new Models.User
                        {
                            Id = authUser.Id,
                            Email = authUser.Email ?? string.Empty
                        };

                        if (authUser.UserMetadata != null)
                        {
                            if (authUser.UserMetadata.TryGetValue("first_name", out var fn))
                                user.FirstName = fn.ToString();
                            if (authUser.UserMetadata.TryGetValue("last_name", out var ln))
                                user.LastName = ln.ToString();
                            if (authUser.UserMetadata.TryGetValue("phone", out var ph))
                                user.PhoneNumber = ph.ToString();
                        }

                        // If it's still completely empty, apply some defaults
                        if (string.IsNullOrWhiteSpace(user.FirstName))
                            user.FirstName = "RescuAR";
                        if (string.IsNullOrWhiteSpace(user.LastName))
                            user.LastName = "User";

                        // Try to get custom user row one more time, or just insert/update it later
                        CurrentUser = user;
                    }
                    
                    // Explicitly update Observable properties for reliable MAUI binding
                    if (!string.IsNullOrWhiteSpace(CurrentUser.Username))
                    {
                        FullName = CurrentUser.Username.Trim();
                    }
                    else
                    {
                        FullName = $"{CurrentUser.FirstName} {CurrentUser.LastName}".Trim();
                    }
                    AvatarUrl = CurrentUser.AvatarUrl;
                    
                    // Force complete reference refresh for nested bindings
                    var temp = CurrentUser;
                    CurrentUser = new User();
                    CurrentUser = temp;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading user: {ex.Message}");
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

                    var client = RescuAR.Services.SupabaseService.Instance.Client;
                    if (client != null && client.Auth.CurrentSession != null)
                    {
                        var stream = await result.OpenReadAsync();
                        using var memoryStream = new MemoryStream();
                        await stream.CopyToAsync(memoryStream);
                        var bytes = memoryStream.ToArray();

                        var fileName = $"{client.Auth.CurrentSession.User.Id}-{Guid.NewGuid()}.jpg";
                        
                        // Save locally first so the UI works even if Supabase Storage fails
                        var localPath = Path.Combine(FileSystem.AppDataDirectory, fileName);
                        File.WriteAllBytes(localPath, bytes);
                        
                        AvatarUrl = localPath;
                        CurrentUser.AvatarUrl = localPath;
                        
                        try
                        {
                            await client.Storage.From("avatars").Upload(bytes, fileName, new Supabase.Storage.FileOptions { Upsert = true });
                            var publicUrl = client.Storage.From("avatars").GetPublicUrl(fileName);
                            AvatarUrl = publicUrl;
                            CurrentUser.AvatarUrl = publicUrl;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Supabase storage upload failed, using local cache: {ex.Message}");
                        }
                        
                        // Re-trigger bindings
                        var tempUser = CurrentUser;
                        CurrentUser = new User();
                        CurrentUser = tempUser;
                        
                        // Try to save to DB
                        try {
                            await client.From<User>().Upsert(CurrentUser);
                            await Shell.Current.DisplayAlert("Success", "Profile picture updated successfully!", "OK");
                        } catch { }
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
