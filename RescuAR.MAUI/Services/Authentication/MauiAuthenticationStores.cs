using Microsoft.Maui.Storage;

namespace RescuAR.App.Services.Authentication;

internal sealed class MauiSecureSessionStore : ISecureSessionStore
{
    public Task<string?> ReadAsync(string key) => Preferences.Default.Get(key + ":SignedOut", false)
        ? Task.FromResult<string?>(null)
        : SecureStorage.Default.GetAsync(key);

    public async Task WriteAsync(string key, string value)
    {
        await SecureStorage.Default.SetAsync(key, value).ConfigureAwait(false);
        Preferences.Default.Remove(key + ":SignedOut");
    }

    public void Remove(string key)
    {
        // A failed secure-storage removal must not restore a signed-out token on restart.
        Preferences.Default.Set(key + ":SignedOut", true);
        SecureStorage.Default.Remove(key);
    }
}

internal sealed class MauiIdentityPreferences : IIdentityPreferences
{
    public bool ContainsKey(string key) => Preferences.Default.ContainsKey(key);
    public string Get(string key) => Preferences.Default.Get(key, string.Empty);
    public void Set(string key, string value) => Preferences.Default.Set(key, value);
    public void Remove(string key) => Preferences.Default.Remove(key);
    public void MarkSignedOut()
    {
        Preferences.Default.Set("IsLoggedIn", false);
        Preferences.Default.Set("AuthenticationOffline", false);
    }
}
