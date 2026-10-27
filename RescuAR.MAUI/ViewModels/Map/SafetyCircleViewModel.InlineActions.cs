using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RescuAR.App.Services.SafetyCircle;

namespace RescuAR.App.ViewModels.Map;

public partial class SafetyCircleViewModel
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsCircleActionOpen))] private bool isCreateCircleOpen;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsCircleActionOpen))] private bool isJoinCircleOpen;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsCircleActionOpen))] private bool isCircleInviteOpen;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEditCircleAction), nameof(CanOpenCircleAction), nameof(CanShowInlineInvite))] private bool isCircleActionBusy;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanShowInlineInvite))] private bool isInlineInviteAvailable;
    [ObservableProperty] private string newCircleName = "";
    [ObservableProperty] private string joinInviteCode = "";
    [ObservableProperty] private string circleDialogTitle = "";
    [ObservableProperty] private string circleActionStatus = "";
    [ObservableProperty] private string circleActionName = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasCircleInviteCode), nameof(CircleInviteDisplay))] private string circleInviteCode = "";
    private bool inlineMembershipsCached = true;
    public bool IsCircleActionOpen => IsCreateCircleOpen || IsJoinCircleOpen || IsCircleInviteOpen;
    public bool CanOpenCircleAction => owner.HasValue && !IsBusy && !IsCircleActionBusy;
    public bool CanShowInlineInvite => CanOpenCircleAction && IsInlineInviteAvailable;
    public bool CanEditCircleAction => !IsCircleActionBusy;
    public bool HasCircleInviteCode => !string.IsNullOrWhiteSpace(CircleInviteCode);
    public string CircleInviteDisplay => HasCircleInviteCode ? CircleInviteCode : "Unavailable";

    private void OpenInlineCircleAction(bool create)
    {
        if (!CanOpenCircleAction || owner is not { } scope) return;
        try
        {
            Require(scope, version); ResetCircleActionPanels(); CircleActionStatus = "";
            CircleDialogTitle = create ? "Create a Safety Circle" : "Join a Safety Circle";
            IsCircleDropdownOpen = false; IsCreateCircleOpen = create; IsJoinCircleOpen = !create;
        }
        catch { StatusMessage = "Your account changed. Reopen the Map tab."; }
    }

    [RelayCommand] private void ShowInlineInvite()
    {
        if (!CanOpenCircleAction || owner is not { } scope) return;
        try
        {
            Require(scope, version);
            var circle = MyCircles.FirstOrDefault(circle => circle.Id == selectedId);
            if (circle is null) { StatusMessage = "Select a confirmed or saved Circle first."; return; }
            ResetCircleActionPanels(); CircleDialogTitle = "Circle invite code";
            CircleActionName = circle.Name; CircleInviteCode = circle.InviteCode;
            CircleActionStatus = inlineMembershipsCached ? "Saved invite code · current membership is unverified offline." : "Share this code with people you want to invite.";
            IsCircleDropdownOpen = false; IsCircleInviteOpen = true;
        }
        catch { StatusMessage = "Your account changed. Reopen the Map tab."; }
    }

    [RelayCommand] private Task SubmitInlineCreate() => ChangeInlineMembership(true);
    [RelayCommand] private Task SubmitInlineJoin() => ChangeInlineMembership(false);
    private async Task ChangeInlineMembership(bool create)
    {
        if (!CanOpenCircleAction || owner is not { } scope) return;
        long request = version;
        IsCircleActionBusy = IsBusy = true; ResetSharing();
        CircleActionStatus = create ? "Creating your Circle…" : "Checking the invite code…";
        try
        {
            Require(scope, request);
            var snapshot = create ? await memberships.CreateAsync(scope, NewCircleName) : await memberships.JoinAsync(scope, JoinInviteCode);
            Require(scope, request);
            var circle = snapshot.Circles.FirstOrDefault(circle => circle.Id == snapshot.SelectedId);
            if (snapshot.IsCached || circle is null) throw new InvalidOperationException("Circle membership could not be confirmed.");
            MyCircles.Clear(); foreach (var item in snapshot.Circles) MyCircles.Add(item);
            selectedId = circle.Id; SelectedCircleName = circle.Name; CircleMembers.Clear(); UpdateMapMarkers();
            inlineMembershipsCached = false; IsInlineInviteAvailable = true;
            NewCircleName = JoinInviteCode = "";
            ResetCircleActionPanels(); CircleDialogTitle = create ? "Circle created" : "Circle joined";
            CircleActionName = circle.Name; CircleInviteCode = circle.InviteCode; CircleActionStatus = snapshot.Notice;
            IsCircleInviteOpen = true;
        }
        catch (Exception error)
        {
            if (request == version) CircleActionStatus = error is OperationCanceledException ? "Your account or Circle changed. Reopen this screen."
                : error is ArgumentException or CircleMembershipOfflineException or UnauthorizedAccessException or InvalidOperationException ? error.Message
                : "The Circle request could not be confirmed. Check your connection and refresh memberships before trying again.";
        }
        finally
        {
            if (request == version)
            {
                IsCircleActionBusy = IsBusy = false;
                if (page is not null && !IsCreateCircleOpen && !IsJoinCircleOpen) { coordinator.RefreshMemberships(); timer.Start(); _ = PollAsync(); }
            }
        }
    }

    [RelayCommand] private async Task CopyInlineInvite()
    {
        if (owner is not { } scope || !HasCircleInviteCode || IsCircleActionBusy) return;
        long request = version;
        try
        {
            Require(scope, request); await Clipboard.Default.SetTextAsync(CircleInviteCode); Require(scope, request);
            CircleActionStatus = "Invite code copied.";
        }
        catch { if (request == version) CircleActionStatus = "The invite code could not be copied. Reopen this Circle and try again."; }
    }
    [RelayCommand] private void CloseInlineCircleAction()
    {
        if (!CanEditCircleAction) return;
        ResetCircleActionPanels();
        if (page is not null) { timer.Start(); _ = PollAsync(); _ = RefreshLocalLocationAsync(); }
    }
    private void ResetCircleActionPanels() => IsCreateCircleOpen = IsJoinCircleOpen = IsCircleInviteOpen = false;
    private void ResetInlineCircleActions()
    {
        ResetCircleActionPanels(); IsCircleActionBusy = IsInlineInviteAvailable = false; inlineMembershipsCached = true;
        NewCircleName = JoinInviteCode = CircleInviteCode = CircleActionName = CircleActionStatus = CircleDialogTitle = "";
    }
}
