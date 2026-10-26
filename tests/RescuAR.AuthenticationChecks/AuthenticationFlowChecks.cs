using RescuAR.App.Services.Authentication;

internal static class AuthenticationFlowChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var source = new FakeFlowSource { Saved = null, HasSignedUp = false };
        var result = await new AuthenticationFlowCoordinator(source).StartAsync(default);
        check(result.Route == AuthenticationRoute.Onboarding && source.Activations == 0, "first run with no saved identity opens onboarding");
        source = new() { Saved = null, HasSignedUp = true };
        result = await new AuthenticationFlowCoordinator(source).StartAsync(default);
        check(result.Route == AuthenticationRoute.Login && source.VerifyCalls == 0, "saved signup flags cannot substitute for a remembered session");

        source = new();
        var flow = new AuthenticationFlowCoordinator(source);
        result = await flow.StartAsync(default);
        check(result.Route == AuthenticationRoute.Application && !result.IsOffline && source.Restoring, "restored online identity with address and permissions opens the app");
        source = new() { Profile = new("a", false) };
        result = await new AuthenticationFlowCoordinator(source).CompleteSignInAsync("a", default);
        check(result.Route == AuthenticationRoute.Address, "a verified sign-in with a missing address opens address completion");
        source = new() { HasCompletedPermissions = false };
        flow = new(source);
        result = await flow.CompleteSignInAsync("a", default);
        check(result.Route == AuthenticationRoute.Permissions && !source.Restoring, "new verified sign-in uses the same permissions gate without a redundant token lookup");
        source.HasCompletedPermissions = true;
        result = await flow.CompletePermissionsAsync(default);
        check(result.Route == AuthenticationRoute.Application, "permission completion returns through the shared decision flow");

        source = new() { IsOnline = false, CurrentUserId = null, Cached = null };
        result = await new AuthenticationFlowCoordinator(source).StartAsync(default);
        check(result.Route == AuthenticationRoute.Application && result.IsOffline && source.VerifyCalls == 0,
            "remembered offline identity retains local app access even without a cached address");
        source = new() { IsOnline = false, CurrentUserId = null, HasCompletedPermissions = false };
        flow = new(source);
        result = await flow.StartAsync(default);
        check(result.Route == AuthenticationRoute.Permissions && result.IsOffline, "offline startup retains the device permission gate");
        source.HasCompletedPermissions = true;
        result = await flow.CompletePermissionsAsync(default);
        check(result.Route == AuthenticationRoute.Application && result.IsOffline, "offline permission completion does not force a network authentication request");

        source = new() { VerifyError = new(AuthenticationFailureKind.Offline, "offline"), CurrentUserId = null };
        result = await new AuthenticationFlowCoordinator(source).StartAsync(default);
        check(result.IsOffline && result.Route == AuthenticationRoute.Application, "network loss during restoration uses the previously remembered identity for local access");
        source = new() { VerifyError = new(AuthenticationFailureKind.InvalidSession, "rejected") };
        result = await new AuthenticationFlowCoordinator(source).StartAsync(default);
        check(result.Route == AuthenticationRoute.Login && source.Invalidations == 1 && source.Activations == 0,
            "an explicitly rejected session is cleared and cannot fall back to offline access");
        source = new() { VerifyError = new(AuthenticationFailureKind.InvalidSession, "late rejection") };
        source.BeforeVerifyError = () => { source.CurrentUserId = "b"; source.Revision++; };
        bool lateRejection = false;
        try { await new AuthenticationFlowCoordinator(source).StartAsync(default); }
        catch (OperationCanceledException) { lateRejection = true; }
        check(lateRejection && source.Invalidations == 0, "an old rejected-session callback cannot clear a newer account");
        source = new() { VerifyError = new(AuthenticationFailureKind.Unavailable, "retry") };
        result = await new AuthenticationFlowCoordinator(source).StartAsync(default);
        check(result.Route == AuthenticationRoute.Retry && source.Activations == 0, "unclassified service failure produces a retry state instead of a successful login");
        source = new() { ProfileError = new(AuthenticationFailureKind.Offline, "offline") };
        result = await new AuthenticationFlowCoordinator(source).CompleteSignInAsync("a", default);
        check(result.IsOffline && result.Route == AuthenticationRoute.Application, "a verified login retains local access if the profile network request loses connection");
        source = new() { ProfileError = new(AuthenticationFailureKind.Unavailable, "profile denied") };
        result = await new AuthenticationFlowCoordinator(source).CompleteSignInAsync("a", default);
        check(result.Route == AuthenticationRoute.Retry && source.Activations == 0,
            "a failed profile read is not treated as an empty profile or permission to overwrite it");

        source = new() { Verified = new("b", "b@example.test"), CurrentUserId = "b" };
        bool differentIdentity = false;
        try { await new AuthenticationFlowCoordinator(source).StartAsync(default); }
        catch (OperationCanceledException) { differentIdentity = true; }
        check(differentIdentity && source.Activations == 0 && source.Invalidations == 0,
            "restoration cannot exchange the remembered identity or clear a newer signed-in account");
        source = new();
        result = await new AuthenticationFlowCoordinator(source).CompleteSignInAsync("another-user", default);
        check(result.Route == AuthenticationRoute.Retry && source.Activations == 0, "a stale OTP or Google callback cannot complete sign-in for another account");
        source = new() { Profile = new("b", true) };
        result = await new AuthenticationFlowCoordinator(source).CompleteSignInAsync("a", default);
        check(result.Route == AuthenticationRoute.Retry && source.Activations == 0, "a profile returned for another user cannot activate the current account");

        source = new() { ProfileBlock = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        flow = new(source);
        var pending = flow.StartAsync(default);
        await source.ProfileEntered.Task;
        source.CurrentUserId = "b";
        source.Revision++;
        source.ProfileBlock.SetResult(new("a", true));
        bool changed = false;
        try { await pending; } catch (OperationCanceledException) { changed = true; }
        check(changed && source.Activations == 0, "an account/revision change during profile loading rejects the late result");

        source = new() { ProfileBlock = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        flow = new(source);
        pending = flow.StartAsync(default);
        await source.ProfileEntered.Task;
        source.Saved = null;
        source.HasSignedUp = true;
        result = await flow.StartAsync(default);
        source.ProfileBlock.SetResult(new("a", true));
        bool replaced = false;
        try { await pending; } catch (OperationCanceledException) { replaced = true; }
        check(replaced && flow.IsCurrent(result) && source.Activations == 0, "a newer startup request prevents an older callback from publishing navigation");

        source = new();
        flow = new(source);
        result = await flow.CompleteSignInAsync("a", default);
        source.Revision++;
        source.CurrentUserId = null;
        check(!flow.IsCurrent(result), "sign-out invalidates an already-computed navigation decision");
        result = await flow.CompletePermissionsAsync(default);
        check(result.Route == AuthenticationRoute.Login, "a stale permissions page cannot re-enter a signed-out account");
    }

    private sealed class FakeFlowSource : IAuthenticationFlowSource
    {
        public bool IsOnline { get; set; } = true;
        public bool HasSignedUp { get; set; } = true;
        public bool HasCompletedPermissions { get; set; } = true;
        public long Revision { get; set; } = 1;
        public string? CurrentUserId { get; set; } = "a";
        public AccountIdentity? Saved { get; set; } = new("a", "a@example.test");
        public AccountIdentity? Verified { get; set; } = new("a", "a@example.test");
        public AccountProfile Profile { get; set; } = new("a", true);
        public AccountProfile? Cached { get; set; } = new("a", true);
        public AuthenticationFlowException? VerifyError { get; set; }
        public AuthenticationFlowException? ProfileError { get; set; }
        public Action? BeforeVerifyError { get; set; }
        public TaskCompletionSource<AccountProfile>? ProfileBlock { get; set; }
        public TaskCompletionSource ProfileEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Activations { get; private set; }
        public int Invalidations { get; private set; }
        public int VerifyCalls { get; private set; }
        public bool Restoring { get; private set; }
        public Task<AccountIdentity?> ReadSavedIdentityAsync(CancellationToken token) => Task.FromResult(Saved);
        public Task<AccountIdentity?> VerifyIdentityAsync(bool restoring, CancellationToken token)
        {
            VerifyCalls++;
            Restoring = restoring;
            BeforeVerifyError?.Invoke();
            return VerifyError is null ? Task.FromResult(Verified) : Task.FromException<AccountIdentity?>(VerifyError);
        }
        public Task<AccountProfile> LoadProfileAsync(AccountIdentity identity, CancellationToken token)
        {
            ProfileEntered.TrySetResult();
            if (ProfileError is not null) return Task.FromException<AccountProfile>(ProfileError);
            return ProfileBlock?.Task ?? Task.FromResult(Profile);
        }
        public AccountProfile? ReadCachedProfile(AccountIdentity identity) => Cached;
        public void Activate(AccountIdentity identity, bool offline) => Activations++;
        public Task InvalidateSessionAsync()
        {
            Invalidations++;
            Saved = null;
            CurrentUserId = null;
            Revision++;
            return Task.CompletedTask;
        }
    }
}
