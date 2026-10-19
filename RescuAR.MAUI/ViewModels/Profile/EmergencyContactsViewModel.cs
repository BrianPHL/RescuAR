using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using RescuAR.App.Models;
using RescuAR.Services;

namespace RescuAR.App.ViewModels.Profile;

public partial class EmergencyContactsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _contact1Name = string.Empty;

    [ObservableProperty]
    private string _contact1Phone = string.Empty;

    [ObservableProperty]
    private string _contact1Relationship = string.Empty;

    [ObservableProperty]
    private string _contact2Name = string.Empty;

    [ObservableProperty]
    private string _contact2Phone = string.Empty;

    [ObservableProperty]
    private string _contact2Relationship = string.Empty;

    [ObservableProperty]
    private bool _isSaving;

    public EmergencyContactsViewModel()
    {
        _ = LoadContactsAsync();
    }

    public async Task LoadContactsAsync()
    {
        try
        {
            // 1. Instantaneous local cache
            Contact1Name = Preferences.Default.Get("EmergencyContact1Name", string.Empty);
            Contact1Phone = Preferences.Default.Get("EmergencyContact1Phone", string.Empty);
            Contact1Relationship = Preferences.Default.Get("EmergencyContact1Rel", "Family");

            Contact2Name = Preferences.Default.Get("EmergencyContact2Name", string.Empty);
            Contact2Phone = Preferences.Default.Get("EmergencyContact2Phone", string.Empty);
            Contact2Relationship = Preferences.Default.Get("EmergencyContact2Rel", "Friend");

            // 2. Fetch from Supabase
            var client = SupabaseService.Instance.Client;
            var userId = client?.Auth.CurrentUser?.Id ?? Preferences.Default.Get("current_user_id", string.Empty);

            if (client != null && !string.IsNullOrWhiteSpace(userId))
            {
                var user = await client.From<User>().Where(u => u.Id == userId).Single();
                if (user != null)
                {
                    if (!string.IsNullOrWhiteSpace(user.EmergencyContact1Name))
                        Contact1Name = user.EmergencyContact1Name;
                    if (!string.IsNullOrWhiteSpace(user.EmergencyContact1Phone))
                        Contact1Phone = user.EmergencyContact1Phone;

                    if (!string.IsNullOrWhiteSpace(user.EmergencyContact2Name))
                        Contact2Name = user.EmergencyContact2Name;
                    if (!string.IsNullOrWhiteSpace(user.EmergencyContact2Phone))
                        Contact2Phone = user.EmergencyContact2Phone;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading emergency contacts: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task PickContact1Async()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.ContactsRead>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.ContactsRead>();
                if (status != PermissionStatus.Granted)
                {
                    if (Shell.Current != null)
                        await Shell.Current.DisplayAlert("Permission Denied", "Contacts permission is required to import from your contacts.", "OK");
                    return;
                }
            }

            var contact = await Contacts.Default.PickContactAsync();
            if (contact != null)
            {
                Contact1Name = $"{contact.GivenName} {contact.FamilyName}".Trim();
                var phone = contact.Phones?.Count > 0 ? contact.Phones[0].PhoneNumber : string.Empty;
                if (!string.IsNullOrWhiteSpace(phone))
                {
                    Contact1Phone = phone;
                }
                await SyncEmergencyContactsToQuickActionsAsync();
                if (Shell.Current != null)
                {
                    await Shell.Current.DisplayAlert("Contact Imported", $"{Contact1Name} was imported and connected to your Quick Actions.", "OK");
                }
            }
        }
        catch (Exception ex)
        {
            if (Shell.Current != null)
                await Shell.Current.DisplayAlert("Contacts", $"Unable to pick contact: {ex.Message}", "OK");
        }
    }

    [RelayCommand]
    private async Task PickContact2Async()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.ContactsRead>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.ContactsRead>();
                if (status != PermissionStatus.Granted)
                {
                    if (Shell.Current != null)
                        await Shell.Current.DisplayAlert("Permission Denied", "Contacts permission is required to import from your contacts.", "OK");
                    return;
                }
            }

            var contact = await Contacts.Default.PickContactAsync();
            if (contact != null)
            {
                Contact2Name = $"{contact.GivenName} {contact.FamilyName}".Trim();
                var phone = contact.Phones?.Count > 0 ? contact.Phones[0].PhoneNumber : string.Empty;
                if (!string.IsNullOrWhiteSpace(phone))
                {
                    Contact2Phone = phone;
                }
                await SyncEmergencyContactsToQuickActionsAsync();
                if (Shell.Current != null)
                {
                    await Shell.Current.DisplayAlert("Contact Imported", $"{Contact2Name} was imported and connected to your Quick Actions.", "OK");
                }
            }
        }
        catch (Exception ex)
        {
            if (Shell.Current != null)
                await Shell.Current.DisplayAlert("Contacts", $"Unable to pick contact: {ex.Message}", "OK");
        }
    }

    private async Task SyncEmergencyContactsToQuickActionsAsync()
    {
        try
        {
            var c1Name = Contact1Name?.Trim() ?? string.Empty;
            var c1Phone = Contact1Phone?.Trim() ?? string.Empty;
            var c1Rel = Contact1Relationship?.Trim() ?? "Family";

            var c2Name = Contact2Name?.Trim() ?? string.Empty;
            var c2Phone = Contact2Phone?.Trim() ?? string.Empty;
            var c2Rel = Contact2Relationship?.Trim() ?? "Friend";

            // Save to Preferences
            Preferences.Default.Set("EmergencyContact1Name", c1Name);
            Preferences.Default.Set("EmergencyContact1Phone", c1Phone);
            Preferences.Default.Set("EmergencyContact1Rel", c1Rel);

            Preferences.Default.Set("EmergencyContact2Name", c2Name);
            Preferences.Default.Set("EmergencyContact2Phone", c2Phone);
            Preferences.Default.Set("EmergencyContact2Rel", c2Rel);

            // Sync with UserProfileService & Quick Actions
            var profiles = RescuAR.App.Services.Profile.UserProfileService.Instance;
            try
            {
                await profiles.PatchAsync(
                    (x => x.EmergencyContact1Name, c1Name),
                    (x => x.EmergencyContact1Phone, c1Phone),
                    (x => x.EmergencyContact2Name, c2Name),
                    (x => x.EmergencyContact2Phone, c2Phone)
                );
            }
            catch { }

            var user = await profiles.LoadAsync();
            var json = profiles.GetLocal("QuickActions");
            var actions = string.IsNullOrWhiteSpace(json)
                ? new System.Collections.Generic.List<RescuAR.App.Models.QuickActionItem>()
                : Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<RescuAR.App.Models.QuickActionItem>>(json) ?? new();

            profiles.SyncContacts(actions, user);
            profiles.SetLocal("QuickActions", Newtonsoft.Json.JsonConvert.SerializeObject(actions));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SyncEmergencyContactsToQuickActions error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SaveContactsAsync()
    {
        if (IsSaving) return;

        IsSaving = true;
        try
        {
            await SyncEmergencyContactsToQuickActionsAsync();

            // Save to Supabase
            var client = SupabaseService.Instance.Client;
            var userId = client?.Auth.CurrentUser?.Id ?? Preferences.Default.Get("current_user_id", string.Empty);

            if (client != null && !string.IsNullOrWhiteSpace(userId))
            {
                var userRecord = new User
                {
                    Id = userId,
                    EmergencyContact1Name = Contact1Name.Trim(),
                    EmergencyContact1Phone = Contact1Phone.Trim(),
                    EmergencyContact2Name = Contact2Name.Trim(),
                    EmergencyContact2Phone = Contact2Phone.Trim()
                };

                try
                {
                    await client.From<User>().Upsert(userRecord);
                }
                catch { }
            }

            if (Shell.Current != null)
            {
                await Shell.Current.DisplayAlert("Saved", "Emergency contacts saved successfully!", "OK");
                await Shell.Current.GoToAsync("..");
            }
        }
        catch (Exception ex)
        {
            if (Shell.Current != null)
                await Shell.Current.DisplayAlert("Error", $"Could not save contacts: {ex.Message}", "OK");
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("..");
        }
    }
}

