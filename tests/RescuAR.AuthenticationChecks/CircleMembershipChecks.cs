using System.Net;
using Newtonsoft.Json;
using RescuAR.App.Services.SafetyCircle;

internal static class CircleMembershipChecks
{
    private static readonly CircleMembership Owned = new("owned", "Household", "ABCD23", "resident-a", DateTime.UnixEpoch);
    private static readonly CircleMembership Joined = new("joined", "Neighbors", "EFGH45", "resident-b", DateTime.UnixEpoch.AddDays(1));

    public static async Task RunAsync(Action<bool, string> check)
    {
        var rig = new Rig();
        var scope = rig.Context.Current;
        rig.Context.Online = false;
        var result = await rig.Service.LoadAsync(scope);
        check(result.IsCached && result.Circles.Count == 0 && result.SelectedId == "", "offline first use cannot fabricate a Circle membership");
        check(rig.Remote.Reads == 0 && rig.Store.Writes == 0, "offline Circle viewing performs no remote request or invented cache write");
        await Reject<CircleMembershipOfflineException>(() => rig.Service.CreateAsync(scope, "Household"), check, "offline create cannot claim local membership success");
        await Reject<CircleMembershipOfflineException>(() => rig.Service.JoinAsync(scope, "ABCD23"), check, "offline join cannot claim local membership success");
        check(rig.Remote.Creates == 0 && rig.Remote.Joins == 0 && rig.Store.State is null, "offline membership changes never reach the remote or save a Circle");

        rig.Context.Online = true;
        await Reject<ArgumentException>(() => rig.Service.CreateAsync(scope, "  "), check, "blank Circle names are rejected before creating a record");
        await Reject<ArgumentException>(() => rig.Service.CreateAsync(scope, new string('n', 61)), check, "overlong Circle names are rejected before creating a record");
        await Reject<ArgumentException>(() => rig.Service.JoinAsync(scope, "abc?23"), check, "invite codes reject punctuation");
        await Reject<ArgumentException>(() => rig.Service.JoinAsync(scope, "éBCD23"), check, "invite codes reject non-ASCII lookalikes");
        await Reject<ArgumentException>(() => rig.Service.JoinAsync(scope, "ABC"), check, "incomplete invite codes do not query the service");
        check(rig.Remote.Creates == 0 && rig.Remote.Joins == 0, "invalid form input causes no membership write");

        rig.Remote.Create = (_, name) => throw new HttpRequestException("response lost");
        await Reject<HttpRequestException>(() => rig.Service.CreateAsync(scope, "Household"), check, "unconfirmed create responses remain failures");
        check(rig.Store.State is null && rig.Store.Writes == 0, "a failed create does not persist a local Circle or selected ID");
        rig.Remote.Create = (_, name) => Task.FromResult(Owned with { CreatedBy = "resident-b" });
        await Reject<UnauthorizedAccessException>(() => rig.Service.CreateAsync(scope, "Household"), check, "create must confirm the verified account as creator");
        rig.Remote.Create = (_, name) => Task.FromResult(Owned with { Id = "" });
        await Reject<InvalidDataException>(() => rig.Service.CreateAsync(scope, "Household"), check, "an incomplete create result cannot become a membership");

        rig.Remote.Create = (_, name) =>
        {
            check(rig.Store.Writes == 0, "Circle storage waits until the remote confirms creation");
            check(name == "Household", "Circle creation trims the confirmed name");
            return Task.FromResult(Owned);
        };
        result = await rig.Service.CreateAsync(scope, " Household ");
        check(!result.IsCached && result.CacheSaved && result.Circles.Single() == Owned && result.SelectedId == Owned.Id, "confirmed creation publishes and saves the actual Circle");
        check(rig.Remote.LastScope == scope && rig.Store.State?.UserId == scope.UserId, "membership operations use the captured signed-in identity");

        rig.Remote.Join = (_, code) => throw new UnauthorizedAccessException("join denied");
        var before = JsonConvert.SerializeObject(rig.Store.State); int writes = rig.Store.Writes;
        await Reject<UnauthorizedAccessException>(() => rig.Service.JoinAsync(scope, "efgh45"), check, "denied membership is not converted into local join success");
        check(JsonConvert.SerializeObject(rig.Store.State) == before && rig.Store.Writes == writes, "a denied join preserves the previously confirmed membership list");
        rig.Remote.Join = (_, code) =>
        {
            check(code == "EFGH45", "joining normalizes a valid invite code without changing its meaning");
            return Task.FromResult(Joined);
        };
        result = await rig.Service.JoinAsync(scope, " efgh45 ");
        check(result.Circles.Count == 2 && result.SelectedId == Joined.Id, "confirmed join retains other saved memberships and selects the joined Circle");
        rig.Remote.Join = (_, _) => Task.FromResult(Joined);
        result = await rig.Service.JoinAsync(scope, "EFGH45");
        check(result.Circles.Count == 2, "repeat confirmation of the same membership does not duplicate the list");

        rig.Context.Online = false;
        result = await rig.Service.LoadAsync(scope);
        check(result.IsCached && result.Circles.Count == 2 && result.Notice.Contains("not been checked"), "offline saved memberships are explicitly unverified cached data");
        result = await rig.Service.SelectAsync(scope, Owned.Id);
        check(result.IsCached && result.SelectedId == Owned.Id && result.Notice.Contains("unverified"), "offline selection keeps the cached membership warning");
        await Reject<UnauthorizedAccessException>(() => rig.Service.SelectAsync(scope, "unknown"), check, "offline selection cannot invent access to an unknown Circle");
        await Reject<CircleMembershipOfflineException>(() => rig.Service.RenameAsync(scope, Owned.Id, "Renamed"), check, "offline creators cannot rename using a cached role");
        await Reject<CircleMembershipOfflineException>(() => rig.Service.RemoveAsync(scope, Owned.Id, true), check, "offline creators cannot delete using a cached role");
        await Reject<CircleMembershipOfflineException>(() => rig.Service.RemoveAsync(scope, Joined.Id, false), check, "offline members cannot queue a pretend Leave success");

        rig.Context.Online = true;
        rig.Remote.Circles = new() { Owned, Joined };
        rig.Remote.Read = _ => throw new HttpRequestException("no connection");
        result = await rig.Service.LoadAsync(scope);
        check(result.IsCached && result.Circles.Count == 2, "a transport failure may display only previously confirmed memberships");
        rig.Remote.Read = _ => throw new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden);
        await Reject<HttpRequestException>(() => rig.Service.LoadAsync(scope), check, "a cloud access denial is not disguised as successful offline membership");
        rig.Remote.Read = _ => throw new UnauthorizedAccessException("expired identity");
        await Reject<UnauthorizedAccessException>(() => rig.Service.LoadAsync(scope), check, "an invalid sign-in remains a visible failure despite a saved Circle list");
        rig.Remote.Read = null;
        rig.Remote.Circles = new() { Joined };
        result = await rig.Service.LoadAsync(scope);
        check(!result.IsCached && result.Circles.SequenceEqual(new[] { Joined }) && result.SelectedId == Joined.Id, "online refresh removes revoked membership and replaces its selected ID");
        rig.Remote.Circles.Clear();
        result = await rig.Service.LoadAsync(scope);
        check(result.Circles.Count == 0 && result.SelectedId == "" && rig.Store.State?.Circles.Count == 0, "an online empty list clears stale Circle selection rather than reviving a legacy archive");
        await Reject<UnauthorizedAccessException>(() => rig.Service.SelectAsync(scope, Owned.Id), check, "online selection rechecks membership rather than trusting an old list");

        rig = new Rig(); scope = rig.Context.Current;
        rig.Remote.Circles = new() { Owned, Joined };
        await rig.Service.LoadAsync(scope);
        await Reject<UnauthorizedAccessException>(() => rig.Service.RenameAsync(scope, Joined.Id, "Changed"), check, "a member cannot rename someone else's Circle");
        await Reject<UnauthorizedAccessException>(() => rig.Service.RemoveAsync(scope, Joined.Id, true), check, "a member cannot delete a Circle for everyone");
        await Reject<InvalidOperationException>(() => rig.Service.RemoveAsync(scope, Owned.Id, false), check, "creator Leave cannot accidentally invoke Delete");
        check(rig.Remote.Renames == 0 && rig.Remote.Deletes == 0 && rig.Remote.Leaves == 0, "role failures perform no destructive remote action");
        rig.Remote.Circles[0] = Owned with { CreatedBy = "resident-b" };
        await Reject<UnauthorizedAccessException>(() => rig.Service.RenameAsync(scope, Owned.Id, "Changed"), check, "a cached creator role is rechecked online before rename");
        await Reject<UnauthorizedAccessException>(() => rig.Service.RemoveAsync(scope, Owned.Id, true), check, "a cached creator role is rechecked online before deletion");
        rig.Remote.Circles[0] = Owned;
        rig.Remote.Rename = (_, id, name) => Task.FromResult(Owned with { Name = "old name" });
        await Reject<InvalidDataException>(() => rig.Service.RenameAsync(scope, Owned.Id, "Changed"), check, "a rename needs a matching confirmed name and Circle identity");
        rig.Remote.Rename = null;
        result = await rig.Service.RenameAsync(scope, Owned.Id, " Changed ");
        check(result.Circles.Single(circle => circle.Id == Owned.Id).Name == "Changed", "the creator's confirmed rename replaces only the matching Circle metadata");

        before = JsonConvert.SerializeObject(rig.Store.State);
        rig.Remote.Leave = (_, _) => throw new HttpRequestException("Leave response missing");
        await Reject<HttpRequestException>(() => rig.Service.RemoveAsync(scope, Joined.Id, false), check, "unconfirmed Leave does not report local success");
        check(before == JsonConvert.SerializeObject(rig.Store.State), "failed Leave preserves the cached list until a confirmed refresh");
        rig.Remote.Leave = null;
        result = await rig.Service.RemoveAsync(scope, Joined.Id, false);
        check(rig.Remote.LastLeave == (scope, Joined.Id) && result.Circles.All(circle => circle.Id != Joined.Id) && rig.Remote.Deletes == 0, "member Leave is a separate own-membership operation and never calls creator Delete");
        rig.Remote.Delete = (_, _) => throw new InvalidOperationException("database reference blocked deletion");
        await Reject<InvalidOperationException>(() => rig.Service.RemoveAsync(scope, Owned.Id, true), check, "a blocked cloud deletion stays visible rather than hiding the Circle locally");
        check(rig.Store.State?.Circles.Single().Id == Owned.Id, "failed Delete retains the last confirmed Circle record");
        rig.Remote.Delete = null;
        result = await rig.Service.RemoveAsync(scope, Owned.Id, true);
        check(result.Circles.Count == 0 && result.SelectedId == "" && rig.Remote.LastDelete == (scope, Owned.Id), "confirmed creator deletion removes only the confirmed Circle from local membership metadata");

        rig = new Rig(); scope = rig.Context.Current;
        rig.Remote.Create = (_, _) => { rig.Remote.Circles.Add(Owned); throw new InvalidOperationException("created, membership confirmation interrupted"); };
        await Reject<InvalidOperationException>(() => rig.Service.CreateAsync(scope, Owned.Name), check, "partial creation remains an explicit unconfirmed operation");
        check(rig.Store.State is null, "partial creation cannot fabricate a locally joined Circle");
        result = await rig.Service.LoadAsync(scope);
        check(result.Circles.SequenceEqual(new[] { Owned }) && !result.IsCached, "refresh can recover a remotely confirmed Circle after an interrupted create");

        rig = new Rig(); scope = rig.Context.Current;
        rig.Store.FailWrite = true;
        result = await rig.Service.CreateAsync(scope, Owned.Name);
        check(!result.CacheSaved && !result.IsCached && result.Circles.Single() == Owned && result.Notice.Contains("cloud result is confirmed"), "cloud success with a cache failure is distinguished from membership failure");
        check(rig.Store.State is null, "failed local storage never claims the confirmed Circle is saved offline");
        rig.Store.FailWrite = false; await rig.Service.LoadAsync(scope);
        rig.Store.FailWrite = true;
        await Reject<IOException>(() => rig.Service.SelectAsync(scope, Owned.Id), check, "a failed selection write cannot claim that a choice was saved");

        rig = new Rig(); scope = rig.Context.Current;
        var suspended = new TaskCompletionSource<CircleMembership>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Remote.Create = (_, _) => suspended.Task;
        var changing = rig.Service.CreateAsync(scope, Owned.Name);
        var queued = rig.Service.LoadAsync(scope);
        rig.Context.Current = scope with { UserId = "resident-b", Revision = 2 };
        suspended.SetResult(Owned);
        await Reject<OperationCanceledException>(() => changing, check, "a late membership result cannot publish after an account switch");
        await Reject<OperationCanceledException>(() => queued, check, "a request queued behind another operation rechecks the account before reading data");
        check(rig.Store.Writes == 0 && rig.Remote.Reads == 0, "late and queued requests cannot write old results into the next account view");
        await Reject<OperationCanceledException>(() => rig.Service.LoadAsync(scope), check, "a stale session is rejected before accessing a cache");

        rig = new Rig(); scope = rig.Context.Current;
        rig.Remote.Read = _ => { rig.Context.Current = scope with { Server = "https://other.example", Revision = 2 }; return Task.FromResult(new List<CircleMembership> { Owned }); };
        await Reject<OperationCanceledException>(() => rig.Service.LoadAsync(scope), check, "a server change during a response rejects the result before caching");
        check(rig.Store.Writes == 0, "a server switch cannot save a response under the new server");
        rig = new Rig(); scope = rig.Context.Current;
        rig.Remote.Read = _ => { rig.Context.Current = scope with { Revision = 2 }; throw new HttpRequestException("offline after sign-out"); };
        await Reject<OperationCanceledException>(() => rig.Service.LoadAsync(scope), check, "an offline exception cannot revive cached data after the session changes");
        rig.Store.State = new("https://wrong.example", scope.UserId, new() { Owned }, Owned.Id);
        await Reject<IOException>(() => rig.Service.LoadAsync(rig.Context.Current), check, "a mismatched cache envelope cannot supply another server's membership");

        FileChecks(check);
    }

    private static void FileChecks(Action<bool, string> check)
    {
        string repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string fixture = Path.GetFullPath(Path.Combine(repository, "artifacts/circle-unit-fixtures", Guid.NewGuid().ToString("N")));
        if (!Directory.Exists(Path.Combine(repository, "RescuAR.MAUI")) || !fixture.StartsWith(Path.Combine(repository, "artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Circle filesystem checks must stay inside ignored worktree artifacts.");
        var store = new FileCircleMembershipStore(Path.Combine(fixture, "memberships"));
        var scope = new CircleMembershipScope("https://example.test:54321/project-a", "resident-a", 1);
        var account = scope with { UserId = "resident-b" };
        var server = scope with { Server = "https://example.test:54321/project-b" };
        var port = scope with { Server = "https://example.test:54322/project-a" };
        var state = new CircleMembershipState(scope.Server, scope.UserId, new() { Owned, Joined }, Joined.Id);
        store.Write(scope, state);
        check(new FileCircleMembershipStore(Path.Combine(fixture, "memberships")).Read(scope)?.SelectedId == Joined.Id, "a newly opened Circle cache restores the confirmed selected ID");
        check(store.Read(account) is null && store.Read(server) is null && store.Read(port) is null, "Circle files isolate accounts and full server paths/ports");
        check(store.Read(scope with { Revision = 2 })?.Circles.Count == 2, "a later verified session for the same account can view its saved memberships");
        string escape = store.PathFor(scope with { UserId = "../../another-account" });
        check(Path.GetDirectoryName(escape) == Path.GetDirectoryName(store.PathFor(scope)), "account identifiers cannot escape the Circle membership directory");
        bool failed = false;
        try { store.Write(account, state); } catch (UnauthorizedAccessException) { failed = true; }
        check(failed && store.Read(account) is null, "cache writes reject an envelope owned by another account");

        string chat = Path.Combine(fixture, "chat.json"), pending = Path.Combine(fixture, "pending.json"), photo = Path.Combine(fixture, "photo.bin");
        File.WriteAllText(chat, "saved-chat"); File.WriteAllText(pending, "pending-upload"); File.WriteAllText(photo, "saved-photo");
        store.Write(scope, state with { Circles = new(), SelectedId = "" });
        check(File.ReadAllText(chat) == "saved-chat" && File.ReadAllText(pending) == "pending-upload" && File.ReadAllText(photo) == "saved-photo", "revoking Circle cache metadata never deletes chat, pending uploads or photos");
        check(!Directory.EnumerateFiles(Path.GetDirectoryName(store.PathFor(scope))!, "*.tmp").Any(), "a completed atomic cache replacement leaves no temporary account files");
        File.WriteAllText(store.PathFor(scope), "broken json");
        failed = false;
        try { store.Read(scope); } catch (IOException) { failed = true; }
        check(failed && File.ReadAllText(store.PathFor(scope)) == "broken json", "corrupt Circle cache data is reported and preserved instead of silently reset");
        File.WriteAllText(store.PathFor(scope), JsonConvert.SerializeObject(state with { UserId = account.UserId }));
        failed = false;
        try { store.Read(scope); } catch (IOException) { failed = true; }
        check(failed, "a file copied under another account's key still fails envelope ownership validation");
    }

    private static async Task Reject<T>(Func<Task> action, Action<bool, string> check, string message) where T : Exception
    {
        bool rejected = false;
        try { await action(); } catch (T) { rejected = true; }
        check(rejected, message);
    }

    private sealed class Rig
    {
        public FakeContext Context { get; } = new();
        public FakeRemote Remote { get; } = new();
        public FakeStore Store { get; } = new();
        public CircleMembershipService Service { get; }
        public Rig() => Service = new(Context, Remote, Store);
    }

    private sealed class FakeContext : ICircleMembershipContext
    {
        public CircleMembershipScope Current { get; set; } = new("https://server.example", "resident-a", 1);
        public bool Online { get; set; } = true;
        public bool IsOnline => Online;
        public CircleMembershipScope Capture() => Current;
        public void Require(CircleMembershipScope scope) { if (scope != Current) throw new OperationCanceledException("identity changed"); }
    }

    private sealed class FakeStore : ICircleMembershipStore
    {
        public CircleMembershipState? State { get; set; }
        public bool FailWrite { get; set; }
        public int Writes { get; private set; }
        public CircleMembershipState? Read(CircleMembershipScope _) => State;
        public void Write(CircleMembershipScope scope, CircleMembershipState state)
        {
            if (FailWrite) throw new IOException("storage unavailable");
            Writes++; State = state;
        }
    }

    private sealed class FakeRemote : ICircleMembershipRemote
    {
        public List<CircleMembership> Circles { get; set; } = new();
        public Func<CircleMembershipScope, Task<List<CircleMembership>>>? Read { get; set; }
        public Func<CircleMembershipScope, string, Task<CircleMembership>>? Create { get; set; }
        public Func<CircleMembershipScope, string, Task<CircleMembership>>? Join { get; set; }
        public Func<CircleMembershipScope, string, string, Task<CircleMembership>>? Rename { get; set; }
        public Func<CircleMembershipScope, string, Task>? Leave { get; set; }
        public Func<CircleMembershipScope, string, Task>? Delete { get; set; }
        public int Reads, Creates, Joins, Renames, Leaves, Deletes;
        public CircleMembershipScope LastScope;
        public (CircleMembershipScope, string) LastLeave, LastDelete;
        public Task<List<CircleMembership>> GetAsync(CircleMembershipScope scope)
        { Reads++; LastScope = scope; return Read?.Invoke(scope) ?? Task.FromResult(Circles.ToList()); }
        public Task<CircleMembership> CreateAsync(CircleMembershipScope scope, string name)
        {
            Creates++; LastScope = scope;
            if (Create != null) return Create(scope, name);
            var circle = Owned with { CreatedBy = scope.UserId, Name = name }; Circles.Add(circle);
            return Task.FromResult(circle);
        }
        public Task<CircleMembership> JoinAsync(CircleMembershipScope scope, string code)
        { Joins++; LastScope = scope; return Join?.Invoke(scope, code) ?? Task.FromResult(Joined); }
        public Task<CircleMembership> RenameAsync(CircleMembershipScope scope, string id, string name)
        {
            Renames++; LastScope = scope;
            if (Rename != null) return Rename(scope, id, name);
            int at = Circles.FindIndex(circle => circle.Id == id); Circles[at] = Circles[at] with { Name = name };
            return Task.FromResult(Circles[at]);
        }
        public Task LeaveAsync(CircleMembershipScope scope, string id)
        {
            Leaves++; LastLeave = (scope, id);
            if (Leave != null) return Leave(scope, id);
            Circles.RemoveAll(circle => circle.Id == id); return Task.CompletedTask;
        }
        public Task DeleteAsync(CircleMembershipScope scope, string id)
        {
            Deletes++; LastDelete = (scope, id);
            if (Delete != null) return Delete(scope, id);
            Circles.RemoveAll(circle => circle.Id == id); return Task.CompletedTask;
        }
    }
}
