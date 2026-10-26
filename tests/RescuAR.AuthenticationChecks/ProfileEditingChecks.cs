using System.Linq.Expressions;
using RescuAR.App.Models;
using RescuAR.App.Services.Profile;

internal static class ProfileEditingChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        static string Field(Expression<Func<User, object>> expression) =>
            (expression.Body is UnaryExpression conversion ? (MemberExpression)conversion.Operand : (MemberExpression)expression.Body).Member.Name;
        static PersonalProfileEdit Edit(PersonalProfileValues original)
        {
            var address = ProfileEditingPolicy.ParseAddress(original.Address);
            return new(original.Username, original.FirstName, original.MiddleName, original.LastName, original.PhoneNumber, address.Street, address.Barangay);
        }
        var original = new PersonalProfileValues("resident", "Juan", "Miguel", "Cruz", "09170000000", "12-A, River Street, Calumpang, Marikina City, Metro Manila, 1800");
        var unchanged = Edit(original);
        check(ProfileEditingPolicy.PersonalChanges(original, unchanged).Length == 0, "an unchanged personal screen produces no profile update");
        var username = ProfileEditingPolicy.PersonalChanges(original, unchanged with { Username = "  new-name  " });
        check(username.Length == 1 && Field(username[0].Field) == nameof(User.Username) && (string)username[0].Value == "new-name",
            "a display-name change updates only the trimmed username");
        var names = ProfileEditingPolicy.PersonalChanges(original, unchanged with { FirstName = "New", MiddleName = "", LastName = "Name" });
        check(names.Select(item => Field(item.Field)).ToHashSet().SetEquals(new[] { "FirstName", "MiddleName", "LastName" }),
            "name edits retain the optional middle-name field without writing unrelated data");
        var phone = ProfileEditingPolicy.PersonalChanges(original, unchanged with { PhoneNumber = "" });
        check(phone.Length == 1 && Field(phone[0].Field) == "PhoneNumber", "clearing a phone value is an explicit field-only edit");
        check(typeof(PersonalProfileEdit).GetProperty("Email") is null, "personal edits cannot change the verified sign-in email");
        check(ProfileEditingPolicy.ParseAddress(original.Address) == ("12-A, River Street", "Calumpang", "Marikina City"),
            "six-part dev addresses preserve the house and street together");
        check(ProfileEditingPolicy.ParseAddress("House Street, Calumpang, Marikina City, Metro Manila, 1800") == ("House Street", "Calumpang", "Marikina City"),
            "five-part resident addresses parse without losing the barangay");
        check(ProfileEditingPolicy.ParseAddress("Street, Calumpang, Marikina City") == ("Street", "Calumpang", "Marikina City"),
            "three-part Athena addresses remain editable");
        var addressChange = ProfileEditingPolicy.PersonalChanges(original, unchanged with { Street = "22, New Street" });
        check(addressChange.Length == 1 && (string)addressChange[0].Value == "22, New Street, Calumpang, Marikina City, Metro Manila, 1800",
            "an address edit preserves the original city, region and postal suffix");
        var unfamiliar = original with { Address = "Unstructured address retained verbatim" };
        check(ProfileEditingPolicy.PersonalChanges(unfamiliar, Edit(unfamiliar) with { Username = "updated" }).All(item => Field(item.Field) != "Address"),
            "editing a name cannot rewrite an unfamiliar stored address");
        bool invalidAddress = false;
        try { ProfileEditingPolicy.PersonalChanges(original, unchanged with { Barangay = "Not in catalog" }); }
        catch (InvalidOperationException) { invalidAddress = true; }
        check(invalidAddress, "a changed barangay must belong to the existing city catalog");
        bool missingName = false;
        try { ProfileEditingPolicy.PersonalChanges(original, unchanged with { FirstName = " " }); }
        catch (InvalidOperationException) { missingName = true; }
        check(missingName, "personal edits retain the required first and last names");

        var health = new HealthProfileValues("card", "A+", "allergy", "medication", "120/80", "needs", false);
        check(ProfileEditingPolicy.HealthChanges(health, health).Length == 0, "unchanged health data produces no update");
        var allergies = ProfileEditingPolicy.HealthChanges(health, health with { Allergies = "" });
        check(allergies.Length == 1 && Field(allergies[0].Field) == "Allergies" && (string)allergies[0].Value == "",
            "clearing allergies updates only that health field");
        var allHealth = ProfileEditingPolicy.HealthChanges(health, new("new", "O-", "new", "new", "new", "new", true));
        check(allHealth.Select(item => Field(item.Field)).ToHashSet().SetEquals(new[] { "HealthCardNumber", "BloodType", "Allergies", "MaintenanceMedications", "AverageBloodPressure", "DisabilityOrSpecialNeeds", "IsOrganDonor" }),
            "health saves retain all seven dev fields and cannot include identity, contacts, avatar or circle fields");
        check(ProfileEditingPolicy.HealthChanges(health with { BloodType = "legacy unknown" }, health with { BloodType = "legacy unknown", Card = "changed" }).Length == 1,
            "an unfamiliar existing blood-type value is preserved when another health field changes");

        var owner = new ProfileAccountScope("project-a", "resident-a", 1);
        check(AvatarUpdateFlow.ScopeKey(owner) != AvatarUpdateFlow.ScopeKey(owner with { UserId = "resident-b" }) &&
            AvatarUpdateFlow.ScopeKey(owner) != AvatarUpdateFlow.ScopeKey(owner with { Project = "project-b" }),
            "local photo folders isolate accounts and backend projects");
        check(AvatarUpdateFlow.ScopeKey(owner) == AvatarUpdateFlow.ScopeKey(owner with { Revision = 2 }),
            "the same account can reopen its new scoped photo after a later login");
        check(AvatarUpdateFlow.Extension("picked.PNG") == ".png" && AvatarUpdateFlow.Extension("file.unknown") == ".jpg",
            "selected image extensions are normalized to the supported image set");
        var source = new FakeAvatarSource(owner);
        var flow = new AvatarUpdateFlow(source);
        bool localShown = false;
        var success = await flow.SaveAsync(owner, new byte[] { 1, 2 }, "photo.png", _ => localShown = true);
        check(localShown && success.CloudConfirmed && source.Events.SequenceEqual(new[] { "local", "upload", "confirm", "mark" }),
            "a photo is locally available before upload and becomes confirmed only after the profile acknowledgement");
        source = new(owner) { UploadFails = true }; flow = new(source);
        var localOnly = await flow.SaveAsync(owner, new byte[] { 1 }, "photo.jpg");
        check(!localOnly.CloudConfirmed && source.Events.SequenceEqual(new[] { "local", "upload" }),
            "a failed upload retains a local photo without writing or confirming a cloud profile");
        source.UploadFails = false;
        var retry = await flow.SyncAsync(owner, localOnly.LocalPath);
        check(retry.CloudConfirmed && source.Events.Count(item => item == "local") == 1,
            "retry can sync the existing pending photo without selecting or copying it again");
        source = new(owner) { ConfirmFails = true }; flow = new(source);
        var failedProfile = await flow.SaveAsync(owner, new byte[] { 1 }, "photo.jpg");
        check(!failedProfile.CloudConfirmed && !source.Events.Contains("mark"),
            "storage upload alone cannot claim that the account profile was updated");
        source = new(owner) { Url = "file:///private/photo.jpg" }; flow = new(source);
        var badUrl = await flow.SaveAsync(owner, new byte[] { 1 }, "photo.jpg");
        check(!badUrl.CloudConfirmed && !source.Events.Contains("confirm"), "a local path cannot be written as the cloud avatar URL");
        source = new(owner) { Timeout = true }; flow = new(source);
        check(!(await flow.SaveAsync(owner, new byte[] { 1 }, "photo.jpg")).CloudConfirmed,
            "an upload timeout leaves an honest local-only retry state");
        source = new(owner) { LocalFails = true }; flow = new(source);
        bool diskFailed = false;
        try { await flow.SaveAsync(owner, new byte[] { 1 }, "photo.jpg"); } catch (IOException) { diskFailed = true; }
        check(diskFailed && !source.Events.Contains("upload"), "local storage failure cannot proceed to a claimed photo save");
        source = new(owner) { UploadGate = new(TaskCreationOptions.RunContinuationsAsynchronously) }; flow = new(source);
        var pending = flow.SaveAsync(owner, new byte[] { 1 }, "photo.jpg");
        source.Current = owner with { Revision = 2 };
        source.UploadGate.SetResult();
        bool stale = false;
        try { await pending; } catch (OperationCanceledException) { stale = true; }
        check(stale && !source.Events.Contains("confirm"), "an old login's photo upload cannot patch the newer session's profile");
        source = new(owner) { ChangeDuringConfirm = true }; flow = new(source);
        bool changed = false;
        try { await flow.SaveAsync(owner, new byte[] { 1 }, "photo.jpg"); } catch (OperationCanceledException) { changed = true; }
        check(changed && !source.Events.Contains("mark"), "a late profile acknowledgement cannot mark another account's photo confirmed");
        source = new(owner); flow = new(source);
        bool empty = false;
        try { await flow.SaveAsync(owner, Array.Empty<byte>(), "photo.jpg"); } catch (InvalidOperationException) { empty = true; }
        check(empty && source.Events.Count == 0, "an empty selection makes no local or cloud change");
    }

    private sealed class FakeAvatarSource(ProfileAccountScope owner) : IAvatarUpdateSource
    {
        public ProfileAccountScope Current = owner;
        public List<string> Events = new();
        public bool UploadFails, ConfirmFails, LocalFails, Timeout, ChangeDuringConfirm;
        public string Url = "https://project-a.test/avatars/resident-a-photo.jpg";
        public TaskCompletionSource? UploadGate;
        public void RequireCurrent(ProfileAccountScope expected) { if (expected != Current) throw new OperationCanceledException(); }
        public Task<string> SaveLocalAsync(ProfileAccountScope expected, byte[] image, string extension)
        {
            Events.Add("local");
            if (LocalFails) throw new IOException("test disk failure");
            return Task.FromResult("/account-scoped/photo" + extension);
        }
        public async Task<string> UploadAsync(ProfileAccountScope expected, string localPath)
        {
            Events.Add("upload");
            if (UploadGate is not null) await UploadGate.Task;
            if (Timeout) throw new TaskCanceledException();
            if (UploadFails) throw new IOException("test upload failure");
            return Url;
        }
        public Task ConfirmAsync(ProfileAccountScope expected, string url)
        {
            Events.Add("confirm");
            if (ChangeDuringConfirm) Current = Current with { UserId = "other" };
            if (ConfirmFails) throw new IOException("test profile failure");
            return Task.CompletedTask;
        }
        public void MarkConfirmed(ProfileAccountScope expected, string path, string url) => Events.Add("mark");
    }
}
