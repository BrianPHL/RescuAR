using System.Linq.Expressions;
using Newtonsoft.Json;
using RescuAR.App.Models;

namespace RescuAR.App.Services.Profile;

internal sealed class MauiContactShortcutStorage : IContactShortcutStorage
{
    private readonly UserProfileService profiles = UserProfileService.Instance;
    public void RequireCurrent(ProfileAccountScope owner) => profiles.RequireOwner(owner);
    public string Get(ProfileAccountScope owner, string key) => profiles.GetLocal(owner, key);
    public void Set(ProfileAccountScope owner, string key, string value) => profiles.SetLocal(owner, key, value);
}

internal sealed class MauiContactSaveSource : IContactSaveSource
{
    private readonly UserProfileService profiles = UserProfileService.Instance;
    private sealed record Labels(ContactValues Contacts, string Relationship1, string Relationship2);
    public void RequireCurrent(ProfileAccountScope owner) => profiles.RequireOwner(owner);
    public async Task<ContactValues> PatchAsync(ProfileAccountScope owner, (Expression<Func<User, object>> Field, object Value)[] fields) =>
        ContactValues.From(await profiles.PatchOwnedAsync(owner, fields));

    public (string First, string Second) ReadLabels(ProfileAccountScope owner, ContactValues current)
    {
        RequireCurrent(owner);
        try
        {
            var saved = JsonConvert.DeserializeObject<Labels>(profiles.GetLocal(owner, "ContactRelationships"));
            if (saved is null) return ("", "");
            return (Same(saved.Contacts, current, 1) ? saved.Relationship1 : "", Same(saved.Contacts, current, 2) ? saved.Relationship2 : "");
        }
        catch (JsonException) { return ("", ""); }
    }

    public void CompleteLocal(ProfileAccountScope owner, ContactValues confirmed, ContactForm edit, bool synchronizeShortcuts)
    {
        RequireCurrent(owner);
        var labels = new Labels(confirmed, Same(confirmed, edit.Values, 1) ? edit.Relationship1.Trim() : "",
            Same(confirmed, edit.Values, 2) ? edit.Relationship2.Trim() : "");
        profiles.SetLocal(owner, "ContactRelationships", JsonConvert.SerializeObject(labels));
        if (Preferences.Default.Get("CurrentUserId", "") == owner.UserId && Preferences.Default.Get("CurrentAuthProject", "") == owner.Project)
        {
            Preferences.Default.Set("EmergencyContact1Name", confirmed.Name1); Preferences.Default.Set("EmergencyContact1Phone", confirmed.Phone1);
            Preferences.Default.Set("EmergencyContact2Name", confirmed.Name2); Preferences.Default.Set("EmergencyContact2Phone", confirmed.Phone2);
        }
        if (synchronizeShortcuts) new ContactShortcutStore(new MauiContactShortcutStorage()).Queue(owner, confirmed);
    }

    public void RetryLocal(ProfileAccountScope owner, ContactValues confirmed, ContactForm edit) => CompleteLocal(owner, confirmed, edit, true);
    private static bool Same(ContactValues left, ContactValues right, int slot)
    {
        var first = left.Slot(slot); var second = right.Slot(slot);
        return ContactShortcutPolicy.Identity(first.Name, first.Phone) == ContactShortcutPolicy.Identity(second.Name, second.Phone);
    }
}
