using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RescuAR.App.Services.Authentication;
using Supabase.Gotrue;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    passed++;
    Console.WriteLine($"PASS: {name}");
}

if (args.Contains("--evacuation-directory"))
{
    await EvacuationDirectoryChecks.RunAsync(Check);
    DirectoryCacheChecks.Run(Check);
    Console.WriteLine($"{passed} evacuation-directory and production-cache checks passed.");
    return;
}

if (args.Contains("--camera-view-handoff"))
{
    CameraViewHandoffChecks.Run(Check);
    Console.WriteLine($"{passed} Camera presentation handoff checks passed.");
    return;
}

if (args.Contains("--circle-chat"))
{
    await CircleChatChecks.RunAsync(Check);
    ChatPresentationChecks.Run(Check);
    Console.WriteLine($"{passed} Circle chat and presentation checks passed.");
    return;
}

Session Token(string value) => new() { AccessToken = value, RefreshToken = "test-refresh" };
var store = new FakeSecureStore();
var persistence = new SecureSessionPersistence("https://project-a.supabase.co", store);
persistence.SaveSession(Token("first"));
persistence.SaveSession(Token("second"));
await persistence.FlushAsync();
var restarted = new SecureSessionPersistence("https://project-a.supabase.co", store);
await restarted.ReadAsync();
Check(restarted.LoadSession()?.AccessToken == "second", "restart restores the latest saved session");
var otherProject = new SecureSessionPersistence("https://project-b.supabase.co", store);
await otherProject.ReadAsync();
Check(otherProject.LoadSession() is null, "one project's session cannot restore into another project");

store.BlockWrites = new(TaskCreationOptions.RunContinuationsAsynchronously);
persistence.SaveSession(Token("queued"));
persistence.Enabled = false;
persistence.DestroySession();
persistence.SaveSession(Token("late"));
Check(persistence.LoadSession() is null, "sign-out immediately clears the in-memory session");
store.BlockWrites.SetResult();
await persistence.FlushAsync();
await restarted.ReadAsync();
Check(restarted.LoadSession() is null, "queued writes complete before sign-out removal and late saves stay disabled");

store.BlockWrites = null;
store.FailNextWrite = true;
persistence.Enabled = true;
persistence.SaveSession(Token("failed"));
bool writeFailed = false;
try { await persistence.FlushAsync(); } catch (IOException) { writeFailed = true; }
Check(writeFailed, "an explicit persistence flush reports a storage failure");
persistence.DestroySession();
await persistence.FlushAsync();
Check(!store.Values.Any(), "failed token saves cannot prevent subsequent sign-out removal");

store.Values["AuthSession:project-a.supabase.co"] = "broken JSON";
await restarted.ReadAsync();
Check(restarted.LoadSession() is null && !store.Values.Any(), "corrupt stored session data is discarded");
store.FailRead = true;
bool readFailed = false;
try { await restarted.ReadAsync(); } catch (IOException) { readFailed = true; }
Check(readFailed, "storage-read failures remain initialization failures instead of ready clients");
store.FailRead = false;

var preferences = new FakePreferences();
preferences.Values["CurrentUserId"] = "legacy-id";
preferences.Values["current_user_id"] = "legacy-id";
preferences.Values["UserPhone"] = "test-phone";
preferences.Values["EmergencyContact1Rel"] = "test-relationship";
preferences.Values["CustomQuickActionsList_v18"] = "test-shortcuts";
preferences.Values["LocalSafetyCircles"] = "test-circles";
preferences.Values["SelectedCircleId"] = "test-circle";
preferences.Values["RememberMe"] = "unchanged";
preferences.Values["DetailedOfflineMapEnabled"] = "unchanged";
AuthenticationIdentity.ClearActive(preferences, "authenticated-id");
Check(!preferences.SignedIn, "cleanup marks the device signed out");
Check(!preferences.Values.ContainsKey("CurrentUserId") && !preferences.Values.ContainsKey("current_user_id"),
    "cleanup clears both legacy active-user identifier variants");
Check(!preferences.Values.ContainsKey("UserPhone") && !preferences.Values.ContainsKey("EmergencyContact1Rel"),
    "cleanup clears legacy phone and relationship aliases");
Check(!preferences.Values.ContainsKey("LocalSafetyCircles") && !preferences.Values.ContainsKey("SelectedCircleId"),
    "cleanup removes old circle state from the active account view");
var archive = JObject.Parse(preferences.Values.Single(p => p.Key.StartsWith("AuthIdentityArchive:")).Value);
Check((string?)archive["Values"]?["CustomQuickActionsList_v18"] == "test-shortcuts" &&
      (string?)archive["Values"]?["LocalSafetyCircles"] == "test-circles",
    "cleanup archives saved shortcuts and circle details before clearing active keys");
Check((string?)archive["AuthenticatedUserHint"] == "authenticated-id" &&
      (string?)archive["Values"]?["CurrentUserId"] == "legacy-id",
    "legacy archive retains ownership hints without assigning uncertain data to the current account");
Check(preferences.Values["RememberMe"] == "unchanged" && preferences.Values["DetailedOfflineMapEnabled"] == "unchanged",
    "cleanup preserves sign-in preferences and device map settings");
var failingArchive = new FakePreferences { FailArchive = true };
failingArchive.Values["UserPhone"] = "preserve-me";
bool archiveFailed = false;
try { AuthenticationIdentity.ClearActive(failingArchive, null); } catch (IOException) { archiveFailed = true; }
Check(archiveFailed && !failingArchive.SignedIn && failingArchive.Values["UserPhone"] == "preserve-me",
    "failed archiving signs out without discarding the original saved details");
await AuthenticationFlowChecks.RunAsync(Check);
await AuthenticationFormChecks.RunAsync(Check);
await ProfileEditingChecks.RunAsync(Check);
await ContactShortcutChecks.RunAsync(Check);
SettingsChecks.Run(Check);
await CircleMembershipChecks.RunAsync(Check);
await CircleMapChecks.RunAsync(Check);
await CircleChatChecks.RunAsync(Check);
await ResidentReportChecks.RunAsync(Check);
await OfflineMapChecks.RunAsync(Check);
Console.WriteLine($"{passed} authentication, profile, settings, Circle, report and offline-map checks passed.");

sealed class FakeSecureStore : ISecureSessionStore
{
    public Dictionary<string, string> Values { get; } = new();
    public TaskCompletionSource? BlockWrites { get; set; }
    public bool FailNextWrite { get; set; }
    public bool FailRead { get; set; }
    public Task<string?> ReadAsync(string key) => FailRead
        ? throw new IOException("test read failure")
        : Task.FromResult(Values.GetValueOrDefault(key));
    public async Task WriteAsync(string key, string value)
    {
        if (BlockWrites is { } blocked) await blocked.Task;
        if (FailNextWrite) { FailNextWrite = false; throw new IOException("test write failure"); }
        Values[key] = value;
    }
    public void Remove(string key) => Values.Remove(key);
}

sealed class FakePreferences : IIdentityPreferences
{
    public Dictionary<string, string> Values { get; } = new();
    public bool SignedIn { get; private set; } = true;
    public bool FailArchive { get; set; }
    public bool ContainsKey(string key) => Values.ContainsKey(key);
    public string Get(string key) => Values[key];
    public void Set(string key, string value)
    {
        if (FailArchive) throw new IOException("test archive failure");
        Values[key] = value;
    }
    public void Remove(string key) => Values.Remove(key);
    public void MarkSignedOut() => SignedIn = false;
}
