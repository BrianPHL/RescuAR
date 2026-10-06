using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RescuAR.App.Services.Profile;

namespace RescuAR.App.ViewModels.Profile;

public partial class EmergencyContactsViewModel : ObservableObject
{
    [ObservableProperty] private string _contact1Name = string.Empty;
    [ObservableProperty] private string _contact1Phone = string.Empty;
    [ObservableProperty] private string _contact1Relationship = string.Empty;
    [ObservableProperty] private string _contact2Name = string.Empty;
    [ObservableProperty] private string _contact2Phone = string.Empty;
    [ObservableProperty] private string _contact2Relationship = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private string _statusMessage = string.Empty;
    private string _loadedId = string.Empty;

    public async Task LoadAsync()
    {
        IsBusy = true; IsReady = false;
        try
        {
            var profiles = UserProfileService.Instance;
            var user = await profiles.LoadAsync();
            _loadedId = user.Id;
            Contact1Name = user.EmergencyContact1Name; Contact1Phone = user.EmergencyContact1Phone;
            Contact2Name = user.EmergencyContact2Name; Contact2Phone = user.EmergencyContact2Phone;
            Contact1Relationship = profiles.GetLocal("Contact1Relationship");
            Contact2Relationship = profiles.GetLocal("Contact2Relationship");
            IsReady = true;
        }
        catch (Exception ex) { StatusMessage = $"Could not load your contacts: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand] private Task PickContact1() => PickContactAsync(1);
    [RelayCommand] private Task PickContact2() => PickContactAsync(2);

    private async Task PickContactAsync(int slot)
    {
        if (IsBusy || !IsReady) return;
        IsBusy = true;
        try
        {
            var permission = await Permissions.CheckStatusAsync<Permissions.ContactsRead>();
            if (permission != PermissionStatus.Granted) permission = await Permissions.RequestAsync<Permissions.ContactsRead>();
            if (permission != PermissionStatus.Granted)
            { StatusMessage = "Allow contacts access to import a contact, or enter the details manually."; return; }
            var contact = await Contacts.Default.PickContactAsync();
            if (contact == null) return;
            var phone = contact.Phones.FirstOrDefault()?.PhoneNumber ?? string.Empty;
            if (slot == 1) { Contact1Name = contact.DisplayName; Contact1Phone = phone; }
            else { Contact2Name = contact.DisplayName; Contact2Phone = phone; }
        }
        catch (Exception ex) { StatusMessage = $"Could not import the contact: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveContacts()
    {
        if (IsBusy || !IsReady) return;
        bool Complete(string name, string phone) => string.IsNullOrWhiteSpace(name) == string.IsNullOrWhiteSpace(phone);
        if (!Complete(Contact1Name, Contact1Phone) || !Complete(Contact2Name, Contact2Phone))
        { StatusMessage = "Each contact needs both a name and phone number. Clear both fields to remove it."; return; }
        IsBusy = true;
        try
        {
            var profiles = UserProfileService.Instance;
            if (_loadedId != profiles.UserId) throw new InvalidOperationException("Your account changed. Reopen this screen.");
            await profiles.PatchAsync((x => x.EmergencyContact1Name, Contact1Name.Trim()),
                (x => x.EmergencyContact1Phone, Contact1Phone.Trim()), (x => x.EmergencyContact2Name, Contact2Name.Trim()),
                (x => x.EmergencyContact2Phone, Contact2Phone.Trim()));
            // The existing database has no relationship columns; these labels stay on this device, per account.
            profiles.SetLocal("Contact1Relationship", Contact1Relationship.Trim());
            profiles.SetLocal("Contact2Relationship", Contact2Relationship.Trim());
            StatusMessage = "Emergency contacts saved. Relationship labels are saved on this device.";
        }
        catch (Exception ex) { StatusMessage = $"Changes were not saved: {ex.Message}"; }
        finally { IsBusy = false; }
    }
    [RelayCommand] private async Task Back() { if (!IsBusy && Shell.Current != null) await Shell.Current.GoToAsync(".."); }
}
