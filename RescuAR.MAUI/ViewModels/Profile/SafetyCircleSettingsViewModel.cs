using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RescuAR.App.Models;
using RescuAR.App.Services.Cloud;

namespace RescuAR.App.ViewModels.Profile;

public partial class SafetyCircleSettingsViewModel(SafetyCircleService service) : ObservableObject
{
    public ObservableCollection<SupabaseSafetyCircle> MyCircles { get; } = new();
    public ObservableCollection<User> Members { get; } = new();
    [ObservableProperty] private SupabaseSafetyCircle? selectedCircle;
    [ObservableProperty] private string syncStatus = string.Empty;
    [ObservableProperty] private string localPhoto = string.Empty;
    [ObservableProperty] private bool isBusy;
    public bool HasCircle => SelectedCircle != null;
    public bool IsOwner => SelectedCircle != null && SelectedCircle.CreatedBy ==
        RescuAR.Services.SupabaseService.Instance.Client?.Auth.CurrentUser?.Id;
    public bool CanLeave => HasCircle && !IsOwner;

    partial void OnSelectedCircleChanged(SupabaseSafetyCircle? value)
    {
        OnPropertyChanged(nameof(HasCircle));
        OnPropertyChanged(nameof(IsOwner));
        OnPropertyChanged(nameof(CanLeave));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await RunAsync(async () =>
        {
            var snapshot = await service.Sync.GetCirclesAsync();
            MyCircles.Clear();
            foreach (var circle in snapshot.Items) MyCircles.Add(circle);
            var selectedId = await service.Sync.GetSelectedCircleIdAsync();
            SelectedCircle = MyCircles.FirstOrDefault(c => c.Id == selectedId);
            SyncStatus = snapshot.IsCached ? "Saved circles · cloud unavailable. Changes need a connection." : "Circles refreshed from cloud";
            await LoadSelectedAsync();
        });
    }

    private async Task LoadSelectedAsync()
    {
        Members.Clear();
        LocalPhoto = string.Empty;
        if (SelectedCircle == null) return;
        var snapshot = await service.Sync.GetMembersAsync(SelectedCircle.Id);
        foreach (var member in snapshot.Items) Members.Add(member);
        LocalPhoto = await service.Sync.GetLocalPhotoAsync(SelectedCircle.Id);
        if (snapshot.IsCached) SyncStatus = "Saved member list · cloud unavailable";
    }

    [RelayCommand]
    private async Task SelectCircleAsync(SupabaseSafetyCircle circle) => await RunAsync(async () =>
    {
        await service.Sync.SelectCircleAsync(circle.Id);
        SelectedCircle = circle;
        await LoadSelectedAsync();
    });

    [RelayCommand]
    private async Task CreateCircleAsync()
    {
        var name = await Shell.Current.DisplayPromptAsync("Create Safety Circle", "Circle name (up to 60 characters):", "Create", "Cancel", maxLength: 60);
        if (string.IsNullOrWhiteSpace(name)) return;
        await RunAsync(async () =>
        {
            var circle = await service.CreateCircleAsync(name);
            await Shell.Current.DisplayAlert("Circle created", $"Invite code: {circle.InviteCode}", "OK");
        });
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task JoinCircleAsync()
    {
        var code = await Shell.Current.DisplayPromptAsync("Join Safety Circle", "Enter the 6-character invite code:", "Join", "Cancel", maxLength: 6);
        if (string.IsNullOrWhiteSpace(code)) return;
        await RunAsync(async () => { await service.JoinCircleWithCodeAsync(code); });
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RenameCircleAsync()
    {
        if (SelectedCircle == null || !IsOwner) return;
        var circleId = SelectedCircle.Id;
        var name = await Shell.Current.DisplayPromptAsync("Rename circle", "New circle name:", "Save", "Cancel", maxLength: 60, initialValue: SelectedCircle.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        await RunAsync(async () => { await service.Sync.RenameAsync(circleId, name); });
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RemoveCircleAsync()
    {
        if (SelectedCircle == null) return;
        var id = SelectedCircle.Id;
        bool delete = IsOwner;
        var pending = await service.Sync.GetMessagesAsync(id);
        var warning = pending.Items.Any(m => m.IsPending) ? " Unsent messages saved on this device will be removed." : string.Empty;
        if (!await Shell.Current.DisplayAlert(delete ? "Delete circle?" : "Leave circle?",
            (delete ? "This removes the circle for all members." : "You will lose access to this circle.") + warning,
            delete ? "Delete" : "Leave", "Cancel")) return;
        await RunAsync(async () => { await service.Sync.RemoveAsync(id, delete); });
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task CopyInviteCodeAsync()
    {
        if (SelectedCircle == null) return;
        await Clipboard.Default.SetTextAsync(SelectedCircle.InviteCode);
        await Shell.Current.DisplayAlert("Copied", "Share this code with the people you want in your circle.", "OK");
    }

    [RelayCommand]
    private async Task SetDevicePhotoAsync()
    {
        if (SelectedCircle == null) return;
        var id = SelectedCircle.Id;
        var user = service.GetCurrentUserId();
        var file = await MediaPicker.Default.PickPhotoAsync();
        if (file == null) return;
        await RunAsync(async () =>
        {
            using var source = await file.OpenReadAsync();
            var directory = Path.Combine(FileSystem.AppDataDirectory, "circle-photos", user);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + Path.GetExtension(file.FileName));
            using (var target = File.Create(path)) await source.CopyToAsync(target);
            if (service.GetCurrentUserId() != user) throw new UnauthorizedAccessException("Account changed. Reopen this page.");
            await service.Sync.SetLocalPhotoAsync(id, path);
            LocalPhoto = path;
            SyncStatus = "Circle photo saved on this device only";
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        string? account = RescuAR.Services.SupabaseService.Instance.Client?.Auth.CurrentUser?.Id;
        IsBusy = true;
        try
        {
            await action();
            if (account != RescuAR.Services.SupabaseService.Instance.Client?.Auth.CurrentUser?.Id)
                throw new UnauthorizedAccessException("Account changed. Reopen this page.");
        }
        catch (Exception ex)
        {
            if (account != RescuAR.Services.SupabaseService.Instance.Client?.Auth.CurrentUser?.Id)
            {
                MyCircles.Clear(); Members.Clear(); SelectedCircle = null; LocalPhoto = string.Empty;
            }
            SyncStatus = "Changes have not been confirmed";
            await Shell.Current.DisplayAlert("Could not confirm changes", ex.Message, "OK");
        }
        finally { IsBusy = false; }
    }
}
