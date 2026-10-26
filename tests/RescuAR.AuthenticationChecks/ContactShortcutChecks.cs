using System.Linq.Expressions;
using Newtonsoft.Json.Linq;
using RescuAR.App.Models;
using RescuAR.App.Services.Profile;

internal static class ContactShortcutChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        static string Field(Expression<Func<User, object>> expression) => ((MemberExpression)expression.Body).Member.Name;
        var owner = new ProfileAccountScope("project-a", "resident-a", 1);
        var original = new ContactValues("Alice", "0917", "Bob", "0918");
        check(ContactSaveFlow.Changes(original, original).Length == 0, "unchanged contacts make no account write");
        var phoneChange = ContactSaveFlow.Changes(original, original with { Phone1 = " 0999 " });
        check(phoneChange.Length == 1 && Field(phoneChange[0].Field) == "EmergencyContact1Phone" && (string)phoneChange[0].Value == "0999",
            "a contact-phone edit patches only the trimmed contact phone field");
        var all = ContactSaveFlow.Changes(original, new("new", "new", "new", "new"));
        check(all.Select(item => Field(item.Field)).ToHashSet().SetEquals(new[] { "EmergencyContact1Name", "EmergencyContact1Phone", "EmergencyContact2Name", "EmergencyContact2Phone" }),
            "contact saves cannot include names, health, address, avatar or circle fields from the profile");
        check(ContactSaveFlow.Changes(original, original with { Name1 = "", Phone1 = "" }).Length == 2,
            "clearing a contact explicitly clears only that slot's fields");
        var source = new FakeContacts(owner, original) { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var flow = new ContactSaveFlow(source);
        var edited = new ContactForm(original with { Phone1 = "0999" }, "Family", "Friend");
        var waiting = flow.SaveAsync(owner, original, edited);
        check(source.Events.SequenceEqual(new[] { "patch" }), "no relationship or shortcut state changes before the account acknowledges a contact save");
        source.Gate.SetResult();
        var saved = await waiting;
        check(saved.AccountChanged && saved.LocalComplete && source.Events.SequenceEqual(new[] { "patch", "local-sync" }),
            "confirmed contact saves complete local labels and shortcut synchronization afterward");
        source = new(owner, original) { PatchFails = true }; flow = new(source);
        bool failed = false;
        try { await flow.SaveAsync(owner, original, edited); } catch (IOException) { failed = true; }
        check(failed && !source.Events.Any(item => item.StartsWith("local")), "a failed account save cannot publish contacts to local shortcuts");
        source = new(owner, original) { ChangeOwnerDuringPatch = true }; flow = new(source);
        bool changed = false;
        try { await flow.SaveAsync(owner, original, edited); } catch (OperationCanceledException) { changed = true; }
        check(changed && !source.Events.Any(item => item.StartsWith("local")), "a late save cannot change another account's local contacts or shortcuts");
        source = new(owner, original) { LocalFails = true }; flow = new(source);
        var partial = await flow.SaveAsync(owner, original, edited);
        check(partial.AccountChanged && !partial.LocalComplete && partial.Status.Contains("saved to your account"),
            "local sync failure reports an already-confirmed account save accurately");
        source = new(owner, original); flow = new(source);
        var labels = await flow.SaveAsync(owner, original, new(original, "Family", "Friend"));
        check(!labels.AccountChanged && source.Events.SequenceEqual(new[] { "local-sync" }),
            "an explicit Save can repair local labels/shortcuts without rewriting unchanged account contacts");

        var defaults = JArray.Parse("""[{"Id":"flashlight","Title":"Flashlight","ActionType":"Flashlight","IsEnabled":true,"TargetRoute":"","extra":"preserve"},{"Id":"reports","Title":"Report","ActionType":"Route","TargetRoute":"//Reports","IsEnabled":false}]""");
        var initial = JArray.Parse(ContactShortcutPolicy.Synchronize(defaults.ToString(), original));
        check(initial.Count == 4 && JToken.DeepEquals(initial[0], defaults[0]) && JToken.DeepEquals(initial[1], defaults[1]),
            "adding contacts retains unrelated shortcuts, order, unknown fields, routes and disabled toggles");
        check(initial[2]!["Id"]!.Value<string>() == "emergency_contact_1" && initial[3]!["Id"]!.Value<string>() == "emergency_contact_2",
            "saved contacts receive stable per-slot shortcut identifiers");
        initial[2]!["IsEnabled"] = false;
        var updated = JArray.Parse(ContactShortcutPolicy.Synchronize(initial.ToString(), original with { Name1 = "Changed", Phone1 = "0999" }));
        check(updated.Count == 4 && (string?)updated[2]!["Title"] == "Changed" && (bool?)updated[2]!["IsEnabled"] == false,
            "editing a contact keeps its shortcut position and disabled toggle");
        var repeat = JArray.Parse(ContactShortcutPolicy.Synchronize(updated.ToString(), original with { Name1 = "Changed", Phone1 = "0999" }));
        check(JToken.DeepEquals(updated, repeat), "repeated contact synchronization does not duplicate or reorder shortcuts");
        var cleared = JArray.Parse(ContactShortcutPolicy.Synchronize(initial.ToString(), original with { Name1 = "", Phone1 = "" }));
        check(cleared.Count == 3 && JToken.DeepEquals(cleared[0], defaults[0]) && (string?)cleared[2]!["Id"] == "emergency_contact_2",
            "clearing a contact removes its owned shortcut without deleting unrelated shortcuts");
        var duplicate = JArray.Parse(ContactShortcutPolicy.Synchronize(defaults.ToString(), new("Alice", "0917", " Alice ", "09-17")));
        check(duplicate.Count == 3, "the same saved contact in both slots appears once in Quick Actions");
        var sameName = JArray.Parse(ContactShortcutPolicy.Synchronize(defaults.ToString(), new("Alice", "0917", "Alice", "0918")));
        check(sameName.Count == 4, "two contacts with the same name and different phone numbers remain distinct");
        var manual = (JArray)defaults.DeepClone();
        manual.Insert(1, JObject.Parse("""{"Id":"manual","Title":"Alice","PhoneNumber":"09 17","ActionType":"CustomContact","IsCustom":true,"IsEnabled":false,"IconColor":"#123456"}"""));
        var reused = JArray.Parse(ContactShortcutPolicy.Synchronize(manual.ToString(), new("Alice", "0917", "", "")));
        check(reused.Count == 3 && (string?)reused[1]!["Id"] == "emergency_contact_1" && (bool?)reused[1]!["IsEnabled"] == false && (string?)reused[1]!["IconColor"] == "#123456",
            "a matching manual contact is reused in place with its toggle and appearance retained");
        var incomplete = JArray.Parse(ContactShortcutPolicy.Synchronize(defaults.ToString(), new("Name only", "", "", "0917")));
        check(JToken.DeepEquals(incomplete, defaults), "incomplete contacts cannot create unusable call shortcuts");
        bool corrupt = false;
        try { ContactShortcutPolicy.Synchronize("not-json", original); } catch (Newtonsoft.Json.JsonException) { corrupt = true; }
        check(corrupt, "invalid saved shortcut data is rejected instead of silently discarded");

        var storage = new FakeShortcutStorage(owner);
        var store = new ContactShortcutStore(storage);
        store.Queue(owner, original);
        check(storage.Get(owner, "QuickActions") == "" && storage.Get(owner, "PendingContactShortcuts") != "",
            "saving contacts before the first dashboard does not replace dev's standard shortcuts with contacts alone");
        var firstDashboard = JArray.Parse(store.Apply(owner, defaults.ToString()));
        check(firstDashboard.Count == 4 && JToken.DeepEquals(firstDashboard[0], defaults[0]), "first dashboard initializes dev defaults before applying the queued confirmed contacts");
        firstDashboard.RemoveAt(2);
        string afterDelete = firstDashboard.ToString();
        check(store.Apply(owner, afterDelete) == afterDelete, "a later dashboard refresh respects deletion of a previously synchronized contact shortcut");
        storage.Set(owner, "QuickActions", afterDelete);
        store.Queue(owner, original);
        check(JArray.Parse(storage.Get(owner, "QuickActions")).Count == 4,
            "a new explicit contact save can synchronize that shortcut again");
        storage = new(owner); storage.Set(owner, "QuickActions", "[]"); store = new(storage);
        store.Queue(owner, original);
        check(storage.Get(owner, "QuickActions") == "[]", "an empty saved list also waits for dashboard defaults before synchronization");
        storage = new(owner); storage.Set(owner, "QuickActions", "not-json"); store = new(storage);
        bool corruptPreserved = false;
        try { store.Queue(owner, original); } catch (Newtonsoft.Json.JsonException) { corruptPreserved = true; }
        check(corruptPreserved && storage.Get(owner, "QuickActions") == "not-json", "failed shortcut parsing preserves the original saved shortcut data");
        storage = new(owner); store = new(storage); store.Queue(owner, original);
        storage.FailMark = true;
        bool markFailed = false;
        try { store.Apply(owner, defaults.ToString()); } catch (IOException) { markFailed = true; }
        storage.FailMark = false;
        var recovered = JArray.Parse(store.Apply(owner, storage.Get(owner, "QuickActions")));
        check(markFailed && recovered.Count == 4, "retry after a failed sync marker write remains idempotent");
        storage.Current = owner with { UserId = "resident-b" };
        check(store.Apply(storage.Current, defaults.ToString()) == defaults.ToString(), "another account cannot consume the previous account's pending contact shortcuts");
        storage.Current = owner with { Project = "project-b" };
        check(store.Apply(storage.Current, defaults.ToString()) == defaults.ToString(), "pending contact shortcuts are isolated between backend projects");
        storage.Current = owner with { Revision = 2 };
        bool stale = false;
        try { store.Queue(owner, original); } catch (OperationCanceledException) { stale = true; }
        check(stale, "an obsolete session cannot queue contact shortcuts for a newer login");

        check(SystemInformationPresentation.Version(null, null) == "Unavailable", "unknown installed version is never replaced by a fabricated v1.0.0");
        check(SystemInformationPresentation.Version("2.4", "7") == "v2.4 (build 7)", "System Information formats the actual installed version and build");
        check(SystemInformationPresentation.Updated(null) == "Unavailable" && SystemInformationPresentation.Updated(0) == "Unavailable" && SystemInformationPresentation.Updated(long.MaxValue) == "Unavailable",
            "missing or invalid package-update timestamps never fall back to today's date");
        check(SystemInformationPresentation.Updated(1_700_000_000_000) != "Unavailable", "a real installed-package update timestamp can be displayed");
    }

    private sealed class FakeContacts(ProfileAccountScope owner, ContactValues confirmed) : IContactSaveSource
    {
        public ProfileAccountScope Current = owner;
        public TaskCompletionSource? Gate;
        public bool PatchFails, LocalFails, ChangeOwnerDuringPatch;
        public List<string> Events = new();
        public void RequireCurrent(ProfileAccountScope expected) { if (expected != Current) throw new OperationCanceledException(); }
        public async Task<ContactValues> PatchAsync(ProfileAccountScope expected, (Expression<Func<User, object>> Field, object Value)[] fields)
        {
            Events.Add("patch");
            if (Gate is not null) await Gate.Task;
            if (PatchFails) throw new IOException("test account failure");
            if (ChangeOwnerDuringPatch) Current = Current with { UserId = "other" };
            return confirmed;
        }
        public void CompleteLocal(ProfileAccountScope expected, ContactValues result, ContactForm edit, bool synchronize)
        {
            Events.Add(synchronize ? "local-sync" : "local-labels");
            if (LocalFails) throw new IOException("test storage failure");
        }
    }

    private sealed class FakeShortcutStorage(ProfileAccountScope owner) : IContactShortcutStorage
    {
        public ProfileAccountScope Current = owner;
        public bool FailMark;
        private readonly Dictionary<string, string> values = new();
        private static string Key(ProfileAccountScope expected, string name) => expected.Project + ":" + expected.UserId + ":" + name;
        public void RequireCurrent(ProfileAccountScope expected) { if (expected != Current) throw new OperationCanceledException(); }
        public string Get(ProfileAccountScope expected, string name) { RequireCurrent(expected); return values.GetValueOrDefault(Key(expected, name), ""); }
        public void Set(ProfileAccountScope expected, string name, string value)
        {
            RequireCurrent(expected);
            if (FailMark && name == "AppliedContactShortcuts") throw new IOException("test marker failure");
            values[Key(expected, name)] = value;
        }
    }
}
