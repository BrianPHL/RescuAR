using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RescuAR.App.Models;
using RescuAR.App.Services.Profile;

namespace RescuAR.App.ViewModels.Profile;

public partial class HealthInformationViewModel : ObservableObject
{
    private readonly UserProfileService profiles = UserProfileService.Instance;
    private ProfileAccountScope owner;
    private HealthProfileValues? original;
    private CancellationTokenSource? loading;
    [ObservableProperty] private User currentUser = new();
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSave))] private bool isSaving;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSave))] private bool isReady;
    [ObservableProperty] private string statusMessage = string.Empty;
    public bool CanSave => IsReady && !IsSaving;
    public ObservableCollection<string> BloodTypes { get; } = new(ProfileEditingPolicy.BloodTypes);

    public async Task LoadAsync()
    {
        StopLoading();
        var request = loading = new CancellationTokenSource();
        original = null; CurrentUser = new User();
        StatusMessage = "Loading your health profile…";
        try
        {
            owner = profiles.CaptureOwner();
            var result = await profiles.LoadForDisplayAsync(owner, request.Token);
            request.Token.ThrowIfCancellationRequested();
            profiles.RequireOwner(owner);
            original = HealthProfileValues.From(result.User);
            if (!string.IsNullOrWhiteSpace(original.BloodType) && !BloodTypes.Contains(original.BloodType)) BloodTypes.Add(original.BloodType);
            CurrentUser = result.User;
            IsReady = true;
            StatusMessage = result.Cached ? "Showing saved health information. Connect before saving changes." : string.Empty;
        }
        catch (OperationCanceledException) { if (ReferenceEquals(loading, request)) StatusMessage = "Reopen this screen to load the current account."; }
        catch (Exception error) { if (ReferenceEquals(loading, request)) StatusMessage = $"Could not load health information: {error.Message}"; }
    }

    [RelayCommand]
    private async Task SaveHealth()
    {
        if (!CanSave || original is null) return;
        IsSaving = true;
        var expected = owner;
        var request = loading;
        try
        {
            profiles.RequireOwner(expected);
            var fields = ProfileEditingPolicy.HealthChanges(original, HealthProfileValues.From(CurrentUser));
            if (fields.Length == 0) { StatusMessage = "No changes to save."; return; }
            var saved = await profiles.PatchOwnedAsync(expected, fields);
            if (!ReferenceEquals(request, loading) || request?.IsCancellationRequested == true) return;
            profiles.RequireOwner(expected);
            original = HealthProfileValues.From(saved);
            if (!string.IsNullOrWhiteSpace(original.BloodType) && !BloodTypes.Contains(original.BloodType)) BloodTypes.Add(original.BloodType);
            CurrentUser = saved;
            StatusMessage = "Health information saved to your account.";
        }
        catch (Exception error) { if (ReferenceEquals(request, loading)) StatusMessage = $"Changes were not confirmed: {error.Message}"; }
        finally { IsSaving = false; }
    }

    public void StopLoading() { loading?.Cancel(); loading?.Dispose(); loading = null; IsReady = false; }
}
