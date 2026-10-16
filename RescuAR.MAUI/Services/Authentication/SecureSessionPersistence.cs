using Newtonsoft.Json;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace RescuAR.App.Services.Authentication;

internal sealed class SecureSessionPersistence : IGotrueSessionPersistence<Session>
{
    private readonly string _key;
    private readonly object _gate = new();
    private Task _pending = Task.CompletedTask;
    private Session? _session;
    public bool Enabled { get; set; } = true;

    public SecureSessionPersistence(string projectUrl) => _key = $"AuthSession:{new Uri(projectUrl).Host}";

    public async Task ReadAsync()
    {
        try
        {
            var json = await SecureStorage.Default.GetAsync(_key);
            _session = string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<Session>(json);
        }
        catch
        {
            SecureStorage.Default.Remove(_key);
            _session = null;
        }
    }

    public Session? LoadSession() => Enabled ? _session : null;
    public void SaveSession(Session session)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(session.AccessToken)) return;
        _session = session;
        Queue(JsonConvert.SerializeObject(session));
    }

    public void DestroySession()
    {
        _session = null;
        Queue(null);
    }

    private void Queue(string? json)
    {
        lock (_gate)
        {
            _pending = WriteAfterAsync(_pending, json);
        }
    }

    private async Task WriteAfterAsync(Task previous, string? json)
    {
        await previous.ConfigureAwait(false);
        try
        {
            if (json == null) SecureStorage.Default.Remove(_key);
            else await SecureStorage.Default.SetAsync(_key, json).ConfigureAwait(false);
        }
        catch
        {
            // Keep the in-memory login usable; never write tokens to Preferences.
            System.Diagnostics.Debug.WriteLine("Secure session persistence is unavailable.");
        }
    }

    public Task FlushAsync() { lock (_gate) return _pending; }
}
