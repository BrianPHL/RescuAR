using RescuAR.Services;

namespace RescuAR.App.Services.Profile;

internal sealed class MauiAvatarUpdateSource : IAvatarUpdateSource
{
    private readonly UserProfileService profiles = UserProfileService.Instance;
    public void RequireCurrent(ProfileAccountScope owner) => profiles.RequireOwner(owner);
    private static string Folder(ProfileAccountScope owner) =>
        Path.Combine(FileSystem.AppDataDirectory, "profile-photos", AvatarUpdateFlow.ScopeKey(owner));

    public (string Path, bool Pending) ReadLocal(ProfileAccountScope owner, string remoteUrl)
    {
        RequireCurrent(owner);
        string path = profiles.GetLocal(owner, "AvatarLocalPath");
        bool pending = profiles.GetLocal(owner, "AvatarPending") == "true";
        if (!IsOwnedPath(owner, path) || !File.Exists(path)) return (string.Empty, false);
        if (!pending && profiles.GetLocal(owner, "AvatarCloudUrl") != remoteUrl) return (string.Empty, false);
        return (path, pending);
    }

    public async Task<string> SaveLocalAsync(ProfileAccountScope owner, byte[] image, string extension)
    {
        RequireCurrent(owner);
        string folder = Folder(owner);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + extension);
        await File.WriteAllBytesAsync(path, image);
        RequireCurrent(owner);
        profiles.SetLocal(owner, "AvatarLocalPath", path);
        profiles.SetLocal(owner, "AvatarUploadedUrl", string.Empty);
        profiles.SetLocal(owner, "AvatarPending", "true");
        return path;
    }

    public async Task<string> UploadAsync(ProfileAccountScope owner, string localPath)
    {
        RequireLocal(owner, localPath);
        string uploaded = profiles.GetLocal(owner, "AvatarUploadedUrl");
        if (!string.IsNullOrWhiteSpace(uploaded)) return uploaded;
        if (Microsoft.Maui.Networking.Connectivity.Current.NetworkAccess != Microsoft.Maui.Networking.NetworkAccess.Internet)
            throw new IOException("Connect to upload the saved photo.");
        var client = await SupabaseService.Instance.GetClientAsync()
            ?? throw new InvalidOperationException("Please sign in again.");
        RequireLocal(owner, localPath);
        byte[] image = await File.ReadAllBytesAsync(localPath);
        RequireLocal(owner, localPath);
        // Keep dev's user-ID filename prefix and existing avatars bucket policies.
        string name = owner.UserId + "-" + Guid.NewGuid().ToString("N") + Path.GetExtension(localPath);
        await client.Storage.From("avatars").Upload(image, name, new Supabase.Storage.FileOptions { Upsert = false });
        RequireLocal(owner, localPath);
        string url = client.Storage.From("avatars").GetPublicUrl(name);
        profiles.SetLocal(owner, "AvatarUploadedUrl", url);
        return url;
    }

    public async Task ConfirmAsync(ProfileAccountScope owner, string remoteUrl) =>
        await profiles.PatchOwnedAsync(owner, (user => user.AvatarUrl, remoteUrl));

    public void MarkConfirmed(ProfileAccountScope owner, string localPath, string remoteUrl)
    {
        RequireLocal(owner, localPath);
        profiles.SetLocal(owner, "AvatarCloudUrl", remoteUrl);
        profiles.SetLocal(owner, "AvatarPending", "false");
    }

    private static bool IsOwnedPath(ProfileAccountScope owner, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { return Path.GetDirectoryName(Path.GetFullPath(path)) == Path.GetFullPath(Folder(owner)); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private void RequireLocal(ProfileAccountScope owner, string path)
    {
        RequireCurrent(owner);
        if (!IsOwnedPath(owner, path) || profiles.GetLocal(owner, "AvatarLocalPath") != path)
            throw new OperationCanceledException("A newer photo or account replaced this selection.");
    }
}
