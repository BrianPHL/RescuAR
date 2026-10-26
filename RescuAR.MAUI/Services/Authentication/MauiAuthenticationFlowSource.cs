using RescuAR.App.Services.Profile;
using RescuAR.Services;
using Supabase.Gotrue;
using Supabase.Gotrue.Exceptions;

namespace RescuAR.App.Services.Authentication;

internal sealed class MauiAuthenticationFlowSource : IAuthenticationFlowSource
{
    private readonly UserProfileService profiles = UserProfileService.Instance;
    private User? savedUser;
    public bool IsOnline => Microsoft.Maui.Networking.Connectivity.Current.NetworkAccess == Microsoft.Maui.Networking.NetworkAccess.Internet;
    public bool HasSignedUp => Preferences.Default.Get("HasSignedUp", false);
    public bool HasCompletedPermissions => Preferences.Default.Get("HasCompletedPermissions", false);
    public long Revision => SupabaseService.Instance.SessionRevision;
    public string? CurrentUserId => SupabaseService.Instance.Client?.Auth.CurrentUser?.Id;

    public async Task<AccountIdentity?> ReadSavedIdentityAsync(CancellationToken token)
    {
        try
        {
            var session = await SupabaseService.Instance.ReadSavedSessionAsync().WaitAsync(token);
            savedUser = session?.User;
            return Identity(session);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new AuthenticationFlowException(AuthenticationFailureKind.Unavailable, "Saved sign-in could not be read. Please retry.", ex); }
    }

    public async Task<AccountIdentity?> VerifyIdentityAsync(bool restoring, CancellationToken token)
    {
        try
        {
            var client = await SupabaseService.Instance.GetClientAsync().WaitAsync(token)
                ?? throw new AuthenticationFlowException(AuthenticationFailureKind.Unavailable, "Account services are not configured.");
            if (restoring) await client.Auth.RetrieveSessionAsync().WaitAsync(token);
            var session = client.Auth.CurrentSession;
            var identity = Identity(session);
            if (identity is null || session!.Expired())
                throw new AuthenticationFlowException(AuthenticationFailureKind.InvalidSession, "Please sign in again.");
            if (restoring)
            {
                var verified = await client.Auth.GetUser(session.AccessToken!).WaitAsync(token);
                if (verified?.Id != identity.Id)
                    throw new AuthenticationFlowException(AuthenticationFailureKind.InvalidSession, "Please sign in again.");
            }
            savedUser = session.User;
            return identity;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (AuthenticationFlowException) { throw; }
        catch (Exception ex) { throw Classify(ex); }
    }

    public async Task<AccountProfile> LoadProfileAsync(AccountIdentity identity, CancellationToken token)
    {
        try
        {
            var user = await profiles.LoadAsync(token, identity.Id);
            return new(user.Id, !string.IsNullOrWhiteSpace(user.Address));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw Classify(ex); }
    }

    public AccountProfile? ReadCachedProfile(AccountIdentity identity)
    {
        var cached = profiles.ReadCached(identity.Id);
        return cached is null ? null : new(cached.Id, !string.IsNullOrWhiteSpace(cached.Address));
    }

    public void Activate(AccountIdentity identity, bool offline) => profiles.Activate(identity.Id, identity.Email, savedUser, offline);

    public async Task InvalidateSessionAsync()
    {
        string? previous = CurrentUserId;
        try { await SupabaseService.Instance.ClearSessionAsync(); }
        finally { AuthenticationIdentity.ClearActive(new MauiIdentityPreferences(), previous); }
    }

    private static AccountIdentity? Identity(Session? session) => !string.IsNullOrWhiteSpace(session?.AccessToken)
        && !string.IsNullOrWhiteSpace(session.User?.Id) ? new(session.User.Id, session.User.Email ?? string.Empty) : null;

    private static AuthenticationFlowException Classify(Exception error)
    {
        if (error is GotrueException auth && ((int)auth.StatusCode is 401 or 403
            || auth.Reason == FailureHint.Reason.ExpiredRefreshToken || auth.Reason == FailureHint.Reason.InvalidRefreshToken))
            return new(AuthenticationFailureKind.InvalidSession, "Please sign in again.", error);
        if (error is Supabase.Postgrest.Exceptions.PostgrestException denied && (int)denied.StatusCode is 401 or 403)
            return new(AuthenticationFailureKind.InvalidSession, "Account access could not be confirmed. Please sign in again.", error);
        if (error is HttpRequestException or TimeoutException or TaskCanceledException
            || error is GotrueException { Reason: FailureHint.Reason.Offline })
            return new(AuthenticationFailureKind.Offline, "You are offline. Saved local features remain available.", error);
        return new(AuthenticationFailureKind.Unavailable, "Your account could not be loaded. Please retry.", error);
    }
}
