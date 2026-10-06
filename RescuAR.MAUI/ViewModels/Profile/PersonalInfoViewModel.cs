using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RescuAR.App.Services.Profile;

namespace RescuAR.App.ViewModels.Profile;

public partial class PersonalInfoViewModel : ObservableObject
{
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _firstName = string.Empty;
    [ObservableProperty] private string _middleName = string.Empty;
    [ObservableProperty] private string _lastName = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _phoneNumber = string.Empty;
    [ObservableProperty] private string _streetAddress = string.Empty;
    [ObservableProperty] private string _selectedBarangay = string.Empty;
    [ObservableProperty] private string _city = "Marikina City";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private string _statusMessage = string.Empty;
    private string _loadedId = string.Empty;
    private string _originalAddress = string.Empty;
    private (string Street, string Barangay, string City) _originalParts;
    public List<string> MarikinaBarangays { get; } = new() {
        "Barangka", "Calumpang", "Concepcion Uno", "Concepcion Dos", "Fortune", "Industrial Valley",
        "Jesus Dela Peña", "Malanday", "Marikina Heights", "Nangka", "Parang", "San Roque",
        "Santa Elena", "Santo Niño", "Tañong", "Tumana" };

    public async Task LoadAsync()
    {
        IsBusy = true; IsReady = false;
        try
        {
            var user = await UserProfileService.Instance.LoadAsync();
            _loadedId = user.Id;
            Username = user.Username; FirstName = user.FirstName; MiddleName = user.MiddleName;
            LastName = user.LastName; Email = user.Email; PhoneNumber = user.PhoneNumber;
            _originalAddress = user.Address;
            _originalParts = UserProfileService.ParseAddress(user.Address);
            StreetAddress = _originalParts.Street; SelectedBarangay = _originalParts.Barangay; City = _originalParts.City;
            IsReady = true;
        }
        catch (Exception ex) { StatusMessage = $"Could not load your profile: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveInformation()
    {
        if (IsBusy || !IsReady) return;
        if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName) || string.IsNullOrWhiteSpace(PhoneNumber))
        { StatusMessage = "First name, last name and phone number are required."; return; }
        IsBusy = true;
        try
        {
            var profiles = UserProfileService.Instance;
            if (_loadedId != profiles.UserId) throw new InvalidOperationException("Your account changed. Reopen this screen.");
            var address = _originalAddress;
            if (StreetAddress != _originalParts.Street || SelectedBarangay != _originalParts.Barangay)
            {
                if (string.IsNullOrWhiteSpace(StreetAddress) || !MarikinaBarangays.Contains(SelectedBarangay))
                    throw new InvalidOperationException("Enter your street address and select a barangay.");
                address = $"{StreetAddress.Trim()}, {SelectedBarangay}, {City}";
                if (_originalAddress.Split(',').Length >= 6)
                    address += ", Metro Manila, 1800";
            }
            await profiles.PatchAsync((x => x.Username, Username.Trim()), (x => x.FirstName, FirstName.Trim()),
                (x => x.MiddleName, MiddleName.Trim()), (x => x.LastName, LastName.Trim()),
                (x => x.PhoneNumber, PhoneNumber.Trim()), (x => x.Address, address));
            _originalAddress = address;
            _originalParts = UserProfileService.ParseAddress(address);
            StatusMessage = "Personal information saved to your account.";
        }
        catch (Exception ex) { StatusMessage = $"Changes were not saved: {ex.Message}"; }
        finally { IsBusy = false; }
    }
    [RelayCommand] private async Task Back() { if (!IsBusy && Shell.Current != null) await Shell.Current.GoToAsync(".."); }
}
