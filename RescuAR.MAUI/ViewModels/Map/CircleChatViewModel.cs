using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RescuAR.App.Models;
using RescuAR.App.Services.Cloud;
using RescuAR.App.Services.SafetyCircle;

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
    public string DeliveryText { get; set; } = string.Empty;
    public bool IsPending { get; set; }
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
    private readonly IDispatcherTimer? _chatTimer;
    private string _accountId = string.Empty;
    private int _generation;
    private bool _active;
    private bool _refreshing;
    public event Action<ChatMessageItem>? MessageAdded;
    [ObservableProperty] private string circleId = string.Empty;
    [ObservableProperty] private string circleName = "Select a circle";
    [ObservableProperty] private string groupAvatarUrl = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoGroupAvatar))]
    private bool hasGroupAvatar;
    public bool HasNoGroupAvatar => !HasGroupAvatar;
    [ObservableProperty] private string inviteCode = string.Empty;
    [ObservableProperty] private string memberCountText = string.Empty;
    [ObservableProperty] private string newMessageText = string.Empty;
    [ObservableProperty] private bool isUploading;
    [ObservableProperty] private string uploadStatusText = string.Empty;
    [ObservableProperty] private bool isCirclePickerOpen;
    [ObservableProperty] private string syncStatus = string.Empty;
    public ObservableCollection<SupabaseSafetyCircle> MyCircles { get; } = new();
    public ObservableCollection<ChatMessageItem> Messages { get; } = new();

    public CircleChatViewModel(SafetyCircleService service)
    {
        _safetyCircleService = service;
        _chatTimer = Application.Current?.Dispatcher?.CreateTimer();
        if (_chatTimer != null)
        {
            _chatTimer.Interval = TimeSpan.FromSeconds(3);
            _chatTimer.Tick += async (_, _) => await LoadMessagesAsync();
        }
    }

    partial void OnCircleIdChanged(string value)
    {
        _generation++;
        Messages.Clear();
        InviteCode = GroupAvatarUrl = MemberCountText = NewMessageText = string.Empty;
        HasGroupAvatar = false;
        IsCirclePickerOpen = false;
    }

    public async Task StartAsync()
    {
        StopTimer();
        _active = true;
        try
        {
            var account = _safetyCircleService.GetCurrentUserId();
            if (account != _accountId) { CircleId = string.Empty; Messages.Clear(); MyCircles.Clear(); }
            _accountId = account;
            var selected = await _safetyCircleService.Sync.GetSelectedCircleIdAsync();
            if (!string.IsNullOrWhiteSpace(selected)) CircleId = selected;
            await LoadCircleDetailsAndMessagesAsync();
            if (_active) _chatTimer?.Start();
        }
        catch (Exception ex) { ClearOnAccountChange(); SyncStatus = ex.Message; }
    }

    public async Task InitializeWithCircleAsync(string id, string name)
    {
        CircleId = id; CircleName = name;
        await StartAsync();
    }

    public void StopTimer() { _active = false; _generation++; _chatTimer?.Stop(); }

    private bool StillCurrent(int generation, string id) => _active && generation == _generation && id == CircleId
        && _accountId == RescuAR.Services.SupabaseService.Instance.Client?.Auth.CurrentUser?.Id;

    private void ClearOnAccountChange()
    {
        if (_accountId == RescuAR.Services.SupabaseService.Instance.Client?.Auth.CurrentUser?.Id) return;
        CircleId = string.Empty;
        MyCircles.Clear(); Messages.Clear(); StopTimer();
    }

    public async Task LoadCircleDetailsAndMessagesAsync()
    {
        int generation = _generation;
        string id = CircleId;
        try
        {
            var snapshot = await _safetyCircleService.Sync.GetCirclesAsync();
            if (!StillCurrent(generation, id)) return;
            MyCircles.Clear();
            foreach (var circle in snapshot.Items) MyCircles.Add(circle);
            if (string.IsNullOrWhiteSpace(id))
            {
                var selected = await _safetyCircleService.Sync.GetSelectedCircleIdAsync();
                if (!StillCurrent(generation, id)) return;
                CircleId = selected;
                id = selected; generation = _generation;
            }
            var activeCircle = MyCircles.FirstOrDefault(c => c.Id == id);
            if (activeCircle == null) { CircleId = string.Empty; SyncStatus = "Select a joined circle to chat."; return; }
            CircleName = activeCircle.Name;
            InviteCode = activeCircle.InviteCode;
            var photo = await _safetyCircleService.Sync.GetLocalPhotoAsync(id);
            if (!StillCurrent(generation, id)) return;
            GroupAvatarUrl = photo;
            HasGroupAvatar = !string.IsNullOrWhiteSpace(GroupAvatarUrl);
            var members = await _safetyCircleService.Sync.GetMembersAsync(id);
            if (!StillCurrent(generation, id)) return;
            MemberCountText = $"{members.Items.Count} members{(members.IsCached ? " · saved list" : "")}";
            SyncStatus = snapshot.IsCached ? "Saved circles · cloud unavailable" : "Circle refreshed from cloud";
            await LoadMessagesAsync();
        }
        catch (Exception ex) { ClearOnAccountChange(); if (StillCurrent(generation, id)) SyncStatus = ex.Message; }
    }

    public async Task LoadMessagesAsync() => await RefreshMessagesAsync(false);

    private async Task RefreshMessagesAsync(bool retry)
    {
        if (!_active || _refreshing || string.IsNullOrWhiteSpace(CircleId)) return;
        _refreshing = true;
        int generation = _generation;
        string id = CircleId;
        try
        {
            var snapshot = await _safetyCircleService.Sync.GetMessagesAsync(id, retry);
            if (!StillCurrent(generation, id)) return;
            UpdateMessages(snapshot.Items);
            int pending = snapshot.Items.Count(m => m.IsPending);
            SyncStatus = snapshot.IsCached ? "Saved messages · cloud unavailable" : "Messages refreshed from cloud";
            if (pending > 0) SyncStatus += $" · {pending} pending";
        }
        catch (Exception ex) { ClearOnAccountChange(); if (StillCurrent(generation, id)) SyncStatus = ex.Message; }
        finally
        {
            _refreshing = false;
            if (_active && generation != _generation) _ = LoadMessagesAsync();
        }
    }

    private void UpdateMessages(List<CircleMessageState> states)
    {
        var updated = states.Select(s => new ChatMessageItem
        {
            Id = s.Message.Id, UserId = s.Message.UserId, SenderName = s.Message.SenderName,
            SenderAvatarUrl = s.Message.SenderAvatarUrl, MessageText = s.Message.MessageText,
            MediaUrl = s.Message.MediaUrl, MediaType = s.Message.MediaType,
            IsMyMessage = s.Message.UserId == _accountId, CreatedAt = s.Message.CreatedAt,
            DeliveryText = s.DeliveryText, IsPending = s.IsPending
        }).ToList();
        string Signature(ChatMessageItem m) => Newtonsoft.Json.JsonConvert.SerializeObject(m);
        if (Messages.Select(Signature).SequenceEqual(updated.Select(Signature))) return;
        var lastId = Messages.LastOrDefault()?.Id;
        Messages.Clear();
        foreach (var item in updated) Messages.Add(item);
        if (Messages.LastOrDefault() is { } latest && latest.Id != lastId) MessageAdded?.Invoke(latest);
    }

    [RelayCommand] private void ToggleCirclePicker() => IsCirclePickerOpen = !IsCirclePickerOpen;
    [RelayCommand] private async Task RetryPendingAsync() => await RefreshMessagesAsync(true);
    [RelayCommand]
    private async Task SwitchCircleAsync(SupabaseSafetyCircle circle)
    {
        try
        {
            await _safetyCircleService.Sync.SelectCircleAsync(circle.Id);
            CircleId = circle.Id;
            CircleName = circle.Name;
            await LoadCircleDetailsAndMessagesAsync();
        }
        catch (Exception ex) { SyncStatus = ex.Message; }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (IsUploading || string.IsNullOrWhiteSpace(NewMessageText) || string.IsNullOrWhiteSpace(CircleId)) return;
        string id = CircleId;
        int generation = _generation;
        var text = NewMessageText.Trim();
        IsUploading = true; UploadStatusText = "Saving and sending update...";
        try
        {
            await _safetyCircleService.SendMessageAsync(id, text);
            if (StillCurrent(generation, id))
            {
                NewMessageText = string.Empty;
                await LoadMessagesAsync();
            }
        }
        catch (Exception ex) { if (StillCurrent(generation, id)) SyncStatus = $"Could not save message: {ex.Message}"; }
        finally { IsUploading = false; UploadStatusText = string.Empty; }
    }

    [RelayCommand]
    private async Task CapturePhotoAsync()
    {
        var requestedCircle = CircleId;
        var requestedGeneration = _generation;
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
            if (photo != null && StillCurrent(requestedGeneration, requestedCircle))
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
        var requestedCircle = CircleId;
        var requestedGeneration = _generation;
        try
        {
            var file = await MediaPicker.Default.PickPhotoAsync();
            if (file != null && StillCurrent(requestedGeneration, requestedCircle))
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
        var requestedCircle = CircleId;
        var requestedGeneration = _generation;
        try
        {
            var file = await MediaPicker.Default.PickVideoAsync();
            if (file != null && StillCurrent(requestedGeneration, requestedCircle))
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
        await Shell.Current.DisplayAlert("Circle photo", "Set a photo for this device in Safety Circle Management. Circle photos are not shared with other members.", "OK");
        await Shell.Current.GoToAsync("SafetyCircleSettingsPage");
    }

    [RelayCommand]
    private async Task CopyInviteCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(InviteCode)) return;
        await Clipboard.Default.SetTextAsync(InviteCode);
        await Shell.Current.DisplayAlert("Copied", "Invite code copied. Share it with the people you want in your circle.", "OK");
    }

    private async Task UploadAndSendMediaAsync(FileResult file, string mediaType)
    {
        if (IsUploading || string.IsNullOrWhiteSpace(CircleId)) return;
        var id = CircleId;
        int generation = _generation;
        var caption = NewMessageText.Trim();
        IsUploading = true; UploadStatusText = "Uploading media...";
        try
        {
            using var stream = await file.OpenReadAsync();
            var url = await CloudinaryService.UploadImageStreamAsync(stream, file.FileName);
            if (!StillCurrent(generation, id)) return;
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("Media upload failed. Select the file again to retry.");
            await _safetyCircleService.SendMessageAsync(id, caption, url, mediaType);
            if (StillCurrent(generation, id)) { NewMessageText = string.Empty; await LoadMessagesAsync(); }
        }
        catch (Exception ex) { if (StillCurrent(generation, id)) SyncStatus = $"Could not send media: {ex.Message}"; }
        finally { IsUploading = false; UploadStatusText = string.Empty; }
    }

    [RelayCommand]
    private async Task BackAsync() { StopTimer(); await Shell.Current.GoToAsync(".."); }
}
