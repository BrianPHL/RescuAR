namespace RescuAR.App.Services.SafetyCircle;

public interface ICirclePresenceRemote
{
    Task<CirclePresenceData> ReadAsync(CircleMembershipScope scope, string circleId, CancellationToken token);
    Task UploadAsync(CircleMembershipScope scope, string circleId, CircleDeviceReading reading, CancellationToken token);
}
public interface ICircleDeviceReader
{
    Task<CircleDeviceReading?> ReadAsync(CancellationToken token);
}
public sealed record CircleMapSnapshot(CircleMembershipSnapshot Memberships, IReadOnlyList<CirclePresence> People, bool PeopleCached, string Notice, string SharingNotice);

public sealed class CircleMapCoordinator(CircleMembershipService memberships, ICirclePresenceRemote remote, ICircleDeviceReader device, Func<DateTimeOffset> clock)
{
    private readonly SemaphoreSlim polling = new(1, 1);
    private CircleMembershipScope? owner;
    private CancellationTokenSource? page;
    private CancellationTokenSource? sharing;
    private long version;
    private CircleMembershipSnapshot? knownMemberships;
    private DateTimeOffset membershipChecked;
    private CirclePresenceData? lastPeople;
    private string peopleCircle = "";
    public bool SharingEnabled => sharing is not null;

    public void Start()
    {
        Stop();
        owner = memberships.Capture();
        page = new();
    }
    public void Stop()
    {
        Interlocked.Increment(ref version);
        SetSharing(false);
        page?.Cancel(); page?.Dispose(); page = null;
        owner = null; knownMemberships = null; lastPeople = null; peopleCircle = "";
    }
    public void SetSharing(bool enabled)
    {
        sharing?.Cancel(); sharing?.Dispose(); sharing = null;
        if (enabled && owner.HasValue && page is not null && knownMemberships is { IsCached: false } confirmed && !string.IsNullOrEmpty(confirmed.SelectedId))
        {
            memberships.Require(owner.Value);
            sharing = CancellationTokenSource.CreateLinkedTokenSource(page.Token);
        }
    }
    private void Require(CircleMembershipScope scope, long request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request != version || owner != scope) throw new OperationCanceledException("Circle screen changed.", token);
        memberships.Require(scope);
    }
    public async Task SelectAsync(string id)
    {
        if (owner is not { } scope) throw new OperationCanceledException();
        Start();
        var token = page!.Token; long request = version;
        await memberships.SelectAsync(scope, id).WaitAsync(token);
        Require(scope, request, token);
    }
    public void RefreshMemberships() => knownMemberships = null;

    public async Task<CircleMapSnapshot?> PollAsync()
    {
        if (owner is not { } scope || page is null) return null;
        var token = page.Token; long request = version;
        if (!await polling.WaitAsync(0, token)) return null;
        try
        {
            Require(scope, request, token);
            if (knownMemberships is null || knownMemberships.IsCached || clock() - membershipChecked >= TimeSpan.FromSeconds(30))
            {
                var loaded = await memberships.LoadAsync(scope).WaitAsync(token);
                Require(scope, request, token);
                knownMemberships = loaded; membershipChecked = clock();
            }
            var memberList = knownMemberships;
            string id = memberList.SelectedId;
            if (id != peopleCircle) { SetSharing(false); lastPeople = null; peopleCircle = id; }
            bool cached = memberList.IsCached;
            string notice = memberList.Notice;
            if (!cached && id.Length > 0)
            {
                try
                {
                    var data = await remote.ReadAsync(scope, id, token);
                    Require(scope, request, token);
                    lastPeople = data;
                    notice = "Last shared positions are shown; they do not establish a member's safety or online status. " + data.Notice;
                }
                catch (Exception error) when (Offline(error))
                {
                    Require(scope, request, token);
                    cached = true;
                    notice = "Connection lost. Previously loaded people/positions are cached and current membership is unverified.";
                }
            }
            Require(scope, request, token);
            string sharingNotice = SharingEnabled ? "Sharing paused — connect and confirm Circle membership." : "Sharing Off. No new positions are uploaded. Previous last-known records remain.";
            var sharingToken = sharing?.Token;
            if (sharingToken.HasValue && !cached && id.Length > 0)
            {
                try
                {
                    var reading = await device.ReadAsync(sharingToken.Value);
                    Require(scope, request, sharingToken.Value);
                    if (CirclePresencePolicy.CanShare(reading, clock()))
                    {
                        await remote.UploadAsync(scope, id, reading!, sharingToken.Value);
                        Require(scope, request, sharingToken.Value);
                        sharingNotice = "Sharing On — position upload confirmed at " + clock().ToLocalTime().ToString("HH:mm:ss") + ".";
                    }
                    else sharingNotice = "Sharing paused — a recent, accurate location is unavailable. No substitute position was uploaded.";
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && request == version)
                { sharingNotice = SharingEnabled ? "Sharing paused — the location request did not complete." : "Sharing Off. No new positions are uploaded."; }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    Require(scope, request, token);
                    sharingNotice = "Sharing paused — the location or upload could not be confirmed. Check location permission and connection.";
                }
            }
            Require(scope, request, token);
            var people = lastPeople?.People.Select(person => CirclePresencePolicy.Project(person,
                lastPeople.Positions.FirstOrDefault(position => position.UserId == person.UserId), clock())).ToList() ?? new();
            return new(memberList, people, cached, notice, sharingNotice);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            Require(scope, request, token);
            knownMemberships = null; lastPeople = null; SetSharing(false);
            throw;
        }
        finally { polling.Release(); }
    }
    private static bool Offline(Exception error) => error is CircleMembershipOfflineException or TimeoutException
        || error is HttpRequestException { StatusCode: null };
}
