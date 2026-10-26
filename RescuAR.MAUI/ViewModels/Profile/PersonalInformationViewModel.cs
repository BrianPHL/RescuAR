using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RescuAR.App.Services.Authentication;
using RescuAR.App.Services.Profile;

namespace RescuAR.App.ViewModels.Profile;

public partial class PersonalInformationViewModel : ObservableObject
{
    private readonly UserProfileService profiles = UserProfileService.Instance;
    private ProfileAccountScope owner;
    private PersonalProfileValues? original;
    private CancellationTokenSource? loading;
    [ObservableProperty] private string username = string.Empty;
    [ObservableProperty] private string firstName = string.Empty;
    [ObservableProperty] private string middleName = string.Empty;
    [ObservableProperty] private string lastName = string.Empty;
    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private string phoneNumber = string.Empty;
    [ObservableProperty] private string streetAddress = string.Empty;
    [ObservableProperty] private string selectedBarangay = string.Empty;
    [ObservableProperty] private string city = "Marikina City";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSave))] private bool isSaving;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSave))] private bool isReady;
    [ObservableProperty] private string statusMessage = string.Empty;
    public bool CanSave => IsReady && !IsSaving;
    public ObservableCollection<string> MarikinaBarangays { get; } = new(AuthenticationFormPolicy.MarikinaBarangays);

    public async Task LoadAsync()
    {
        StopLoading();
        var request = loading = new CancellationTokenSource();
        IsReady = false;
        original = null;
        Username = FirstName = MiddleName = LastName = Email = PhoneNumber = StreetAddress = SelectedBarangay = string.Empty;
        StatusMessage = "Loading your profile…";
        try
        {
            owner = profiles.CaptureOwner();
            var result = await profiles.LoadForDisplayAsync(owner, request.Token);
            request.Token.ThrowIfCancellationRequested();
            profiles.RequireOwner(owner);
            original = PersonalProfileValues.From(result.User);
            Username = original.Username; FirstName = original.FirstName; MiddleName = original.MiddleName;
            LastName = original.LastName; Email = result.User.Email; PhoneNumber = original.PhoneNumber;
            var address = ProfileEditingPolicy.ParseAddress(original.Address);
            if (!string.IsNullOrWhiteSpace(address.Barangay) && !MarikinaBarangays.Contains(address.Barangay))
                MarikinaBarangays.Add(address.Barangay);
            StreetAddress = address.Street; SelectedBarangay = address.Barangay; City = address.City;
            IsReady = true;
            StatusMessage = result.Cached ? "Showing your saved profile. Connect before saving changes." : string.Empty;
        }
        catch (OperationCanceledException) { if (ReferenceEquals(loading, request)) StatusMessage = "Reopen this screen to load the current account."; }
        catch (Exception error) { if (ReferenceEquals(loading, request)) StatusMessage = $"Could not load your profile: {error.Message}"; }
    }

    [RelayCommand]
    private async Task SaveInformation()
    {
        if (!CanSave || original is null) return;
        IsSaving = true;
        var expected = owner;
        var request = loading;
        try
        {
            profiles.RequireOwner(expected);
            var fields = ProfileEditingPolicy.PersonalChanges(original,
                new(Username, FirstName, MiddleName, LastName, PhoneNumber, StreetAddress, SelectedBarangay));
            if (fields.Length == 0) { StatusMessage = "No changes to save."; return; }
            var saved = await profiles.PatchOwnedAsync(expected, fields);
            if (!ReferenceEquals(request, loading) || request?.IsCancellationRequested == true) return;
            profiles.RequireOwner(expected);
            original = PersonalProfileValues.From(saved);
            Username = original.Username; FirstName = original.FirstName; MiddleName = original.MiddleName;
            LastName = original.LastName; PhoneNumber = original.PhoneNumber; Email = saved.Email;
            var address = ProfileEditingPolicy.ParseAddress(original.Address);
            if (!string.IsNullOrWhiteSpace(address.Barangay) && !MarikinaBarangays.Contains(address.Barangay))
                MarikinaBarangays.Add(address.Barangay);
            StreetAddress = address.Street; SelectedBarangay = address.Barangay; City = address.City;
            StatusMessage = "Personal information saved to your account.";
        }
        catch (Exception error) { if (ReferenceEquals(request, loading)) StatusMessage = $"Changes were not confirmed: {error.Message}"; }
        finally { IsSaving = false; }
    }

    public void StopLoading() { loading?.Cancel(); loading?.Dispose(); loading = null; IsReady = false; }
    [RelayCommand] private async Task Back() { if (!IsSaving && Shell.Current != null) await Shell.Current.GoToAsync(".."); }
}
