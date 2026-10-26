using RescuAR.Services;

namespace RescuAR.App.Services.Authentication;

internal sealed class MauiPasswordRecoveryFactory : IPasswordRecoveryFactory
{
    public async Task<IPasswordRecoveryClient> CreateAsync()
    {
        var settings = SupabaseService.Instance;
        if (settings.IsMockMode) throw new InvalidOperationException("Account services are not configured.");
        string url = settings.SupabaseUrl, key = settings.SupabaseKey;
        // No shared persistence, refresh worker, or realtime connection for recovery.
        var temporary = new Supabase.Client(url, key, new Supabase.SupabaseOptions
        { AutoRefreshToken = false, AutoConnectRealtime = false });
        try
        {
            await temporary.InitializeAsync();
            return new RecoveryClient(temporary, url, key);
        }
        catch { temporary.Auth.Shutdown(); throw; }
    }

    private sealed class RecoveryClient(Supabase.Client client, string url, string key) : IPasswordRecoveryClient
    {
        private bool disposed;
        private void RequireCurrent()
        {
            if (disposed || SupabaseService.Instance.SupabaseUrl != url || SupabaseService.Instance.SupabaseKey != key)
                throw new OperationCanceledException("Account settings changed. Restart password recovery.");
        }

        public async Task SendCodeAsync(string email)
        {
            RequireCurrent();
            await client.Auth.ResetPasswordForEmail(email);
            RequireCurrent();
        }

        public async Task<string?> VerifyCodeAsync(string email, string code)
        {
            RequireCurrent();
            var session = await client.Auth.VerifyOTP(email, code, Supabase.Gotrue.Constants.EmailOtpType.Recovery);
            RequireCurrent();
            return string.IsNullOrWhiteSpace(session?.AccessToken) ? null : session.User?.Id;
        }

        public async Task<string?> UpdatePasswordAsync(string password)
        {
            RequireCurrent();
            var updated = await client.Auth.Update(new Supabase.Gotrue.UserAttributes { Password = password });
            RequireCurrent();
            return updated?.Id;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                if (client.Auth.CurrentSession is { } session)
                {
                    session.AccessToken = string.Empty;
                    session.RefreshToken = string.Empty;
                    session.ProviderToken = string.Empty;
                    session.ProviderRefreshToken = string.Empty;
                    session.User = null;
                }
            }
            finally { client.Auth.Shutdown(); }
        }
    }
}
