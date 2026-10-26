namespace RescuAR.App.Services.SafetyCircle;

public readonly record struct CircleMembershipScope(string Server, string UserId, long Revision);
public sealed record CircleMembership(string Id, string Name, string InviteCode, string CreatedBy, DateTime CreatedAt);
public sealed record CircleMembershipState(string Server, string UserId, List<CircleMembership> Circles, string SelectedId);
public sealed record CircleMembershipSnapshot(IReadOnlyList<CircleMembership> Circles, string SelectedId, bool IsCached, bool CacheSaved, string Notice);
public interface ICircleMembershipContext
{
    CircleMembershipScope Capture();
    void Require(CircleMembershipScope scope);
    bool IsOnline { get; }
}
public interface ICircleMembershipStore
{
    CircleMembershipState? Read(CircleMembershipScope scope);
    void Write(CircleMembershipScope scope, CircleMembershipState state);
}
public interface ICircleMembershipRemote
{
    Task<List<CircleMembership>> GetAsync(CircleMembershipScope scope);
    Task<CircleMembership> CreateAsync(CircleMembershipScope scope, string name);
    Task<CircleMembership> JoinAsync(CircleMembershipScope scope, string code);
    Task<CircleMembership> RenameAsync(CircleMembershipScope scope, string id, string name);
    Task LeaveAsync(CircleMembershipScope scope, string id);
    Task DeleteAsync(CircleMembershipScope scope, string id);
}
public sealed class CircleMembershipOfflineException(string message) : IOException(message);

public sealed class CircleMembershipService(ICircleMembershipContext context, ICircleMembershipRemote remote, ICircleMembershipStore store)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public CircleMembershipScope Capture() => context.Capture();
    public void Require(CircleMembershipScope scope) => context.Require(scope);
    private async Task<T> WithState<T>(CircleMembershipScope scope, Func<CircleMembershipState, Task<T>> action)
    {
        context.Require(scope);
        await gate.WaitAsync();
        try
        {
            context.Require(scope);
            var state = store.Read(scope) ?? new(scope.Server, scope.UserId, new(), "");
            if (state.Server != scope.Server || state.UserId != scope.UserId || state.Circles is null)
                throw new IOException("The saved Circle list does not belong to this account and server.");
            Validate(state.Circles);
            context.Require(scope);
            return await action(state with { Circles = state.Circles.ToList() });
        }
        finally { gate.Release(); }
    }
    public Task<CircleMembershipSnapshot> LoadAsync(CircleMembershipScope scope) => WithState(scope, state => Load(scope, state));
    private async Task<CircleMembershipSnapshot> Load(CircleMembershipScope scope, CircleMembershipState state)
    {
        if (!context.IsOnline) return Cached(state);
        List<CircleMembership> circles;
        try { circles = await Fresh(scope); }
        catch (Exception error) when (IsOffline(error)) { context.Require(scope); return Cached(state); }
        context.Require(scope);
        var current = Replace(state, circles);
        return SaveConfirmed(scope, current, "Membership checked online.");
    }
    private static bool IsOffline(Exception error) => error is CircleMembershipOfflineException or TimeoutException or TaskCanceledException
        || error is HttpRequestException { StatusCode: null };
    private static CircleMembershipSnapshot Cached(CircleMembershipState state) => new(state.Circles.AsReadOnly(),
        state.Circles.Any(circle => circle.Id == state.SelectedId) ? state.SelectedId : state.Circles.FirstOrDefault()?.Id ?? "", true, true,
        state.Circles.Count > 0 ? "Saved Circle list — offline. Current membership has not been checked. Connect before changing a Circle." : "Offline. No previously confirmed Circles are saved for this account.");
    private async Task<List<CircleMembership>> Fresh(CircleMembershipScope scope)
    {
        context.Require(scope);
        if (!context.IsOnline) throw new CircleMembershipOfflineException("Connect before changing Circle membership.");
        var circles = await remote.GetAsync(scope);
        context.Require(scope);
        Validate(circles);
        return circles.DistinctBy(circle => circle.Id).OrderBy(circle => circle.CreatedAt).ToList();
    }
    private static void Validate(IEnumerable<CircleMembership> circles)
    {
        if (circles.Any(circle => circle is null || string.IsNullOrWhiteSpace(circle.Id) || string.IsNullOrWhiteSpace(circle.Name)))
            throw new InvalidDataException("The Circle service returned an incomplete list. Refresh before continuing.");
    }
    private static CircleMembershipState Replace(CircleMembershipState state, List<CircleMembership> circles) => state with
    {
        Circles = circles,
        SelectedId = circles.Any(circle => circle.Id == state.SelectedId) ? state.SelectedId : circles.FirstOrDefault()?.Id ?? ""
    };
    private CircleMembershipSnapshot SaveConfirmed(CircleMembershipScope scope, CircleMembershipState state, string notice)
    {
        context.Require(scope);
        bool saved = true;
        try { store.Write(scope, state); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { saved = false; notice += " The cloud result is confirmed, but the local list/selection could not be saved. Refresh when storage is available."; }
        context.Require(scope);
        return new(state.Circles.AsReadOnly(), state.SelectedId, false, saved, notice);
    }
    public Task<CircleMembershipSnapshot> SelectAsync(CircleMembershipScope scope, string id) => WithState(scope, async state =>
    {
        var loaded = await Load(scope, state);
        context.Require(scope);
        if (!loaded.Circles.Any(circle => circle.Id == id)) throw new UnauthorizedAccessException("Select a Circle whose membership has been confirmed for this account.");
        var selected = new CircleMembershipState(scope.Server, scope.UserId, loaded.Circles.ToList(), id);
        store.Write(scope, selected);
        context.Require(scope);
        return loaded with { SelectedId = id, CacheSaved = true, Notice = loaded.IsCached ? "Selected a saved Circle offline. Current membership remains unverified." : "Active Circle selection saved." };
    });
    private static string Name(string value)
    {
        value = (value ?? "").Trim();
        if (value.Length is < 1 or > 60) throw new ArgumentException("Use a Circle name of 1–60 characters.");
        return value;
    }
    public Task<CircleMembershipSnapshot> CreateAsync(CircleMembershipScope scope, string name) => Change(scope, user => remote.CreateAsync(user, Name(name)), true);
    public Task<CircleMembershipSnapshot> JoinAsync(CircleMembershipScope scope, string code)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (code.Length != 6 || code.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("Enter a six-character invite code.");
        return Change(scope, user => remote.JoinAsync(user, code), false);
    }
    private Task<CircleMembershipSnapshot> Change(CircleMembershipScope scope, Func<CircleMembershipScope, Task<CircleMembership>> action, bool creation) => WithState(scope, async state =>
    {
        if (!context.IsOnline) throw new CircleMembershipOfflineException("Connect to create or join a Circle. No membership has been created on this device.");
        var circle = await action(scope);
        context.Require(scope);
        Validate(new[] { circle });
        if (creation && circle.CreatedBy != scope.UserId) throw new UnauthorizedAccessException("Circle creation did not confirm this account as creator.");
        var circles = state.Circles.Where(item => item.Id != circle.Id).Append(circle).ToList();
        return SaveConfirmed(scope, state with { Circles = circles, SelectedId = circle.Id }, creation ? "Circle creation and membership confirmed." : "Circle membership confirmed.");
    });
    public Task<CircleMembershipSnapshot> RenameAsync(CircleMembershipScope scope, string id, string name) => WithState(scope, async state =>
    {
        string requested = Name(name);
        var circles = await Fresh(scope);
        var circle = circles.FirstOrDefault(item => item.Id == id) ?? throw new UnauthorizedAccessException("This Circle is no longer available to your account.");
        if (circle.CreatedBy != scope.UserId) throw new UnauthorizedAccessException("Only the creator can rename this Circle.");
        var updated = await remote.RenameAsync(scope, id, requested);
        context.Require(scope);
        if (updated.Id != id || updated.CreatedBy != scope.UserId || updated.Name != requested)
            throw new InvalidDataException("The Circle rename was not confirmed. Refresh before retrying.");
        circles[circles.FindIndex(item => item.Id == id)] = updated;
        return SaveConfirmed(scope, Replace(state, circles), "Circle rename confirmed.");
    });
    public Task<CircleMembershipSnapshot> RemoveAsync(CircleMembershipScope scope, string id, bool delete) => WithState(scope, async state =>
    {
        var circles = await Fresh(scope);
        var circle = circles.FirstOrDefault(item => item.Id == id) ?? throw new UnauthorizedAccessException("This Circle is no longer available to your account.");
        bool creator = circle.CreatedBy == scope.UserId;
        if (delete && !creator) throw new UnauthorizedAccessException("Only the creator can delete this Circle for everyone.");
        if (!delete && creator) throw new InvalidOperationException("The creator must use Delete for everyone rather than Leave.");
        if (delete) await remote.DeleteAsync(scope, id); else await remote.LeaveAsync(scope, id);
        context.Require(scope);
        circles.RemoveAll(item => item.Id == id);
        return SaveConfirmed(scope, Replace(state, circles), delete ? "Circle deletion confirmed." : "Leaving the Circle was confirmed. Other members keep their Circle.");
    });
}
