using Newtonsoft.Json;
using RescuAR.App.Models;
using RescuAR.App.Services.SafetyCircle;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    passed++;
    Console.WriteLine($"PASS: {name}");
}
async Task ExpectFailure(Func<Task> action, string name)
{
    bool failed = false;
    try { await action(); } catch { failed = true; }
    Check(failed, name);
}

var directory = Path.Combine(AppContext.BaseDirectory, "circle-checks-" + Guid.NewGuid().ToString("N"));
var store = new FileCircleStateStore(directory);
var remote = new FakeRemote();
string account = "alice";
var sync = new SafetyCircleSyncService(remote, store, () => account);
var first = await sync.CreateAsync("Family");
Check(first.CreatedBy == "alice", "create uses authenticated account identity");
var second = await sync.CreateAsync("Friends");
await sync.SelectCircleAsync(first.Id);
Check(await sync.GetSelectedCircleIdAsync() == first.Id, "selected circle persists independently of list order");
await sync.MarkTutorialSeenAsync();
Check(await sync.HasSeenTutorialAsync(), "tutorial dismissal persists for this account");
await sync.SetLocalPhotoAsync(first.Id, "device-photo.png");
Check(await sync.GetLocalPhotoAsync(second.Id) == string.Empty, "local circle photos do not bleed between circles");
await ExpectFailure(() => sync.JoinAsync("bad"), "invalid invite code is rejected before network writes");
await ExpectFailure(() => sync.CreateAsync(new string('x', 61)), "circle names enforce the shared length limit");

remote.FailWrites = true;
await ExpectFailure(() => sync.CreateAsync("Offline"), "offline creation does not report local success");
await ExpectFailure(() => sync.JoinAsync("ABC123"), "offline join does not invent a membership");
await ExpectFailure(() => sync.RenameAsync(first.Id, "Changed offline"), "failed rename remains unconfirmed");
await ExpectFailure(() => sync.RemoveAsync(first.Id, true), "failed delete preserves confirmed local circle");
Check(store.Read("alice").Circles.First(c => c.Id == first.Id).Name == "Family", "failed mutations retain the last confirmed name");
Check(store.Read("alice").SelectedCircleId == first.Id, "failed delete retains selected circle");
remote.FailReads = true;
var cached = await sync.GetCirclesAsync();
Check(cached.IsCached && cached.Items.Count == 2, "cloud failure returns explicitly cached circle list");
remote.DenyRead = true;
await ExpectFailure(() => sync.GetCirclesAsync(), "authorization failure cannot be hidden by cached membership success");
remote.DenyRead = false;

var queued = await sync.SendAsync(first.Id, "Need help", null, "Text", "Alice", "avatar-a");
Check(queued.IsPending && queued.DeliveryText.Contains("Pending"), "failed chat delivery is visibly pending");
Check(queued.Message.UserId == "alice", "durable outbox uses the authenticated sender");
var restarted = new SafetyCircleSyncService(remote, new FileCircleStateStore(directory), () => account);
var restored = await restarted.GetMessagesAsync(first.Id);
Check(restored.IsCached && restored.Items.Single().IsPending, "unsent chat survives service restart and offline reload");
remote.FailReads = remote.FailWrites = false;
var delivered = await restarted.GetMessagesAsync(first.Id, true);
Check(!delivered.Items.Single().IsPending, "explicit retry confirms queued delivery");
Check(delivered.Items.Single().Message.Id == queued.Message.Id, "retry preserves the original client message identity");
await restarted.GetMessagesAsync(first.Id, true);
Check(remote.Messages.Count == 1, "repeated retry does not duplicate cloud messages");

remote.LoseNextSendResponse = true;
var uncertain = await restarted.SendAsync(first.Id, "Response lost", null, "Text", "Alice", "avatar-a");
Check(uncertain.IsPending, "lost response leaves delivery pending until confirmation");
var reconciled = await restarted.GetMessagesAsync(first.Id, true);
Check(reconciled.Items.Count == 2 && reconciled.Items.All(m => !m.IsPending), "cloud reconciliation acknowledges a previously uncertain write");
Check(remote.Messages.Count == 2, "response-loss reconciliation avoids duplicate delivery");
remote.Messages[0].MessageText = "Corrected by server";
remote.Messages[0].SenderAvatarUrl = "new-avatar";
var updated = await restarted.GetMessagesAsync(first.Id);
Check(updated.Items.Any(m => m.Message.MessageText == "Corrected by server" && m.Message.SenderAvatarUrl == "new-avatar"),
    "same-count chat refresh accepts changed content and avatars");
remote.Messages.RemoveAt(0);
Check((await restarted.GetMessagesAsync(first.Id)).Items.Count == 1, "successful remote history removes deleted cached messages");

account = "bob";
remote.FailReads = true;
Check((await restarted.GetCirclesAsync()).Items.Count == 0, "another account cannot see cached circles");
Check(!await restarted.HasSeenTutorialAsync(), "tutorial state is account-specific");
Check(await restarted.GetLocalPhotoAsync(first.Id) == string.Empty, "another account cannot see local circle photos");
await ExpectFailure(() => restarted.GetMessagesAsync(first.Id), "another account cannot access a previous account's outbox");
account = string.Empty;
await ExpectFailure(() => restarted.GetCirclesAsync(), "signed-out identity cannot fall back to cached or synthetic identity");
account = "alice";
remote.FailReads = false;
remote.BeforeRead = () => account = "bob";
await ExpectFailure(() => restarted.GetCirclesAsync(), "account change during network read rejects the old response");
Check(store.Read("bob").Circles.Count == 0, "in-flight account change cannot persist Alice's data into Bob's cache");
remote.BeforeRead = null;
account = "alice";
await Task.WhenAll(restarted.SendAsync(first.Id, "One", null, "Text", "Alice", ""),
    restarted.SendAsync(first.Id, "Two", null, "Text", "Alice", ""));
Check((await restarted.GetMessagesAsync(first.Id)).Items.Count == 3, "concurrent sends retain both messages without cache lost updates");
remote.Circles.RemoveAll(c => c.Id == first.Id);
var pruned = await restarted.GetCirclesAsync();
Check(pruned.Items.Count == 1 && await restarted.GetSelectedCircleIdAsync() == second.Id, "removed membership reselects a remaining circle");
Check(!store.Read("alice").Messages.ContainsKey(first.Id) && !store.Read("alice").LocalPhotos.ContainsKey(first.Id),
    "removed membership prunes private chat and photo references");

var memberCircle = new SupabaseSafetyCircle { Id = "joined", Name = "Joined", InviteCode = "ABC123", CreatedBy = "bob" };
remote.Circles.Add(memberCircle);
await restarted.GetCirclesAsync();
await ExpectFailure(() => restarted.RenameAsync(memberCircle.Id, "No"), "non-creator cannot rename a circle");
await ExpectFailure(() => restarted.RemoveAsync(memberCircle.Id, true), "non-creator cannot delete a circle");
await restarted.RemoveAsync(memberCircle.Id, false);
Check(!store.Read("alice").Circles.Any(c => c.Id == memberCircle.Id), "confirmed leave removes membership from cache");
await ExpectFailure(() => restarted.RemoveAsync(second.Id, false), "creator cannot orphan a circle by leaving");
await restarted.RemoveAsync(second.Id, true);
Check(store.Read("alice").Circles.Count == 0, "confirmed owner deletion removes circle from cache");

var now = DateTime.UtcNow;
Check(CircleLocationStatus.Describe(null, now, false) == "Location unavailable", "missing coordinates have an explicit unavailable state");
var missing = JsonConvert.DeserializeObject<SupabaseUserLocation>("{\"latitude\":null,\"longitude\":null,\"last_updated\":null}");
Check(!CircleLocationStatus.HasLocation(missing), "nullable backend location fields deserialize without inventing a position");
var malformed = new SupabaseUserLocation { Latitude = double.NaN, Longitude = 121, LastUpdated = now };
Check(!CircleLocationStatus.HasLocation(malformed), "invalid coordinates cannot create a map marker");
var fresh = new SupabaseUserLocation { Latitude = 0, Longitude = 0, LastUpdated = now, StatusText = "Location shared|0%" };
Check(CircleLocationStatus.HasLocation(fresh), "valid zero latitude or longitude is not treated as missing");
Check(CircleLocationStatus.IsFresh(fresh, now), "recent timestamp is required for a current location state");
Check(CircleLocationStatus.Describe(fresh, now, true).StartsWith("Last known"), "offline cached position is labelled last known even with a recent timestamp");
Check(CircleLocationStatus.BatteryText(fresh, now, false) == "0%", "real zero-percent battery is retained");
fresh.StatusText = "Location shared";
Check(CircleLocationStatus.BatteryText(fresh, now, false) == string.Empty, "missing battery never becomes fabricated 100 percent");
fresh.LastUpdated = now.AddMinutes(-3);
Check(!CircleLocationStatus.IsFresh(fresh, now) && CircleLocationStatus.Describe(fresh, now, false).StartsWith("Last known"),
    "old position is labelled last known rather than online");
fresh.LastUpdated = now.AddHours(1);
Check(!CircleLocationStatus.IsFresh(fresh, now), "implausible future timestamp cannot claim current location");
fresh.LastUpdated = default;
Check(!CircleLocationStatus.HasLocation(fresh), "position without a timestamp is unavailable");
Check(!CircleLocationStatus.ValidCoordinates(double.PositiveInfinity, 121), "infinite coordinates are rejected");
Console.WriteLine($"{passed} Safety Circle checks passed.");

sealed class FakeRemote : ISafetyCircleRemote
{
    public List<SupabaseSafetyCircle> Circles { get; } = new();
    public List<SupabaseCircleMessage> Messages { get; } = new();
    public bool FailReads { get; set; }
    public bool DenyRead { get; set; }
    public bool FailWrites { get; set; }
    public bool LoseNextSendResponse { get; set; }
    public Action? BeforeRead { get; set; }
    private void Read()
    {
        BeforeRead?.Invoke();
        if (DenyRead) throw new UnauthorizedAccessException("Access denied");
        if (FailReads) throw new IOException("Offline");
    }
    private void Write() { if (FailWrites) throw new IOException("Offline"); }
    private static T Copy<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value))!;
    public Task<List<SupabaseSafetyCircle>> GetCirclesAsync(string user) { Read(); return Task.FromResult(Copy(Circles)); }
    public Task<SupabaseSafetyCircle> CreateAsync(string user, string name)
    {
        Write(); var circle = new SupabaseSafetyCircle { Id = Guid.NewGuid().ToString(), Name = name, CreatedBy = user, InviteCode = "ABC123" };
        Circles.Add(circle); return Task.FromResult(Copy(circle));
    }
    public Task<SupabaseSafetyCircle> JoinAsync(string user, string code) { Write(); return Task.FromResult(Copy(Circles.First(c => c.InviteCode == code))); }
    public Task<SupabaseSafetyCircle> RenameAsync(string user, string id, string name)
    { Write(); var circle = Circles.First(c => c.Id == id); circle.Name = name; return Task.FromResult(Copy(circle)); }
    public Task LeaveAsync(string user, string id) { Write(); Circles.RemoveAll(c => c.Id == id); return Task.CompletedTask; }
    public Task DeleteAsync(string user, string id) => LeaveAsync(user, id);
    public Task<List<User>> GetMembersAsync(string user, string id) { Read(); return Task.FromResult(new List<User> { new() { Id = user } }); }
    public Task<List<SupabaseUserLocation>> GetLocationsAsync(string user, string id) { Read(); return Task.FromResult(new List<SupabaseUserLocation>()); }
    public Task PushLocationAsync(string user, SupabaseUserLocation location) { Write(); return Task.CompletedTask; }
    public Task<List<SupabaseCircleMessage>> GetMessagesAsync(string user, string id) { Read(); return Task.FromResult(Copy(Messages.Where(m => m.CircleId == id).ToList())); }
    public Task<SupabaseCircleMessage> SendAsync(string user, SupabaseCircleMessage message)
    {
        Write();
        if (!Messages.Any(m => m.Id == message.Id)) Messages.Add(Copy(message));
        if (LoseNextSendResponse) { LoseNextSendResponse = false; throw new IOException("Response lost"); }
        return Task.FromResult(Copy(Messages.First(m => m.Id == message.Id)));
    }
}
