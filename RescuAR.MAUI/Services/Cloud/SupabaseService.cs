using Supabase;
using RescuAR.App.Services.Authentication;

namespace RescuAR.Services;

public class SupabaseService
{
    private static SupabaseService? _instance;
    public static SupabaseService Instance => _instance ??= new SupabaseService();

    public const string PlaceholderUrl = "https://itxjqcnvxlgzeqkivhhc.supabase.co";
    public const string PlaceholderKey = "sb_publishable_QQi7onBtfe-d8fV0ep_K9g_N94rlE0s";

    public string SupabaseUrl { get; private set; } = PlaceholderUrl;
    public string SupabaseKey { get; private set; } = PlaceholderKey;
    public string GoogleWebClientId { get; private set; } = "110430882823-ck8pi6d9ngiedo78mmg3gpsf6f2p9ove.apps.googleusercontent.com";

    public Client? Client { get; private set; }

    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private bool initialized;
    private long clientRevision;
    private SecureSessionPersistence? persistence;
    private Task previousPersistence = Task.CompletedTask;

    internal long SessionRevision => Volatile.Read(ref clientRevision);

    internal async Task<Supabase.Gotrue.Session?> ReadSavedSessionAsync()
    {
        if (IsMockMode || !Preferences.Default.Get("RememberMe", false)) return null;
        await initializationGate.WaitAsync();
        try
        {
            try { await previousPersistence; } catch { }
            if (persistence is { } active) { try { await active.FlushAsync(); } catch { } }
            var saved = new SecureSessionPersistence(SupabaseUrl, new MauiSecureSessionStore());
            long revision = SessionRevision;
            await saved.ReadAsync();
            if (revision != SessionRevision) throw new OperationCanceledException("Account settings changed during startup.");
            return saved.LoadSession();
        }
        finally { initializationGate.Release(); }
    }

    public async Task SetSessionPersistenceAsync(bool enabled, string? expectedUserId = null)
    {
        await initializationGate.WaitAsync();
        try
        {
            if (expectedUserId is not null && Client?.Auth.CurrentUser?.Id != expectedUserId)
                throw new OperationCanceledException("Your account changed. Please retry.");
            if (persistence is null) return;
            persistence.Enabled = enabled;
            if (!enabled) persistence.DestroySession();
            else if (Client?.Auth.CurrentSession is { } session) persistence.SaveSession(session);
            await persistence.FlushAsync();
        }
        finally { initializationGate.Release(); }
    }

    public async Task ClearSessionAsync()
    {
        Interlocked.Increment(ref clientRevision);
        await initializationGate.WaitAsync();
        try
        {
            Client?.Auth.Shutdown();
            try { await previousPersistence; } catch { }
            var current = persistence ?? new SecureSessionPersistence(SupabaseUrl, new MauiSecureSessionStore());
            current.Enabled = false;
            current.DestroySession();
            await current.FlushAsync();
        }
        finally
        {
            try { InitializeClient(); }
            finally { initializationGate.Release(); }
        }
    }


    public bool IsMockMode => string.IsNullOrWhiteSpace(SupabaseUrl) ||
                              SupabaseUrl == "https://your-supabase-project.supabase.co" ||
                              string.IsNullOrWhiteSpace(SupabaseKey) ||
                              SupabaseKey == "your-supabase-anon-key";

    private SupabaseService()
    {
        LoadKeys();
        InitializeClient();
    }

    public void LoadKeys()
    {
        SupabaseUrl = Preferences.Default.Get("SupabaseUrl", PlaceholderUrl);
        SupabaseKey = Preferences.Default.Get("SupabaseKey", PlaceholderKey);

        // Reset preferences if they contain old placeholders
        if (SupabaseUrl == "https://your-supabase-project.supabase.co")
        {
            SupabaseUrl = PlaceholderUrl;
            Preferences.Default.Set("SupabaseUrl", PlaceholderUrl);
        }
        if (SupabaseKey == "your-supabase-anon-key")
        {
            SupabaseKey = PlaceholderKey;
            Preferences.Default.Set("SupabaseKey", PlaceholderKey);
        }

        GoogleWebClientId = Preferences.Default.Get("GoogleWebClientId", "110430882823-ck8pi6d9ngiedo78mmg3gpsf6f2p9ove.apps.googleusercontent.com");
    }

    public void SaveKeys(string url, string key, string googleWebClientId)
    {
        SupabaseUrl = url.Trim();
        SupabaseKey = key.Trim();
        GoogleWebClientId = googleWebClientId.Trim();
        Preferences.Default.Set("SupabaseUrl", SupabaseUrl);
        Preferences.Default.Set("SupabaseKey", SupabaseKey);
        Preferences.Default.Set("GoogleWebClientId", GoogleWebClientId);
        InitializeClient();
    }

    public async Task<Client?> GetClientAsync()
    {
        if (IsMockMode) return null;
        await initializationGate.WaitAsync();
        try
        {
            if (IsMockMode) return null;
            if (initialized) return Client;
            if (Client is null) InitializeClient();
            var client = Client ?? throw new InvalidOperationException("Supabase configuration is unavailable.");
            long revision = Volatile.Read(ref clientRevision);
            try { await previousPersistence; } catch { }
            if (persistence is { } old)
            {
                old.Enabled = false;
                try { await old.FlushAsync(); } catch { }
            }
            var saved = new SecureSessionPersistence(SupabaseUrl, new MauiSecureSessionStore())
            { Enabled = Preferences.Default.Get("RememberMe", false) };
            persistence = saved;
            await saved.ReadAsync();
            RequireCurrentClient(client, revision);
            client.Auth.SetPersistence(saved);
            client.Auth.LoadSession();
            await client.InitializeAsync();
            RequireCurrentClient(client, revision);
            initialized = true;
            return client;
        }
        catch
        {
            initialized = false;
            throw;
        }
        finally { initializationGate.Release(); }
    }

    private void RequireCurrentClient(Client client, long revision)
    {
        if (!ReferenceEquals(client, Client) || revision != Volatile.Read(ref clientRevision))
            throw new OperationCanceledException("Supabase configuration changed. Please retry.");
    }

    public void InitializeClient()
    {
        initialized = false;
        Interlocked.Increment(ref clientRevision);
        if (persistence is { } previous)
        {
            previous.Enabled = false;
            previousPersistence = previous.FlushAsync();
            persistence = null;
        }
        Client?.Auth.Shutdown();
        if (IsMockMode)
        {
            Client = null;
            return;
        }

        try
        {
            var options = new SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = true
            };
            Client = new Client(SupabaseUrl, SupabaseKey, options);
        }
        catch
        {
            Client = null;
        }
    }
}
