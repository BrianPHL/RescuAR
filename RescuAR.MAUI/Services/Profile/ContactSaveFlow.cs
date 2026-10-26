using System.Linq.Expressions;
using RescuAR.App.Models;

namespace RescuAR.App.Services.Profile;

internal sealed record ContactValues(string Name1, string Phone1, string Name2, string Phone2)
{
    public static ContactValues From(User user) => new(user.EmergencyContact1Name ?? "", user.EmergencyContact1Phone ?? "",
        user.EmergencyContact2Name ?? "", user.EmergencyContact2Phone ?? "");
    public ContactValues Trimmed() => new(Name1.Trim(), Phone1.Trim(), Name2.Trim(), Phone2.Trim());
    public (string Name, string Phone) Slot(int slot) => slot == 1 ? (Name1, Phone1) : (Name2, Phone2);
}
internal sealed record ContactForm(ContactValues Values, string Relationship1, string Relationship2);
internal sealed record ContactSaveResult(ContactValues Confirmed, bool AccountChanged, bool LocalComplete, string Status);
internal interface IContactSaveSource
{
    void RequireCurrent(ProfileAccountScope owner);
    Task<ContactValues> PatchAsync(ProfileAccountScope owner, (Expression<Func<User, object>> Field, object Value)[] fields);
    void CompleteLocal(ProfileAccountScope owner, ContactValues confirmed, ContactForm edit, bool synchronizeShortcuts);
}
internal sealed class ContactSaveFlow(IContactSaveSource source)
{
    public static (Expression<Func<User, object>> Field, object Value)[] Changes(ContactValues original, ContactValues edit)
    {
        edit = edit.Trimmed();
        var fields = new List<(Expression<Func<User, object>> Field, object Value)>();
        Add(user => user.EmergencyContact1Name, original.Name1, edit.Name1);
        Add(user => user.EmergencyContact1Phone, original.Phone1, edit.Phone1);
        Add(user => user.EmergencyContact2Name, original.Name2, edit.Name2);
        Add(user => user.EmergencyContact2Phone, original.Phone2, edit.Phone2);
        return fields.ToArray();
        void Add(Expression<Func<User, object>> field, string before, string after) { if (before != after) fields.Add((field, after)); }
    }

    public async Task<ContactSaveResult> SaveAsync(ProfileAccountScope owner, ContactValues original, ContactForm edit)
    {
        source.RequireCurrent(owner);
        var fields = Changes(original, edit.Values);
        var confirmed = fields.Length == 0 ? original : await source.PatchAsync(owner, fields);
        source.RequireCurrent(owner);
        try
        {
            source.CompleteLocal(owner, confirmed, edit, true);
            source.RequireCurrent(owner);
            return new(confirmed, fields.Length > 0, true, fields.Length > 0
                ? "Contacts saved to your account. Matching Quick Actions will refresh on the dashboard."
                : "Account contacts are unchanged. Relationship labels are saved on this device; matching Quick Actions will refresh.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            source.RequireCurrent(owner);
            return new(confirmed, fields.Length > 0, false, fields.Length > 0
                ? "Contacts saved to your account, but this device's relationship labels or shortcuts could not be updated. Reopen this screen and retry local sync."
                : "Account contacts are unchanged. Local labels or shortcuts could not be updated; please retry.");
        }
    }
}
