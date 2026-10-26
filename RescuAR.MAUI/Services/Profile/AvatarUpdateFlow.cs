using System.Security.Cryptography;
using System.Text;

namespace RescuAR.App.Services.Profile;

internal interface IAvatarUpdateSource
{
    void RequireCurrent(ProfileAccountScope owner);
    Task<string> SaveLocalAsync(ProfileAccountScope owner, byte[] image, string extension);
    Task<string> UploadAsync(ProfileAccountScope owner, string localPath);
    Task ConfirmAsync(ProfileAccountScope owner, string remoteUrl);
    void MarkConfirmed(ProfileAccountScope owner, string localPath, string remoteUrl);
}

internal sealed record AvatarUpdateResult(string LocalPath, bool CloudConfirmed, string Status);

internal sealed class AvatarUpdateFlow(IAvatarUpdateSource source)
{
    public static string ScopeKey(ProfileAccountScope owner) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner.Project + "\n" + owner.UserId))).ToLowerInvariant();

    public static string Extension(string name) => Path.GetExtension(name).ToLowerInvariant() is var extension
        && new[] { ".jpg", ".jpeg", ".png", ".webp", ".heic", ".heif" }.Contains(extension) ? extension : ".jpg";

    public async Task<AvatarUpdateResult> SaveAsync(ProfileAccountScope owner, byte[] image, string name, Action<string>? localReady = null)
    {
        source.RequireCurrent(owner);
        if (image.Length == 0) throw new InvalidOperationException("The selected photo is empty.");
        string path = await source.SaveLocalAsync(owner, image, Extension(name));
        source.RequireCurrent(owner);
        localReady?.Invoke(path);
        return await SyncAsync(owner, path);
    }

    public async Task<AvatarUpdateResult> SyncAsync(ProfileAccountScope owner, string localPath)
    {
        source.RequireCurrent(owner);
        try
        {
            string url = await source.UploadAsync(owner, localPath);
            source.RequireCurrent(owner);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var remote) || remote.Scheme is not ("https" or "http"))
                throw new InvalidOperationException("The photo upload returned no usable address.");
            await source.ConfirmAsync(owner, url);
            source.RequireCurrent(owner);
            source.MarkConfirmed(owner, localPath, url);
            return new(localPath, true, "Profile photo saved to your account and this device.");
        }
        catch (OperationCanceledException error) when (error is not TaskCanceledException) { throw; }
        catch (Exception)
        {
            source.RequireCurrent(owner);
            return new(localPath, false, "Photo saved on this device only. The cloud update is not confirmed; reconnect and retry.");
        }
    }
}
