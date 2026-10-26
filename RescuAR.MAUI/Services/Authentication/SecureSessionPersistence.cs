using Newtonsoft.Json;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace RescuAR.App.Services.Authentication;

internal interface ISecureSessionStore
{
    Task<string?> ReadAsync(string key);
    Task WriteAsync(string key, string value);
    void Remove(string key);
}

internal sealed class SecureSessionPersistence(string projectUrl, ISecureSessionStore store)
    : IGotrueSessionPersistence<Session>
{
    private readonly string key = $"AuthSession:{new Uri(projectUrl).Host}";
    private readonly object gate = new();
    private Task pending = Task.CompletedTask;
    private Session? session;
    private bool enabled = true;

    public bool Enabled
    {
        get { lock (gate) return enabled; }
        set { lock (gate) enabled = value; }
    }

    public async Task ReadAsync()
    {
        string? json = await store.ReadAsync(key).ConfigureAwait(false);
        Session? restored = null;
        if (!string.IsNullOrEmpty(json))
        {
            try { restored = JsonConvert.DeserializeObject<Session>(json); }
            catch (JsonException) { store.Remove(key); }
            if (restored is not null && string.IsNullOrWhiteSpace(restored.AccessToken))
            {
                store.Remove(key);
                restored = null;
            }
        }
        lock (gate) session = restored;
    }

    public Session? LoadSession() { lock (gate) return enabled ? session : null; }

    public void SaveSession(Session value)
    {
        lock (gate)
        {
            if (!enabled || string.IsNullOrWhiteSpace(value.AccessToken)) return;
            session = value;
            Queue(JsonConvert.SerializeObject(value));
        }
    }

    public void DestroySession()
    {
        lock (gate)
        {
            session = null;
            Queue(null);
        }
    }

    // Caller holds gate. Removal is ordered after every preceding token save.
    private void Queue(string? json)
    {
        pending = WriteAfterAsync(pending, json);
        _ = pending.ContinueWith(failed =>
        {
            _ = failed.Exception;
            System.Diagnostics.Debug.WriteLine("Secure session persistence is unavailable.");
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    private async Task WriteAfterAsync(Task previous, string? json)
    {
        try { await previous.ConfigureAwait(false); }
        catch { /* A failed save must not prevent a subsequent sign-out removal. */ }
        if (json is null) store.Remove(key);
        else await store.WriteAsync(key, json).ConfigureAwait(false);
    }

    public Task FlushAsync() { lock (gate) return pending; }
}
