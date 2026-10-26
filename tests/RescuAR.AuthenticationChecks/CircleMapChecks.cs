using RescuAR.App.Services.SafetyCircle;

internal static class CircleMapChecks
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly CirclePerson Person = new("resident-a", "Test Resident", "+639001234567", "https://example.test/avatar.png");
    private static readonly CirclePosition Position = new(Person.UserId, 14.65, 121.1, Now.AddSeconds(-10), "Shared|45%⚡");
    private static readonly CircleDeviceReading Reading = new(14.65, 121.1, Now.AddSeconds(-2), 12, false, 40, false);

    public static async Task RunAsync(Action<bool, string> check)
    {
        check(CirclePresencePolicy.CanDisplayLocal(Reading, Now), "a fresh local device fix may be displayed independently of cloud sharing");
        check(!CirclePresencePolicy.CanDisplayLocal(null, Now), "missing local GPS never becomes a placeholder self pin");
        check(!CirclePresencePolicy.CanDisplayLocal(Reading with { RecordedAt = Now.AddSeconds(-16) }, Now), "an expired device fix is removed from the current local marker");
        check(!CirclePresencePolicy.CanDisplayLocal(Reading with { Accuracy = 51 }, Now), "coarse local readings cannot appear as a precise current self pin");
        check(!CirclePresencePolicy.CanDisplayLocal(Reading with { RecordedAt = Now.AddSeconds(6) }, Now), "future local fixes cannot qualify as current");
        check(!CirclePresencePolicy.CanDisplayLocal(Reading with { IsMock = true }, Now), "mock locations cannot become current local self pins");
        check(CirclePresencePolicy.CanShare(Reading, Now), "a recent accurate Circle position may be shared after explicit consent");
        check(!CirclePresencePolicy.CanShare(null, Now), "missing GPS cannot be replaced with a default Circle position");
        check(!CirclePresencePolicy.CanShare(Reading with { RecordedAt = Now.AddSeconds(-31) }, Now), "an old device fix is never uploaded as a new Circle position");
        check(!CirclePresencePolicy.CanShare(Reading with { Accuracy = null }, Now) && !CirclePresencePolicy.CanShare(Reading with { Accuracy = 101 }, Now), "unknown or poor GPS accuracy cannot qualify for sharing");
        check(!CirclePresencePolicy.CanShare(Reading with { Accuracy = 0 }, Now) && !CirclePresencePolicy.CanShare(Reading with { Accuracy = double.NaN }, Now), "invalid accuracy cannot qualify as a precise Circle reading");
        check(!CirclePresencePolicy.CanShare(Reading with { IsMock = true }, Now), "mock readings do not become a real shared position");
        check(!CirclePresencePolicy.CanShare(Reading with { RecordedAt = Now.AddSeconds(31) }, Now), "implausibly future device readings cannot be shared");
        check(!CirclePresencePolicy.Coordinates(double.NaN, 121) && !CirclePresencePolicy.Coordinates(14, double.PositiveInfinity), "nonfinite coordinates never enter Circle pin geometry");
        check(!CirclePresencePolicy.Coordinates(91, 121) && !CirclePresencePolicy.Coordinates(14, 181) && !CirclePresencePolicy.Coordinates(0, 0), "invalid and placeholder coordinate pairs cannot become member pins");
        check(CirclePresencePolicy.Coordinates(0, 121) && CirclePresencePolicy.Coordinates(14, 0), "a valid zero latitude or longitude is not mistaken for missing data");
        var projected = CirclePresencePolicy.Project(Person, Position, Now);
        check(projected.Position == Position && projected.IsRecent && projected.BatteryPercent == 45 && projected.IsCharging, "actual dated coordinates and battery telemetry remain associated with their member");
        projected = CirclePresencePolicy.Project(Person, Position with { RecordedAt = Now.AddHours(-3) }, Now);
        check(projected.Position is not null && !projected.IsRecent && projected.BatteryPercent == 45, "old Circle positions remain explicitly last known rather than marked current");
        check(CirclePresencePolicy.Project(Person, null, Now).BatteryPercent is null, "missing member readings cannot imply a full battery");
        check(CirclePresencePolicy.Project(Person, Position with { Status = "Online" }, Now).BatteryPercent is null, "an Online text alone cannot invent battery telemetry");
        check(CirclePresencePolicy.Project(Person, Position with { Status = "Safe|101%" }, Now).BatteryPercent is null, "out-of-range battery values remain unknown");
        check(CirclePresencePolicy.Project(Person, Position with { Status = "Shared|0%" }, Now).BatteryPercent == 0, "an actual empty battery is preserved rather than replaced by a guessed value");
        check(CirclePresencePolicy.Project(Person, Position with { UserId = "resident-b" }, Now).Position is null, "one member cannot inherit another member's coordinates or battery");
        check(CirclePresencePolicy.Project(Person, Position with { RecordedAt = default }, Now).Position is null, "an undated cloud coordinate is not represented as a known member location");
        check(CirclePresencePolicy.Project(Person, Position with { RecordedAt = Now.AddHours(1) }, Now).Position is null, "a future cloud record cannot establish current member presence");
        check(CirclePresencePolicy.Project(Person, Position with { Latitude = 0, Longitude = 0 }, Now).Position is null, "a cloud placeholder never receives an avatar pin");
        check(CirclePresencePolicy.SafeAvatar("file:///another-account/photo.png") == "" && CirclePresencePolicy.SafeAvatar("http://example.test/image") == "", "Circle avatars cannot load an unscoped local path or insecure profile URL");
        check(CirclePresencePolicy.SafeAvatar("https://example.test/image") == "https://example.test/image", "confirmed HTTPS profile photos remain available to avatar rendering");

        var rig = new Rig(); rig.Map.Start();
        var result = (await rig.Map.PollAsync())!;
        check(!rig.Map.SharingEnabled && rig.Device.Reads == 0 && rig.Presence.Uploads == 0, "the upload coordinator defaults to Off and requests neither a sharing GPS fix nor an upload");
        check(result.People.Single().Position == Position && !result.PeopleCached, "member positions can be viewed independently of sharing this device");
        rig.Map.SetSharing(true); result = (await rig.Map.PollAsync())!;
        check(rig.Presence.Uploads == 1 && rig.Device.Reads == 1 && result.SharingNotice.Contains("confirmed"), "explicit sharing reaches the remote only after membership and valid GPS are available");
        check(rig.Presence.LastScope == rig.Context.Current && rig.Presence.LastCircle == "circle-a" && rig.Presence.LastReading == Reading, "sharing retains the verified account, selected Circle and original acquisition timestamp");
        rig.Map.SetSharing(false); await rig.Map.PollAsync();
        check(rig.Presence.Uploads == 1 && rig.Device.Reads == 1, "switching sharing Off stops subsequent sharing GPS requests and uploads");
        rig.Map.SetSharing(true); rig.Device.Value = Reading with { Accuracy = 200 };
        result = (await rig.Map.PollAsync())!;
        check(rig.Presence.Uploads == 1 && result.SharingNotice.Contains("No substitute"), "a poor fix pauses sharing without sending a replacement coordinate");
        rig.Device.Value = Reading with { RecordedAt = Now.AddMinutes(-1) };
        await rig.Map.PollAsync();
        check(rig.Presence.Uploads == 1, "a stale last-known device reading cannot be relabelled as a fresh upload");
        rig.Device.Value = Reading; rig.Presence.FailUpload = true;
        result = (await rig.Map.PollAsync())!;
        check(result.SharingNotice.Contains("could not be confirmed") && !result.SharingNotice.Contains("upload confirmed"), "an upload error is a paused failure rather than successful sharing");
        rig.Presence.FailUpload = false;
        rig.Context.Online = false;
        result = (await rig.Map.PollAsync())!;
        check(result.PeopleCached && result.People.Single().Position == Position && result.Notice.Contains("unverified"), "connection loss labels already-loaded member readings and unverified membership");
        check(rig.Presence.Uploads == 1, "cached member access does not permit new offline location uploads");
        rig.Map.Stop(); rig.Map.Start();
        result = (await rig.Map.PollAsync())!;
        check(result.Memberships.IsCached && result.People.Count == 0 && !rig.Map.SharingEnabled, "a cold offline Circle view exposes only saved membership metadata and no invented roster");
        rig.Map.SetSharing(true);
        check(!rig.Map.SharingEnabled, "an offline cached membership cannot activate sharing");

        rig = new Rig(); rig.Map.Start(); await rig.Map.PollAsync(); rig.Map.SetSharing(true);
        await rig.Map.SelectAsync("circle-b"); result = (await rig.Map.PollAsync())!;
        check(!rig.Map.SharingEnabled && result.Memberships.SelectedId == "circle-b" && rig.Presence.LastCircle == "circle-b", "switching Circle uses the shared saved selection and resets sharing to Off");
        rig.Map.SetSharing(true);
        rig.Membership.Circles.RemoveAll(circle => circle.Id == "circle-b"); rig.Clock = Now.AddSeconds(31);
        result = (await rig.Map.PollAsync())!;
        check(result.Memberships.SelectedId == "circle-a" && !rig.Map.SharingEnabled && rig.Presence.Uploads == 0, "membership revocation cannot transfer sharing consent to an automatically selected Circle");
        rig.Membership.Circles.Clear(); rig.Clock = Now.AddSeconds(62);
        result = (await rig.Map.PollAsync())!;
        check(result.Memberships.SelectedId == "" && result.People.Count == 0, "losing the last membership clears people and selected-Circle data");

        rig = new Rig(); rig.Map.Start(); await rig.Map.PollAsync(); rig.Map.SetSharing(true);
        var suspended = new TaskCompletionSource<CircleDeviceReading?>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Device.Read = _ => suspended.Task;
        var polling = rig.Map.PollAsync(); rig.Map.SetSharing(false); suspended.SetResult(Reading);
        result = (await polling)!;
        check(rig.Presence.Uploads == 0 && result.SharingNotice.Contains("Off"), "a late GPS result cannot upload after sharing is switched Off");
        rig.Map.SetSharing(true);
        suspended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Device.Read = _ => suspended.Task;
        polling = rig.Map.PollAsync(); rig.Map.Stop(); suspended.SetResult(Reading);
        await Canceled(() => polling, check, "page exit cancels a pending GPS result before any upload");
        check(rig.Presence.Uploads == 0 && !rig.Map.SharingEnabled, "closing the Circle map stops location-sharing ownership");
        rig.Map.Start(); rig.Device.Read = null; await rig.Map.PollAsync();
        check(!rig.Map.SharingEnabled && rig.Presence.Uploads == 0, "reopening the screen cannot silently resume prior sharing consent");

        rig = new Rig(); rig.Map.Start();
        var blocked = new TaskCompletionSource<CirclePresenceData>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Presence.Read = (_, _, _) => blocked.Task;
        polling = rig.Map.PollAsync();
        check(await rig.Map.PollAsync() is null, "timer pulses cannot run overlapping Circle data polls");
        rig.Context.Current = rig.Context.Current with { UserId = "resident-b", Revision = 2 };
        blocked.SetResult(new(new[] { Person }, new[] { Position }));
        await Canceled(() => polling, check, "an account change rejects late member and location data");

        rig = new Rig(); rig.Map.Start(); await rig.Map.PollAsync(); rig.Map.SetSharing(true);
        rig.Presence.Read = (_, _, _) => throw new UnauthorizedAccessException("membership revoked");
        bool rejected = false;
        try { await rig.Map.PollAsync(); } catch (UnauthorizedAccessException) { rejected = true; }
        check(rejected && !rig.Map.SharingEnabled && rig.Presence.Uploads == 0, "a revoked/access-denied Circle cannot fall back to cached people or continue sharing");
        rig.Presence.Read = null; rig.Map.Start(); await rig.Map.PollAsync();
        rig.Map.SetSharing(true);
        rig.Device.Read = _ => { rig.Context.Current = rig.Context.Current with { Server = "https://other.example", Revision = 2 }; return Task.FromResult<CircleDeviceReading?>(Reading); };
        await Canceled(() => rig.Map.PollAsync(), check, "a server change during GPS acquisition prevents upload under another backend");
        check(rig.Presence.Uploads == 0, "a stale device callback never starts a cross-server upload");
    }

    private static async Task Canceled(Func<Task> action, Action<bool, string> check, string name)
    {
        bool canceled = false;
        try { await action(); } catch (OperationCanceledException) { canceled = true; }
        check(canceled, name);
    }

    private sealed class Rig
    {
        public Context Context { get; } = new();
        public MembershipRemote Membership { get; } = new();
        public Device Device { get; } = new();
        public PresenceRemote Presence { get; }
        public CircleMapCoordinator Map { get; }
        public DateTimeOffset Clock { get; set; } = Now;
        public Rig()
        {
            Presence = new(Context);
            Map = new(new CircleMembershipService(Context, Membership, new Store()), Presence, Device, () => Clock);
        }
    }
    private sealed class Context : ICircleMembershipContext
    {
        public CircleMembershipScope Current { get; set; } = new("https://server.example", Person.UserId, 1);
        public bool Online { get; set; } = true;
        public bool IsOnline => Online;
        public CircleMembershipScope Capture() => Current;
        public void Require(CircleMembershipScope scope) { if (scope != Current) throw new OperationCanceledException(); }
    }
    private sealed class Store : ICircleMembershipStore
    {
        private CircleMembershipState? state;
        public CircleMembershipState? Read(CircleMembershipScope scope) => state;
        public void Write(CircleMembershipScope scope, CircleMembershipState value) => state = value;
    }
    private sealed class MembershipRemote : ICircleMembershipRemote
    {
        public List<CircleMembership> Circles { get; } = new() { new("circle-a", "Household", "ABCD23", Person.UserId, Now.UtcDateTime), new("circle-b", "Neighbors", "EFGH45", "resident-b", Now.UtcDateTime) };
        public Task<List<CircleMembership>> GetAsync(CircleMembershipScope scope) => Task.FromResult(Circles.ToList());
        public Task<CircleMembership> CreateAsync(CircleMembershipScope scope, string name) => throw new NotSupportedException();
        public Task<CircleMembership> JoinAsync(CircleMembershipScope scope, string code) => throw new NotSupportedException();
        public Task<CircleMembership> RenameAsync(CircleMembershipScope scope, string id, string name) => throw new NotSupportedException();
        public Task LeaveAsync(CircleMembershipScope scope, string id) => throw new NotSupportedException();
        public Task DeleteAsync(CircleMembershipScope scope, string id) => throw new NotSupportedException();
    }
    private sealed class Device : ICircleDeviceReader
    {
        public int Reads;
        public CircleDeviceReading? Value { get; set; } = Reading;
        public Func<CancellationToken, Task<CircleDeviceReading?>>? Read { get; set; }
        public Task<CircleDeviceReading?> ReadAsync(CancellationToken token) { Reads++; return Read?.Invoke(token) ?? Task.FromResult(Value); }
    }
    private sealed class PresenceRemote(Context context) : ICirclePresenceRemote
    {
        public Func<CircleMembershipScope, string, CancellationToken, Task<CirclePresenceData>>? Read { get; set; }
        public int Uploads;
        public bool FailUpload;
        public CircleMembershipScope LastScope;
        public string LastCircle = "";
        public CircleDeviceReading? LastReading;
        public Task<CirclePresenceData> ReadAsync(CircleMembershipScope scope, string circleId, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); context.Require(scope);
            if (!context.Online) throw new CircleMembershipOfflineException("offline");
            LastCircle = circleId;
            return Read?.Invoke(scope, circleId, token) ?? Task.FromResult(new CirclePresenceData(new[] { Person }, new[] { Position }));
        }
        public Task UploadAsync(CircleMembershipScope scope, string circleId, CircleDeviceReading reading, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); context.Require(scope);
            if (FailUpload) throw new IOException("upload failed");
            Uploads++; LastScope = scope; LastCircle = circleId; LastReading = reading;
            return Task.CompletedTask;
        }
    }
}
