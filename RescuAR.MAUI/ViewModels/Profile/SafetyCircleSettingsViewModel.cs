using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RescuAR.App.Services.SafetyCircle;

namespace RescuAR.App.ViewModels.Profile;

public sealed class CircleMembershipItem(CircleMembership circle, string userId, bool cached, bool selected)
{
    public CircleMembership Circle { get; } = circle;
    public string Id => Circle.Id;
    public string Name => Circle.Name;
    public string InviteCode => Circle.InviteCode;
    public bool IsCreator => Circle.CreatedBy == userId;
    public bool CanManage => IsCreator && !cached;
    public bool CanLeave => !IsCreator && !cached;
    public string RoleText => (selected ? "Selected · " : "") + (IsCreator ? "Creator" : "Member") + (cached ? " · cached" : "");
}

public partial class SafetyCircleSettingsViewModel : ObservableObject
{
    private readonly CircleMembershipService circles = CircleMembershipRuntime.Current;
    private CircleMembershipScope? scope;
    private long viewVersion;
    private bool deletePending;
    private string selectedCircleId = "";
    private CancellationTokenSource extras = new();
    public ObservableCollection<RescuAR.App.ViewModels.Map.CircleMember> Members { get; } = new();
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasLocalGroupPhoto))] private string localGroupPhotoPath = "";
    [ObservableProperty] private string extrasStatus = "";
    public bool HasLocalGroupPhoto => LocalGroupPhotoPath.Length > 0;
    public ObservableCollection<CircleMembershipItem> AllMyCircles { get; } = new();
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsNotInCircle))] private bool isInCircle;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsNotBusy), nameof(CanChange))] private bool isBusy;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanChange))] private bool isReady;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanChange))] private bool isCached;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanChange))] private bool needsRefresh;
    [ObservableProperty] private string activeCircleName = string.Empty;
    [ObservableProperty] private string inviteCode = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string newCircleName = string.Empty;
    [ObservableProperty] private string joinInviteCode = string.Empty;
    [ObservableProperty] private bool isDeleteModalVisible;
    [ObservableProperty] private CircleMembershipItem? circleToDelete;
    [ObservableProperty] private string removalTitle = string.Empty;
    [ObservableProperty] private string removalMessage = string.Empty;
    [ObservableProperty] private string removalAction = string.Empty;
    public bool IsNotInCircle => !IsInCircle;
    public bool IsNotBusy => !IsBusy;
    public bool CanChange => IsReady && !IsBusy && !IsCached && !NeedsRefresh;

    public async Task LoadCircleDataAsync()
    {
        var previous = scope;
        Reset(false);
        long request = viewVersion;
        IsBusy = true;
        StatusMessage = "Checking your Circle memberships…";
        try
        {
            scope = circles.Capture();
            if (previous.HasValue && previous != scope) NewCircleName = JoinInviteCode = string.Empty;
            var result = await circles.LoadAsync(scope.Value);
            if (request != viewVersion) return;
            circles.Require(scope.Value);
            Present(result);
            NeedsRefresh = false; IsReady = true;
            await LoadExtrasAsync(scope.Value, result.IsCached, request);
        }
        catch (Exception error) { if (request == viewVersion) StatusMessage = Error(error); }
        finally { if (request == viewVersion) IsBusy = false; }
    }
    [RelayCommand] private Task RefreshCircles() => LoadCircleDataAsync();
    private void Present(CircleMembershipSnapshot snapshot)
    {
        if (scope is not { } owner) return;
        circles.Require(owner);
        AllMyCircles.Clear();
        foreach (var circle in snapshot.Circles) AllMyCircles.Add(new(circle, owner.UserId, snapshot.IsCached, circle.Id == snapshot.SelectedId));
        var active = AllMyCircles.FirstOrDefault(circle => circle.Id == snapshot.SelectedId);
        selectedCircleId = active?.Id ?? ""; Members.Clear(); LocalGroupPhotoPath = ExtrasStatus = "";
        ActiveCircleName = active?.Name ?? string.Empty; InviteCode = active?.InviteCode ?? string.Empty;
        IsInCircle = AllMyCircles.Count > 0; IsCached = snapshot.IsCached; StatusMessage = snapshot.Notice;
    }
    private async Task<bool> Run(Func<CircleMembershipScope, Task<CircleMembershipSnapshot>> operation, bool mutation)
    {
        if (scope is not { } owner || IsBusy || !IsReady) return false;
        long request = viewVersion;
        IsBusy = true;
        try
        {
            circles.Require(owner);
            var result = await operation(owner);
            if (request != viewVersion) return false;
            circles.Require(owner); Present(result); NeedsRefresh = false;
            await LoadExtrasAsync(owner, result.IsCached, request);
            if (request != viewVersion) return false;
            circles.Require(owner);
            return true;
        }
        catch (Exception error)
        {
            if (request == viewVersion)
            {
                NeedsRefresh = mutation;
                if (error is OperationCanceledException) Stop();
                StatusMessage = Error(error);
            }
            return false;
        }
        finally { if (request == viewVersion) IsBusy = false; }
    }
    private static string Error(Exception error) => error is OperationCanceledException ? "Your account or server changed. Reopen Circle management." :
        error is ArgumentException or UnauthorizedAccessException or InvalidOperationException or CircleMembershipOfflineException
            ? error.Message + " Refresh before retrying a change."
            : "The Circle request could not be confirmed. Check your connection and access, then Refresh before retrying.";

    [RelayCommand]
    private async Task CreateCircle()
    {
        if (!CanChange) return;
        string name = NewCircleName ?? "";
        if (await Run(owner => circles.CreateAsync(owner, name), true)) NewCircleName = string.Empty;
    }
    [RelayCommand]
    private async Task JoinCircle()
    {
        if (!CanChange) return;
        string code = JoinInviteCode ?? "";
        if (await Run(owner => circles.JoinAsync(owner, code), true)) JoinInviteCode = string.Empty;
    }
    [RelayCommand]
    private async Task SelectActiveCircle(CircleMembershipItem item)
    {
        if (item is null) return;
        await Run(owner => circles.SelectAsync(owner, item.Id), false);
    }
    [RelayCommand]
    private async Task RenameCircle(CircleMembershipItem item)
    {
        if (!CanChange || item is null || !item.IsCreator || scope is not { } owner || Shell.Current is not { } page) return;
        long request = viewVersion;
        string? name = await page.DisplayPromptAsync("Rename Circle", "Choose a Circle name:", "Save", "Cancel", maxLength: 60, initialValue: item.Name);
        if (name is null || request != viewVersion || scope != owner) return;
        await Run(owner => circles.RenameAsync(owner, item.Id, name), true);
    }
    [RelayCommand] private void RequestDeleteCircle(CircleMembershipItem item) => RequestRemoval(item, true);
    [RelayCommand] private void RequestLeaveCircle(CircleMembershipItem item) => RequestRemoval(item, false);
    private void RequestRemoval(CircleMembershipItem item, bool delete)
    {
        if (!CanChange || item is null || item.IsCreator != delete) return;
        CircleToDelete = item; deletePending = delete;
        RemovalTitle = delete ? "Delete for everyone" : "Leave Circle";
        RemovalAction = delete ? "Delete for everyone" : "Leave";
        RemovalMessage = delete ? $"Delete ‘{item.Name}’ for every member? This action cannot be undone."
            : $"Leave ‘{item.Name}’? Only your membership is removed; other members keep their Circle.";
        IsDeleteModalVisible = true;
    }
    [RelayCommand] private void CancelDelete() { IsDeleteModalVisible = false; CircleToDelete = null; }
    [RelayCommand]
    private async Task ConfirmDelete()
    {
        if (!CanChange || CircleToDelete is not { } item) return;
        bool delete = deletePending; CancelDelete();
        await Run(owner => circles.RemoveAsync(owner, item.Id, delete), true);
    }
    [RelayCommand]
    private async Task CopyInviteCode()
    {
        if (scope is not { } owner || string.IsNullOrWhiteSpace(InviteCode)) return;
        long request = viewVersion;
        try { circles.Require(owner); await Clipboard.Default.SetTextAsync(InviteCode); circles.Require(owner); if (request == viewVersion) StatusMessage = "Invite code copied."; }
        catch { if (request == viewVersion) StatusMessage = "The invite code could not be copied. Reopen this Circle if your account changed."; }
    }
    public void Stop() => Reset(true);
    private void Reset(bool clearDrafts)
    {
        Interlocked.Increment(ref viewVersion); scope = null;
        extras.Cancel(); extras.Dispose(); extras = new();
        selectedCircleId = ""; Members.Clear(); LocalGroupPhotoPath = ExtrasStatus = "";
        IsBusy = false; IsReady = false; IsInCircle = false; IsCached = false; NeedsRefresh = false;
        AllMyCircles.Clear(); ActiveCircleName = InviteCode = StatusMessage = string.Empty; CancelDelete();
        if (clearDrafts) NewCircleName = JoinInviteCode = string.Empty;
    }
    private async Task LoadExtrasAsync(CircleMembershipScope owner, bool cached, long request)
    {
        string id = selectedCircleId;
        if (id.Length == 0) return;
        var token = extras.Token;
        try
        {
            var local = await CircleChatRuntime.Current.LoadLocalAsync(new(owner, id), token);
            if (request != viewVersion || selectedCircleId != id) return;
            circles.Require(owner);
            var photo = local.State.LocalPhoto;
            LocalGroupPhotoPath = photo is not null && CircleChatRuntime.Files.Owns(new(owner, id), photo) && File.Exists(photo.Path) ? photo.Path : "";
        }
        catch (OperationCanceledException) { return; }
        catch { if (request == viewVersion) ExtrasStatus = "The local Circle photo could not be read. Existing data was preserved."; }
        if (cached)
        {
            if (request == viewVersion) ExtrasStatus += " Member details are unavailable offline. Connect and Refresh.";
            return;
        }
        try
        {
            var roster = await new SupabaseCirclePresenceRemote(CircleMembershipRuntime.Context).ReadAsync(owner, id, token);
            if (request != viewVersion || selectedCircleId != id) return;
            circles.Require(owner); Members.Clear();
            foreach (var person in roster.People)
            {
                var position = roster.Positions.FirstOrDefault(position => position.UserId == person.UserId);
                Members.Add(RescuAR.App.ViewModels.Map.SafetyCircleViewModel.Row(CirclePresencePolicy.Project(person, position, DateTimeOffset.UtcNow), owner.UserId, false));
            }
            ExtrasStatus += " Positions and battery are last shared readings, not proof of safety or online status. " + roster.Notice;
        }
        catch (OperationCanceledException) { }
        catch { if (request == viewVersion) { Members.Clear(); ExtrasStatus += " Current member details could not be confirmed. Refresh your Circles."; } }
    }
    [RelayCommand]
    private async Task PickLocalCirclePhoto()
    {
        if (scope is not { } owner || selectedCircleId.Length == 0 || IsBusy || !IsReady) return;
        string id = selectedCircleId; long request = viewVersion; var token = extras.Token;
        IsBusy = true;
        try
        {
            circles.Require(owner);
            var file = await MediaPicker.Default.PickPhotoAsync();
            if (request != viewVersion || selectedCircleId != id) return;
            circles.Require(owner); if (file is null) return;
            var photo = await CircleChatRuntime.ImportAsync(new(owner, id), file, "LocalPhoto", token);
            var saved = await CircleChatRuntime.Current.SetLocalPhotoAsync(new(owner, id), photo, token);
            if (request != viewVersion || selectedCircleId != id) return;
            circles.Require(owner); LocalGroupPhotoPath = photo.Path; StatusMessage = saved.Notice;
        }
        catch (Exception error) { if (request == viewVersion) { if (error is OperationCanceledException) Stop(); StatusMessage = error is CircleChatInputException ? error.Message : "The device-only Circle photo could not be saved. No shared group photo was changed."; } }
        finally { if (request == viewVersion) IsBusy = false; }
    }

    [RelayCommand] private async Task Back() { if (!IsBusy && Shell.Current != null) await Shell.Current.GoToAsync(".."); }
}
