namespace RescuAR.App.Services.Authentication;

internal enum AuthenticationRoute { Onboarding, Login, Address, Permissions, Application, Retry }
internal enum AuthenticationFailureKind { Offline, InvalidSession, Unavailable }
internal sealed class AuthenticationFlowException(AuthenticationFailureKind kind, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public AuthenticationFailureKind Kind { get; } = kind;
}
internal sealed record AccountIdentity(string Id, string Email);
internal sealed record AccountProfile(string UserId, bool HasAddress);
internal sealed record AuthenticationDecision(AuthenticationRoute Route, AccountIdentity? Identity,
    bool IsOffline, long Request, long Revision, string Message = "");

internal interface IAuthenticationFlowSource
{
    bool IsOnline { get; }
    bool HasSignedUp { get; }
    bool HasCompletedPermissions { get; }
    long Revision { get; }
    string? CurrentUserId { get; }
    Task<AccountIdentity?> ReadSavedIdentityAsync(CancellationToken token);
    Task<AccountIdentity?> VerifyIdentityAsync(bool restoring, CancellationToken token);
    Task<AccountProfile> LoadProfileAsync(AccountIdentity identity, CancellationToken token);
    AccountProfile? ReadCachedProfile(AccountIdentity identity);
    void Activate(AccountIdentity identity, bool offline);
    Task InvalidateSessionAsync();
}

internal sealed class AuthenticationFlowCoordinator(IAuthenticationFlowSource source)
{
    private long request;
    private AuthenticationDecision? lastDecision;

    public bool IsCurrent(AuthenticationDecision decision) => decision.Request == Volatile.Read(ref request)
        && decision.Revision == source.Revision
        && (decision.Identity is null || (decision.IsOffline && source.CurrentUserId is null)
            || source.CurrentUserId == decision.Identity.Id);

    public async Task<AuthenticationDecision> StartAsync(CancellationToken token)
    {
        long current = Interlocked.Increment(ref request);
        long savedRevision = source.Revision;
        AccountIdentity? saved = null;
        try
        {
            saved = await source.ReadSavedIdentityAsync(token);
            EnsureRequest(current, token);
            if (savedRevision != source.Revision) throw new OperationCanceledException("Account settings changed during startup.");
            if (saved is null) return Decision(current, source.HasSignedUp ? AuthenticationRoute.Login : AuthenticationRoute.Onboarding);
            if (!source.IsOnline) return Offline(current, saved, token, savedRevision);
            try
            {
                var verified = await source.VerifyIdentityAsync(true, token);
                if (verified is null || verified.Id != saved.Id)
                    throw new AuthenticationFlowException(AuthenticationFailureKind.InvalidSession, "Please sign in again.");
                return await CompleteAsync(current, verified, token);
            }
            catch (AuthenticationFlowException ex) when (ex.Kind == AuthenticationFailureKind.Offline)
            { return Offline(current, saved, token, savedRevision); }
        }
        catch (AuthenticationFlowException ex) when (ex.Kind == AuthenticationFailureKind.InvalidSession)
        {
            EnsureRequest(current, token);
            if (source.Revision != savedRevision || (source.CurrentUserId is { } active && active != saved?.Id))
                throw new OperationCanceledException("A newer account replaced the rejected session.");
            await source.InvalidateSessionAsync();
            EnsureRequest(current, token);
            return Decision(current, AuthenticationRoute.Login);
        }
        catch (AuthenticationFlowException ex)
        { EnsureRequest(current, token); return Decision(current, AuthenticationRoute.Retry, message: ex.Message); }
    }

    public async Task<AuthenticationDecision> CompleteSignInAsync(string expectedUserId, CancellationToken token)
    {
        long current = Interlocked.Increment(ref request);
        try
        {
            var identity = await source.VerifyIdentityAsync(false, token);
            if (identity is null || identity.Id != expectedUserId)
                throw new AuthenticationFlowException(AuthenticationFailureKind.InvalidSession, "Your account changed. Please sign in again.");
            return await CompleteAsync(current, identity, token);
        }
        catch (AuthenticationFlowException ex)
        { EnsureRequest(current, token); return Decision(current, AuthenticationRoute.Retry, message: ex.Message); }
    }

    public Task<AuthenticationDecision> CompletePermissionsAsync(CancellationToken token)
    {
        var previous = lastDecision;
        if (previous?.Identity is null || !IsCurrent(previous))
            return Task.FromResult(Decision(Interlocked.Increment(ref request), AuthenticationRoute.Login));
        if (previous.IsOffline)
            return Task.FromResult(Offline(Interlocked.Increment(ref request), previous.Identity, token, previous.Revision));
        return CompleteSignInAsync(previous.Identity.Id, token);
    }

    private async Task<AuthenticationDecision> CompleteAsync(long current, AccountIdentity identity, CancellationToken token)
    {
        long revision = source.Revision;
        AccountProfile profile;
        try { profile = await source.LoadProfileAsync(identity, token); }
        catch (AuthenticationFlowException ex) when (ex.Kind == AuthenticationFailureKind.Offline)
        { return Offline(current, identity, token, revision); }
        EnsureCurrent(current, revision, identity, offline: false, token);
        if (profile.UserId != identity.Id)
            throw new AuthenticationFlowException(AuthenticationFailureKind.Unavailable, "The saved profile belongs to another account. Please retry.");
        source.Activate(identity, offline: false);
        var route = !profile.HasAddress ? AuthenticationRoute.Address
            : !source.HasCompletedPermissions ? AuthenticationRoute.Permissions : AuthenticationRoute.Application;
        return Decision(current, route, identity);
    }

    private AuthenticationDecision Offline(long current, AccountIdentity identity, CancellationToken token, long? expectedRevision = null)
    {
        long revision = expectedRevision ?? source.Revision;
        EnsureCurrent(current, revision, identity, offline: true, token);
        var cached = source.ReadCachedProfile(identity);
        if (cached is not null && cached.UserId != identity.Id)
            throw new AuthenticationFlowException(AuthenticationFailureKind.Unavailable, "The saved profile belongs to another account. Please retry.");
        EnsureCurrent(current, revision, identity, offline: true, token);
        source.Activate(identity, offline: true);
        // Missing cached address must not block existing offline map access.
        return Decision(current, source.HasCompletedPermissions ? AuthenticationRoute.Application : AuthenticationRoute.Permissions,
            identity, offline: true);
    }

    private void EnsureCurrent(long current, long revision, AccountIdentity identity, bool offline, CancellationToken token)
    {
        EnsureRequest(current, token);
        if (revision != source.Revision || (!offline && source.CurrentUserId != identity.Id)
            || (offline && source.CurrentUserId is { } user && user != identity.Id))
            throw new OperationCanceledException("The account changed during sign-in.");
    }

    private void EnsureRequest(long current, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (current != Volatile.Read(ref request)) throw new OperationCanceledException("A newer sign-in request replaced this request.");
    }

    private AuthenticationDecision Decision(long current, AuthenticationRoute route, AccountIdentity? identity = null,
        bool offline = false, string message = "")
    {
        var result = new AuthenticationDecision(route, identity, offline, current, source.Revision, message);
        if (current == Volatile.Read(ref request)) lastDecision = result;
        return result;
    }
}
