using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Media;
using RescuAR.App.Models;
using RescuAR.App.Services.Profile;
using RescuAR.Services;

namespace RescuAR.App.ViewModels.Profile;

public partial class ProfileViewModel
{
    private readonly UserProfileService profiles = UserProfileService.Instance;
    private readonly MauiAvatarUpdateSource photos = new();
    private ProfileAccountScope? profileOwner;
    private string localPhoto = string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasProfileStatus))] private string profileStatus = string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasAvatarStatus))] private string avatarStatus = string.Empty;
    [ObservableProperty] private bool isProfileLoading;
    [ObservableProperty] private bool isAvatarBusy;
    [ObservableProperty] private bool canSyncAvatar;
    public bool HasProfileStatus => !string.IsNullOrWhiteSpace(ProfileStatus);
    public bool HasAvatarStatus => !string.IsNullOrWhiteSpace(AvatarStatus);

    public async Task RefreshProfileAsync()
    {
        if (IsProfileLoading || IsAvatarBusy) return;
        IsProfileLoading = true;
        try
        {
            var expected = profiles.CaptureOwner();
            if (profileOwner != expected) ClearOverview();
            profileOwner = expected;
            var cached = profiles.ReadCached(expected.UserId);
            CurrentUser = cached ?? new User { Id = expected.UserId, Email = SupabaseService.Instance.Client?.Auth.CurrentUser?.Email ?? string.Empty };
            CurrentUser.Email = SupabaseService.Instance.Client?.Auth.CurrentUser?.Email ?? CurrentUser.Email;
            PresentProfile(expected);
            ProfileStatus = "Loading your profile…";
            var result = await profiles.LoadForDisplayAsync(expected);
            profiles.RequireOwner(expected);
            CurrentUser = result.User;
            PresentProfile(expected);
            ProfileStatus = result.Cached ? "Showing your saved profile while offline." : string.Empty;
        }
        catch (OperationCanceledException) { ClearOverview(); ProfileStatus = "Your account changed. Reopen this screen."; }
        catch (Exception)
        {
            if (profileOwner is not { } current || !profiles.IsCurrentOwner(current)) ClearOverview();
            ProfileStatus = "Your profile could not be refreshed. Any displayed details are saved on this device.";
        }
        finally { IsProfileLoading = false; }
    }

    private void PresentProfile(ProfileAccountScope owner)
    {
        profiles.RequireOwner(owner);
        FullName = string.IsNullOrWhiteSpace(CurrentUser.Username)
            ? $"{CurrentUser.FirstName} {CurrentUser.LastName}".Trim() : CurrentUser.Username.Trim();
        var local = photos.ReadLocal(owner, CurrentUser.AvatarUrl);
        localPhoto = local.Path;
        AvatarUrl = string.IsNullOrEmpty(local.Path) ? CurrentUser.AvatarUrl : local.Path;
        CanSyncAvatar = local.Pending;
        AvatarStatus = local.Pending ? "Photo saved on this device only. Retry to save it to your account." : string.Empty;
    }

    private void ClearOverview()
    {
        CurrentUser = new User(); FullName = string.Empty; AvatarUrl = string.Empty;
        AvatarStatus = string.Empty; CanSyncAvatar = false; localPhoto = string.Empty; profileOwner = null;
    }

    [RelayCommand]
    private async Task ChangeAvatarAsync()
    {
        if (IsAvatarBusy || profileOwner is not { } expected || Shell.Current is not { } page) return;
        IsAvatarBusy = true;
        try
        {
            profiles.RequireOwner(expected);
            string action = await page.DisplayActionSheet("Update Profile Picture", "Cancel", null, "Take a Picture", "Choose from Gallery");
            profiles.RequireOwner(expected);
            FileResult? result = null;
            if (action == "Take a Picture")
            {
                var permission = await Permissions.CheckStatusAsync<Permissions.Camera>();
                if (permission != PermissionStatus.Granted) permission = await Permissions.RequestAsync<Permissions.Camera>();
                profiles.RequireOwner(expected);
                if (permission != PermissionStatus.Granted) { AvatarStatus = "Camera permission is required to take a picture."; return; }
                if (!MediaPicker.Default.IsCaptureSupported) { AvatarStatus = "This device cannot take a picture. Choose one from your gallery."; return; }
                result = await MediaPicker.Default.CapturePhotoAsync();
            }
            else if (action == "Choose from Gallery")
                result = await MediaPicker.Default.PickPhotoAsync(); // The system picker needs no broad storage permission.
            profiles.RequireOwner(expected);
            if (result is null) return;
            bool confirm = await page.DisplayAlert("Confirm Profile Picture", "Use this image as your profile picture?", "Yes", "No");
            profiles.RequireOwner(expected);
            if (!confirm) return;
            using var stream = await result.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            profiles.RequireOwner(expected);
            var saved = await new AvatarUpdateFlow(photos).SaveAsync(expected, buffer.ToArray(), result.FileName, path =>
            {
                localPhoto = path; AvatarUrl = path; CanSyncAvatar = true;
                AvatarStatus = "Photo saved on this device. Saving to your account…";
            });
            ApplyPhotoResult(expected, saved);
        }
        catch (OperationCanceledException) { if (profileOwner == expected && !profiles.IsCurrentOwner(expected)) ClearOverview(); }
        catch (Exception) { if (profiles.IsCurrentOwner(expected)) AvatarStatus = "The photo could not be saved. Please try again."; }
        finally { IsAvatarBusy = false; }
    }

    [RelayCommand]
    private async Task RetryPhotoSyncAsync()
    {
        if (IsAvatarBusy || !CanSyncAvatar || profileOwner is not { } expected) return;
        IsAvatarBusy = true;
        try { ApplyPhotoResult(expected, await new AvatarUpdateFlow(photos).SyncAsync(expected, localPhoto)); }
        catch (OperationCanceledException) { if (profileOwner == expected && !profiles.IsCurrentOwner(expected)) ClearOverview(); }
        catch (Exception) { if (profiles.IsCurrentOwner(expected)) AvatarStatus = "The cloud update is not confirmed. Please retry."; }
        finally { IsAvatarBusy = false; }
    }

    private void ApplyPhotoResult(ProfileAccountScope expected, AvatarUpdateResult saved)
    {
        profiles.RequireOwner(expected);
        AvatarUrl = saved.LocalPath; localPhoto = saved.LocalPath;
        CanSyncAvatar = !saved.CloudConfirmed; AvatarStatus = saved.Status;
        if (saved.CloudConfirmed) CurrentUser = profiles.ReadCached(expected.UserId) ?? CurrentUser;
    }
}
