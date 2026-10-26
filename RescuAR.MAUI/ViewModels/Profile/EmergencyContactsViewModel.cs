using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel.Communication;
using RescuAR.App.Services.Profile;

namespace RescuAR.App.ViewModels.Profile;

public partial class EmergencyContactsViewModel : ObservableObject
{
    private readonly UserProfileService profiles = UserProfileService.Instance;
    private readonly MauiContactSaveSource source = new();
    private ProfileAccountScope owner;
    private ContactValues? original;
    private ContactForm? confirmedLocalDraft;
    private CancellationTokenSource? loading;
    [ObservableProperty] private string contact1Name = string.Empty;
    [ObservableProperty] private string contact1Phone = string.Empty;
    [ObservableProperty] private string contact1Relationship = string.Empty;
    [ObservableProperty] private string contact2Name = string.Empty;
    [ObservableProperty] private string contact2Phone = string.Empty;
    [ObservableProperty] private string contact2Relationship = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEdit))] private bool isReady;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEdit))] private bool isSaving;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEdit))] private bool isPicking;
    [ObservableProperty] private bool canRetryLocal;
    public bool CanEdit => IsReady && !IsSaving && !IsPicking;
    private ContactForm Draft() => new(new(Contact1Name ?? "", Contact1Phone ?? "", Contact2Name ?? "", Contact2Phone ?? ""), Contact1Relationship ?? "", Contact2Relationship ?? "");

    public async Task LoadAsync()
    {
        if (IsPicking) return;
        StopLoading();
        var request = loading = new CancellationTokenSource();
        original = null; CanRetryLocal = false;
        Contact1Name = Contact1Phone = Contact1Relationship = Contact2Name = Contact2Phone = Contact2Relationship = string.Empty;
        StatusMessage = "Loading your saved contacts…";
        try
        {
            owner = profiles.CaptureOwner();
            var result = await profiles.LoadForDisplayAsync(owner, request.Token);
            request.Token.ThrowIfCancellationRequested(); profiles.RequireOwner(owner);
            Present(ContactValues.From(result.User));
            IsReady = true;
            StatusMessage = result.Cached ? "Showing saved contacts. Connect before saving account changes." : "Import fills this form. Tap Save to confirm account changes. Relationships stay on this device.";
        }
        catch (Exception error) { if (ReferenceEquals(request, loading)) StatusMessage = $"Contacts could not be loaded: {error.Message}"; }
    }

    private void Present(ContactValues values, bool readLabels = true)
    {
        original = values;
        Contact1Name = values.Name1; Contact1Phone = values.Phone1; Contact2Name = values.Name2; Contact2Phone = values.Phone2;
        if (!readLabels) return;
        var labels = source.ReadLabels(owner, values);
        Contact1Relationship = labels.First; Contact2Relationship = labels.Second;
    }

    [RelayCommand]
    private async Task SaveContacts()
    {
        if (!CanEdit || original is null) return;
        var expected = owner; var request = loading;
        IsSaving = true;
        var edit = Draft();
        try
        {
            var result = await new ContactSaveFlow(source).SaveAsync(expected, original, edit);
            if (!ReferenceEquals(request, loading)) return;
            profiles.RequireOwner(expected);
            Present(result.Confirmed, result.LocalComplete);
            confirmedLocalDraft = result.LocalComplete ? null : edit;
            CanRetryLocal = !result.LocalComplete;
            StatusMessage = result.Status;
        }
        catch (Exception error)
        {
            if (ReferenceEquals(request, loading))
            {
                if (!profiles.IsCurrentOwner(expected)) StopLoading();
                StatusMessage = $"Account contact changes were not confirmed: {error.Message}";
            }
        }
        finally { IsSaving = false; }
    }

    [RelayCommand]
    private void RetryLocalSync()
    {
        if (!CanEdit || !CanRetryLocal || original is null) return;
        try { source.RetryLocal(owner, original, confirmedLocalDraft ?? Draft()); CanRetryLocal = false; confirmedLocalDraft = null; StatusMessage = "Previously saved contacts synced locally. Tap Save for any additional form edits."; }
        catch (Exception) { StatusMessage = "The local update could not be completed. Your confirmed account contacts are unchanged."; }
    }

    [RelayCommand] private Task PickContact1() => PickContactAsync(1);
    [RelayCommand] private Task PickContact2() => PickContactAsync(2);
    private async Task PickContactAsync(int slot)
    {
        if (!CanEdit) return;
        IsPicking = true;
        var expected = owner;
        try
        {
            profiles.RequireOwner(expected);
            var permission = await Permissions.CheckStatusAsync<Permissions.ContactsRead>();
            if (permission != PermissionStatus.Granted) permission = await Permissions.RequestAsync<Permissions.ContactsRead>();
            profiles.RequireOwner(expected);
            if (permission != PermissionStatus.Granted) { StatusMessage = "Contacts access was not granted. You can still enter a contact manually."; return; }
            var contact = await Microsoft.Maui.ApplicationModel.Communication.Contacts.Default.PickContactAsync();
            profiles.RequireOwner(expected);
            if (contact is null) return;
            string[] phones = contact.Phones?.Select(phone => phone.PhoneNumber).Where(phone => !string.IsNullOrWhiteSpace(phone)).Distinct().ToArray() ?? Array.Empty<string>();
            string number = phones.FirstOrDefault() ?? string.Empty;
            if (phones.Length > 1 && Shell.Current is { } page)
            {
                number = await page.DisplayActionSheet("Choose a phone number", "Cancel", null, phones);
                profiles.RequireOwner(expected);
                if (!phones.Contains(number)) return;
            }
            string name = string.IsNullOrWhiteSpace(contact.DisplayName) ? $"{contact.GivenName} {contact.FamilyName}".Trim() : contact.DisplayName;
            if (slot == 1) { Contact1Name = name; Contact1Phone = number; }
            else { Contact2Name = name; Contact2Phone = number; }
            StatusMessage = string.IsNullOrWhiteSpace(number) ? "Imported a name without a phone number. Add a number for a call shortcut, then tap Save." : "Contact imported into the form. Tap Save to confirm; nothing has been saved yet.";
        }
        catch (Exception)
        {
            if (!profiles.IsCurrentOwner(expected)) { StopLoading(); StatusMessage = "Your account changed. Reopen this screen."; }
            else StatusMessage = "Contact import was cancelled or unavailable. You can enter the details manually.";
        }
        finally { IsPicking = false; }
    }

    public void StopLoading()
    {
        loading?.Cancel(); loading?.Dispose(); loading = null; IsReady = false;
        original = null; confirmedLocalDraft = null; CanRetryLocal = false;
        Contact1Name = Contact1Phone = Contact1Relationship = Contact2Name = Contact2Phone = Contact2Relationship = string.Empty;
    }
    [RelayCommand] private async Task Back() { if (!IsSaving && Shell.Current != null) await Shell.Current.GoToAsync(".."); }
}
