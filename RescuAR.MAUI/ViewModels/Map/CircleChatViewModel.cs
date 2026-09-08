using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using RescuAR.App.Models;
using RescuAR.App.Services.Cloud;

namespace RescuAR.App.ViewModels.Map;

public class ChatMessageItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string SenderAvatarUrl { get; set; } = string.Empty;
    public string SenderInitials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SenderName)) return "?";
            var parts = SenderName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpper();
            return $"{parts[0][0]}{parts[parts.Length - 1][0]}".ToUpper();
        }
    }
    public string MessageText { get; set; } = string.Empty;
    public string MediaUrl { get; set; } = string.Empty;
    public string MediaType { get; set; } = "Text";
    public bool HasMedia => !string.IsNullOrWhiteSpace(MediaUrl);
    public bool IsImage => HasMedia && MediaType.Equals("Image", StringComparison.OrdinalIgnoreCase);
    public bool IsVideo => HasMedia && MediaType.Equals("Video", StringComparison.OrdinalIgnoreCase);
    public bool HasText => !string.IsNullOrWhiteSpace(MessageText);
    public bool IsMyMessage { get; set; } = true;
    public bool IsNotMyMessage => !IsMyMessage;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string FormattedTime => CreatedAt.ToLocalTime().ToString("h:mm tt");
}

[QueryProperty(nameof(CircleId), "circleId")]
[QueryProperty(nameof(CircleName), "circleName")]
public partial class CircleChatViewModel : ObservableObject
{
    private readonly SafetyCircleService _safetyCircleService;
    private IDispatcherTimer? _chatTimer;
    private string _currentUserId = string.Empty;

    public event Action<ChatMessageItem>? MessageAdded;

    [ObservableProperty]
    private string circleId = string.Empty;

    [ObservableProperty]
    private string circleName = "Family Circle";

    [ObservableProperty]
    private string groupAvatarUrl = string.Empty;

    [ObservableProperty]
    private bool hasGroupAvatar = false;

    public bool HasNoGroupAvatar => !HasGroupAvatar;

    [ObservableProperty]
    private string inviteCode = string.Empty;

    [ObservableProperty]
    private string memberCountText = "Family Updates";

    [ObservableProperty]
    private string newMessageText = string.Empty;

    [ObservableProperty]
    private bool isUploading = false;

    [ObservableProperty]
    private string uploadStatusText = string.Empty;

    public ObservableCollection<ChatMessageItem> Messages { get; } = new();

    public CircleChatViewModel(SafetyCircleService safetyCircleService)
    {
        _safetyCircleService = safetyCircleService;
        RefreshCurrentUserId();

        _chatTimer = Application.Current?.Dispatcher?.CreateTimer();
        if (_chatTimer != null)
        {
            _chatTimer.Interval = TimeSpan.FromSeconds(3);
            _chatTimer.Tick += async (s, e) => await RefreshMessagesSilentAsync();
        }
    }

    private void RefreshCurrentUserId()
    {
        try
        {
            _currentUserId = _safetyCircleService.GetCurrentUserId();
        }
        catch
        {
            _currentUserId = string.Empty;
        }
    }

    partial void OnCircleIdChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            RefreshCurrentUserId();
            _ = LoadCircleDetailsAndMessagesAsync();
            _chatTimer?.Start();
        }
    }

    public async Task InitializeWithCircleAsync(string id, string name)
    {
        CircleId = id;
        if (!string.IsNullOrWhiteSpace(name))
        {
            CircleName = name;
        }

        RefreshCurrentUserId();
        await LoadCircleDetailsAndMessagesAsync();
        _chatTimer?.Start();
    }

    public void StopTimer()
    {
        _chatTimer?.Stop();
    }

    public async Task LoadCircleDetailsAndMessagesAsync()
    {
        if (string.IsNullOrEmpty(CircleId)) return;

        try
        {
            // Load circle metadata (group avatar, invite code, members count)
            var circles = await _safetyCircleService.GetMyCirclesAsync();
            var circle = circles.FirstOrDefault(c => c.Id == CircleId);
            if (circle != null)
            {
                InviteCode = circle.InviteCode;
                CircleName = circle.Name;
            }

            var members = await _safetyCircleService.GetCircleMembersAsync(CircleId);
            if (members != null && members.Count > 0)
            {
                MemberCountText = $"{members.Count} {(members.Count == 1 ? "Member" : "Members")} Online";
            }

            // Load saved group photo from preferences if exists
            var savedGroupPhoto = Preferences.Default.Get($"circle_avatar_{CircleId}", string.Empty);
            if (!string.IsNullOrWhiteSpace(savedGroupPhoto))
            {
                GroupAvatarUrl = savedGroupPhoto;
                HasGroupAvatar = true;
            }
        }
        catch { }

        await LoadMessagesAsync();
    }

    public async Task LoadMessagesAsync()
    {
        if (string.IsNullOrEmpty(CircleId)) return;

        try
        {
            RefreshCurrentUserId();
            var rawMsgs = await _safetyCircleService.GetCircleMessagesAsync(CircleId);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                UpdateMessagesCollection(rawMsgs);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadMessages error: {ex.Message}");
        }
    }

    private async Task RefreshMessagesSilentAsync()
    {
        if (string.IsNullOrEmpty(CircleId)) return;

        try
        {
            var rawMsgs = await _safetyCircleService.GetCircleMessagesAsync(CircleId);
            if (rawMsgs.Count != Messages.Count)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    UpdateMessagesCollection(rawMsgs);
                });
            }
        }
        catch { }
    }

    private void UpdateMessagesCollection(System.Collections.Generic.List<SupabaseCircleMessage> rawMsgs)
    {
        RefreshCurrentUserId();
        Messages.Clear();
        foreach (var m in rawMsgs)
        {
            bool isMine = !string.IsNullOrEmpty(_currentUserId) ? (m.UserId == _currentUserId) : true;
            Messages.Add(new ChatMessageItem
            {
                Id = m.Id,
                UserId = m.UserId,
                SenderName = !string.IsNullOrWhiteSpace(m.SenderName) ? m.SenderName : "Family Member",
                SenderAvatarUrl = m.SenderAvatarUrl,
                MessageText = m.MessageText,
                MediaUrl = m.MediaUrl,
                MediaType = m.MediaType,
                IsMyMessage = isMine,
                CreatedAt = m.CreatedAt
            });
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(NewMessageText)) return;

        var textToSend = NewMessageText.Trim();
        NewMessageText = string.Empty;

        RefreshCurrentUserId();

        // Optimistic display immediately
        var localItem = new ChatMessageItem
        {
            Id = Guid.NewGuid().ToString(),
            UserId = _currentUserId,
            SenderName = "Me",
            MessageText = textToSend,
            MediaType = "Text",
            IsMyMessage = true,
            CreatedAt = DateTime.UtcNow
        };

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Messages.Add(localItem);
            MessageAdded?.Invoke(localItem);
        });

        // Send to cloud
        if (!string.IsNullOrEmpty(CircleId))
        {
            _ = Task.Run(async () =>
            {
                await _safetyCircleService.SendMessageAsync(CircleId, textToSend, null, "Text");
            });
        }
    }

    [RelayCommand]
    private async Task CapturePhotoAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.Camera>();
            }

            if (status != PermissionStatus.Granted)
            {
                if (Shell.Current != null)
                    await Shell.Current.DisplayAlert("Permission Denied", "Camera permission is required to capture status photos.", "OK");
                return;
            }

            if (!MediaPicker.Default.IsCaptureSupported)
            {
                if (Shell.Current != null)
                    await Shell.Current.DisplayAlert("Unavailable", "Camera capture is not supported on this device.", "OK");
                return;
            }

            var photo = await MediaPicker.Default.CapturePhotoAsync();
            if (photo != null)
            {
                await UploadAndSendMediaAsync(photo, "Image");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CapturePhoto error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task PickMediaAsync()
    {
        try
        {
            var file = await MediaPicker.Default.PickPhotoAsync();
            if (file != null)
            {
                await UploadAndSendMediaAsync(file, "Image");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PickMedia error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task PickVideoAsync()
    {
        try
        {
            var file = await MediaPicker.Default.PickVideoAsync();
            if (file != null)
            {
                await UploadAndSendMediaAsync(file, "Video");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PickVideo error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task PickGroupPhotoAsync()
    {
        try
        {
            var file = await MediaPicker.Default.PickPhotoAsync();
            if (file != null)
            {
                IsUploading = true;
                UploadStatusText = "Updating group photo...";

                using var stream = await file.OpenReadAsync();
                var uploadedUrl = await CloudinaryService.UploadImageStreamAsync(stream, file.FileName);

                if (!string.IsNullOrWhiteSpace(uploadedUrl))
                {
                    GroupAvatarUrl = uploadedUrl;
                    HasGroupAvatar = true;
                    Preferences.Default.Set($"circle_avatar_{CircleId}", uploadedUrl);

                    if (Shell.Current != null)
                        await Shell.Current.DisplayAlert("Group Photo Updated", "The Safety Circle group photo has been updated!", "OK");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PickGroupPhoto error: {ex.Message}");
        }
        finally
        {
            IsUploading = false;
            UploadStatusText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task CopyInviteCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(InviteCode)) return;

        await Clipboard.Default.SetTextAsync(InviteCode);
        if (Shell.Current != null)
        {
            await Shell.Current.DisplayAlert("Copied to Clipboard!", $"Invite Code: {InviteCode}\nShare this with family members so they can join and chat with you.", "OK");
        }
    }

    private async Task UploadAndSendMediaAsync(FileResult file, string mediaType)
    {
        IsUploading = true;
        UploadStatusText = $"Uploading {mediaType.ToLower()}...";

        try
        {
            using var stream = await file.OpenReadAsync();
            var uploadedUrl = await CloudinaryService.UploadImageStreamAsync(stream, file.FileName);

            if (!string.IsNullOrWhiteSpace(uploadedUrl))
            {
                var caption = !string.IsNullOrWhiteSpace(NewMessageText) ? NewMessageText.Trim() : string.Empty;
                NewMessageText = string.Empty;

                RefreshCurrentUserId();

                var localItem = new ChatMessageItem
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = _currentUserId,
                    SenderName = "Me",
                    MessageText = caption,
                    MediaUrl = uploadedUrl,
                    MediaType = mediaType,
                    IsMyMessage = true,
                    CreatedAt = DateTime.UtcNow
                };

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Messages.Add(localItem);
                    MessageAdded?.Invoke(localItem);
                });

                if (!string.IsNullOrEmpty(CircleId))
                {
                    _ = Task.Run(async () =>
                    {
                        await _safetyCircleService.SendMessageAsync(CircleId, caption, uploadedUrl, mediaType);
                    });
                }
            }
            else
            {
                if (Shell.Current != null)
                    await Shell.Current.DisplayAlert("Upload Failed", "Could not upload the media. Please try again.", "OK");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UploadAndSendMedia error: {ex.Message}");
        }
        finally
        {
            IsUploading = false;
            UploadStatusText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        StopTimer();
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("..");
        }
    }
}
