using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RescuAR.App.Services.SafetyCircle;

namespace RescuAR.App.ViewModels.Map;

public sealed class ChatMessageItem(CircleChatMessage message, CircleChatScope scope)
{
    public CircleChatMessage Message { get; } = message;
    public string Id => Message.Id;
    public string SenderName => Message.SenderName;
    public string SenderAvatarUrl => CirclePresencePolicy.SafeAvatar(Message.AvatarUrl);
    public string SenderInitials => string.Concat(SenderName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => char.ToUpperInvariant(word[0])));
    public string MessageText => Message.Text;
    public string MediaUrl => Message.LocalMedia is { } file && CircleChatRuntime.Files.Owns(scope, file) && File.Exists(file.Path) ? file.Path : Message.MediaUrl;
    public bool HasMedia => MediaUrl.Length > 0;
    public bool IsImage => HasMedia && Message.MediaType == "Image";
    public bool IsVideo => HasMedia && Message.MediaType == "Video";
    public bool HasText => MessageText.Length > 0;
    public bool IsMyMessage => Message.UserId == scope.Account.UserId;
    public bool IsNotMyMessage => !IsMyMessage;
    public bool CanRetry => IsMyMessage && Message.Delivery != ChatDelivery.Sent;
    public string DeliveryText => Message.Delivery switch { ChatDelivery.Sent => "Sent to server", ChatDelivery.Failed => "Failed · Retry", _ => "Pending · Retry when online" };
    public string FormattedTime => Message.CreatedAt.ToLocalTime().ToString("MMM d, HH:mm");
}

public partial class CircleChatViewModel : ObservableObject, IQueryAttributable
{
    private readonly CircleChatService chat = CircleChatRuntime.Current;
    private readonly CircleMembershipService memberships = CircleMembershipRuntime.Current;
    private readonly IDispatcherTimer timer;
    private readonly Dictionary<CircleChatScope, string> composerDrafts = new();
    private CircleChatScope? scope;
    private CancellationTokenSource? lifetime;
    private CancellationTokenSource? network;
    private ChatFile? attachment;
    private string requestedId = "";
    private long version;
    private int externalInteraction;
    public ObservableCollection<ChatMessageItem> Messages { get; } = new();
    public ObservableCollection<CircleMembership> MyCircles { get; } = new();
    public event Action<ChatMessageItem>? MessageAdded;
    [ObservableProperty] private string circleName = "Circle chat";
    [ObservableProperty] private string inviteCode = "";
    [ObservableProperty] private string memberCountText = "";
    [ObservableProperty] private string groupAvatarUrl = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasNoGroupAvatar), nameof(GroupPhotoStatus))] private bool hasGroupAvatar;
    [ObservableProperty] private string newMessageText = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsNotBusy), nameof(CanSend))] private bool isBusy;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSend))] private bool isReady;
    [ObservableProperty] private bool isCirclePickerOpen;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private bool hasAttachment;
    [ObservableProperty] private bool isAttachmentImage;
    [ObservableProperty] private bool isAttachmentVideo;
    [ObservableProperty] private string attachmentPath = "";
    [ObservableProperty] private string attachmentDetails = "";
    public bool HasNoGroupAvatar => !HasGroupAvatar;
    public string GroupPhotoStatus => HasGroupAvatar ? "Photo on this device" : "No local photo";
    public bool IsNotBusy => !IsBusy;
    public bool CanSend => IsReady && !IsBusy;
    public bool HasExternalInteraction => externalInteraction > 0;

    public CircleChatViewModel()
    {
        timer = Application.Current?.Dispatcher?.CreateTimer() ?? throw new InvalidOperationException("A page dispatcher is required.");
        timer.Interval = TimeSpan.FromSeconds(5);
        timer.Tick += async (_, _) => await RefreshAsync();
    }
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("circleId", out var value)) requestedId = value?.ToString() ?? "";
    }
    private void Require(CircleChatScope owner, long request)
    {
        if (lifetime is null || lifetime.IsCancellationRequested || request != version || scope != owner) throw new OperationCanceledException();
        chat.Require(owner);
    }
    public async Task StartAsync()
    {
        Stop(); long request = version;
        try
        {
            var account = memberships.Capture();
            lifetime = new(); network = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            IsBusy = true; StatusMessage = "Checking Circle membership…";
            var circles = await memberships.LoadAsync(account).WaitAsync(network.Token);
            if (request != version) return;
            memberships.Require(account);
            string id = requestedId.Length > 0 ? requestedId : circles.SelectedId;
            var selected = circles.Circles.FirstOrDefault(circle => circle.Id == id) ?? throw new UnauthorizedAccessException("This Circle is unavailable to your account.");
            scope = chat.Capture(id); requestedId = id;
            foreach (var circle in circles.Circles) MyCircles.Add(circle);
            CircleName = selected.Name; InviteCode = selected.InviteCode;
            MemberCountText = "Member count unavailable";
            NewMessageText = composerDrafts.GetValueOrDefault(scope.Value, "");
            var snapshot = await chat.LoadAsync(scope.Value, network.Token);
            Require(scope.Value, request); Present(snapshot); IsReady = true;
            timer.Start();
        }
        catch (Exception error) { if (request == version) Failure(error); }
        finally { if (request == version) IsBusy = false; }
    }
    public void Suspend() { timer.Stop(); network?.Cancel(); }
    public async Task ResumeAsync()
    {
        if (lifetime is null || lifetime.IsCancellationRequested || HasExternalInteraction) return;
        network?.Dispose(); network = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timer.Start(); await RefreshAsync();
    }
    public void Stop()
    {
        if (scope is { } old) composerDrafts[old] = NewMessageText;
        Interlocked.Increment(ref version); timer.Stop(); network?.Cancel(); network?.Dispose(); network = null;
        lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; scope = null;
        Messages.Clear(); MyCircles.Clear(); NewMessageText = ""; GroupAvatarUrl = ""; HasGroupAvatar = false;
        attachment = null; HasAttachment = IsAttachmentImage = IsAttachmentVideo = false; AttachmentPath = AttachmentDetails = "";
        IsReady = false; IsBusy = false; IsCirclePickerOpen = false; StatusMessage = "";
    }
    private void Present(CircleChatSnapshot snapshot)
    {
        if (scope is not { } owner) return;
        chat.Require(owner);
        bool newTail = ChatMessageReconciler.Apply(Messages, snapshot.State.Messages, row => row.Id, message => message.Id,
            (row, message) => row.Message == message, message => new ChatMessageItem(message, owner));
        MemberCountText = snapshot.State.MemberCount is > 0 and var count
            ? $"{count} {(count == 1 ? "member" : "members")}{(snapshot.MemberCountConfirmed ? "" : " · cached")}"
            : "Member count unavailable";
        attachment = snapshot.State.Attachment; HasAttachment = attachment is not null;
        AttachmentPath = attachment?.Path ?? ""; IsAttachmentImage = attachment?.Kind == "Image"; IsAttachmentVideo = attachment?.Kind == "Video";
        AttachmentDetails = attachment is null ? "" : $"{attachment.Name} · {attachment.Kind} · {attachment.Bytes / 1024.0:F1} KB · not uploaded";
        var photo = snapshot.State.LocalPhoto;
        HasGroupAvatar = photo is not null && CircleChatRuntime.Files.Owns(owner, photo) && File.Exists(photo.Path);
        GroupAvatarUrl = HasGroupAvatar ? photo!.Path : "";
        StatusMessage = snapshot.Notice;
        if (newTail && Messages.LastOrDefault() is { } last) MessageAdded?.Invoke(last);
    }
    private void Failure(Exception error)
    {
        if (error is UnauthorizedAccessException or InvalidDataException)
        { Messages.Clear(); IsReady = false; StatusMessage = "Circle access or message data could not be confirmed. Reopen after checking membership."; }
        else if (error is OperationCanceledException)
        {
            bool current = scope is { } owner;
            if (current) { try { chat.Require(scope!.Value); } catch { current = false; } }
            if (!current) { Stop(); StatusMessage = "Your account or Circle changed. Reopen chat."; }
            else StatusMessage = "Request paused. Saved pending messages remain available for explicit Retry.";
        }
        else if (error is CircleChatInputException) StatusMessage = error.Message;
        else StatusMessage = "Chat or local storage could not complete the request. Drafts and unconfirmed messages have not been marked Sent.";
    }
    private async Task Run(Func<CircleChatScope, CancellationToken, Task<CircleChatSnapshot>> operation)
    {
        if (scope is not { } owner || network is null || IsBusy || !IsReady) return;
        long request = version; var token = network.Token; IsBusy = true;
        try { Require(owner, request); var result = await operation(owner, token); Require(owner, request); Present(result); }
        catch (Exception error) { if (request == version) Failure(error); }
        finally { if (request == version) IsBusy = false; }
    }
    private Task RefreshAsync() => Run((owner, token) => chat.LoadAsync(owner, token));
    [RelayCommand] private Task RefreshMessages() => RefreshAsync();
    [RelayCommand] private Task SendMessage()
    {
        if (!CanSend || scope is not { } owner || (string.IsNullOrWhiteSpace(NewMessageText) && attachment is null)) return Task.CompletedTask;
        string text = NewMessageText; string? path = attachment?.Path; long request = version;
        return Run((captured, token) =>
        {
            var sender = CircleChatRuntime.Sender(captured);
            return chat.SendAsync(captured, text, path, sender.Name, sender.Avatar, pending =>
            { if (request == version && scope == captured) { Present(pending); NewMessageText = ""; } }, token);
        });
    }
    [RelayCommand] private Task RetryMessage(ChatMessageItem message) => message is not null && message.CanRetry && Messages.Contains(message)
        ? Run((owner, token) => chat.RetryAsync(owner, message.Id, token)) : Task.CompletedTask;
    [RelayCommand] private void QuickChat(string text) { if (CanSend && !string.IsNullOrWhiteSpace(text)) NewMessageText = text; }
    [RelayCommand] private void ToggleCirclePicker() => IsCirclePickerOpen = !IsCirclePickerOpen;
    [RelayCommand] private async Task SwitchCircle(CircleMembership circle)
    {
        if (!CanSend || scope is not { } owner || lifetime is null || circle is null) return;
        long request = version; IsBusy = true;
        try
        {
            await memberships.SelectAsync(owner.Account, circle.Id).WaitAsync(lifetime.Token); Require(owner, request);
            requestedId = circle.Id; await StartAsync();
        }
        catch (Exception error) { if (request == version) { Failure(error); IsBusy = false; } }
    }
    [RelayCommand] private Task CancelAttachment() => Run((owner, token) => chat.SetAttachmentAsync(owner, null, token));
    [RelayCommand] private Task PickGroupPhoto() => PickAsync("LocalPhoto");
    [RelayCommand] private async Task PickMedia()
    {
        if (!CanSend || scope is not { } owner || Shell.Current is null) return;
        long request = version;
        try
        {
            string? choice = await Shell.Current.DisplayActionSheet("Choose attachment (photo 10 MB / video 50 MB)", "Cancel", null, "Photo", "Video", "Camera");
            Require(owner, request);
            if (choice is "Photo" or "Video" or "Camera") await PickAsync(choice);
        }
        catch (Exception error) { if (request == version) Failure(error); }
    }
    private async Task PickAsync(string choice)
    {
        if (!CanSend || scope is not { } owner || lifetime is null) return;
        long request = version; var token = lifetime.Token; IsBusy = true; externalInteraction++;
        try
        {
            Require(owner, request);
            if (choice == "Camera" && await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted)
            { StatusMessage = "Camera permission was not granted. Nothing was selected or uploaded."; return; }
            FileResult? file = choice == "Video" ? await MediaPicker.Default.PickVideoAsync()
                : choice == "Camera" ? await MediaPicker.Default.CapturePhotoAsync() : await MediaPicker.Default.PickPhotoAsync();
            Require(owner, request); if (file is null) return;
            string kind = choice == "LocalPhoto" ? "LocalPhoto" : choice == "Video" ? "Video" : "Image";
            var local = await CircleChatRuntime.ImportAsync(owner, file, kind, token); Require(owner, request);
            var result = kind == "LocalPhoto" ? await chat.SetLocalPhotoAsync(owner, local, token) : await chat.SetAttachmentAsync(owner, local, token);
            Require(owner, request); Present(result);
        }
        catch (Exception error) { if (request == version) Failure(error); }
        finally { externalInteraction--; if (request == version) { IsBusy = false; await ResumeAsync(); } }
    }
    [RelayCommand] private async Task PreviewAttachment()
    {
        if (scope is not { } owner || attachment is not { } file || !CircleChatRuntime.Files.Owns(owner, file)) return;
        long request = version;
        try { chat.Require(owner); externalInteraction++; await Launcher.Default.OpenAsync(new OpenFileRequest("Preview attachment", new ReadOnlyFile(file.Path))); }
        catch { if (request == version) StatusMessage = "This attachment could not be previewed by the device. Nothing was uploaded."; }
        finally { externalInteraction--; if (request == version) await ResumeAsync(); }
    }
    [RelayCommand] private async Task OpenMedia(ChatMessageItem message)
    {
        if (scope is not { } owner || message is null || !Messages.Contains(message) || !message.HasMedia) return;
        long request = version;
        try
        {
            chat.Require(owner); externalInteraction++;
            if (message.Message.LocalMedia is { } file && CircleChatRuntime.Files.Owns(owner, file) && File.Exists(file.Path))
                await Launcher.Default.OpenAsync(new OpenFileRequest("Open attachment", new ReadOnlyFile(file.Path)));
            else if (CirclePresencePolicy.SafeAvatar(message.MediaUrl).Length > 0) await Launcher.Default.OpenAsync(new Uri(message.MediaUrl));
        }
        catch { if (request == version) StatusMessage = "The device could not open this media."; }
        finally { externalInteraction--; if (request == version) await ResumeAsync(); }
    }
    [RelayCommand] private async Task CopyInviteCode()
    {
        if (scope is not { } owner || InviteCode.Length == 0) return;
        long request = version;
        try { Require(owner, request); await Clipboard.Default.SetTextAsync(InviteCode); Require(owner, request); StatusMessage = "Invite code copied."; }
        catch { if (request == version) StatusMessage = "The invite code could not be copied."; }
    }
    [RelayCommand] private async Task Back() { if (Shell.Current is not null) await Shell.Current.GoToAsync(".."); }
}
