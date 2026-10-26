using Newtonsoft.Json;

namespace RescuAR.App.Services.Profile;

internal interface IContactShortcutStorage
{
    void RequireCurrent(ProfileAccountScope owner);
    string Get(ProfileAccountScope owner, string key);
    void Set(ProfileAccountScope owner, string key, string value);
}
internal sealed class ContactShortcutStore(IContactShortcutStorage storage)
{
    private sealed record Pending(string Revision, ContactValues Contacts);
    public void Queue(ProfileAccountScope owner, ContactValues confirmed)
    {
        storage.RequireCurrent(owner);
        storage.Set(owner, "PendingContactShortcuts", JsonConvert.SerializeObject(new Pending(Guid.NewGuid().ToString("N"), confirmed)));
        string existing = storage.Get(owner, "QuickActions");
        // A dashboard that has never opened must populate dev's defaults first.
        if (!string.IsNullOrWhiteSpace(existing) && Newtonsoft.Json.Linq.JArray.Parse(existing).Count > 0) Apply(owner, existing);
    }

    public string Apply(ProfileAccountScope owner, string currentActions)
    {
        storage.RequireCurrent(owner);
        string pendingJson = storage.Get(owner, "PendingContactShortcuts");
        if (string.IsNullOrWhiteSpace(pendingJson)) return currentActions;
        var pending = JsonConvert.DeserializeObject<Pending>(pendingJson) ?? throw new InvalidOperationException("Contact shortcut sync could not be read.");
        if (pending.Revision == storage.Get(owner, "AppliedContactShortcuts")) return currentActions;
        string updated = ContactShortcutPolicy.Synchronize(currentActions, pending.Contacts);
        storage.RequireCurrent(owner);
        storage.Set(owner, "QuickActions", updated);
        storage.Set(owner, "AppliedContactShortcuts", pending.Revision);
        return updated;
    }
}
