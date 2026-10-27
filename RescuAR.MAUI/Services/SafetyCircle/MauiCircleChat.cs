using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using RescuAR.App.Models;
using RescuAR.App.Services.Cloud;
using RescuAR.App.Services.Profile;
using RescuAR.Services;

namespace RescuAR.App.Services.SafetyCircle;

internal static class CircleChatRuntime
{
    public static FileCircleChatStore Files { get; } = new(Path.Combine(FileSystem.AppDataDirectory, "circle-chat-v1"));
    public static CircleChatService Current { get; } = new(CircleMembershipRuntime.Current, Files,
        new SupabaseCircleChatRemote(), new CircleChatMediaUploader(), () => DateTimeOffset.UtcNow);

    public static async Task<ChatFile> ImportAsync(CircleChatScope scope, FileResult file, string kind, CancellationToken token)
    {
        Current.Require(scope, token);
        using var stream = await file.OpenReadAsync().WaitAsync(token);
        var copy = await Files.ImportAsync(scope, stream, file.FileName, kind, token);
        Current.Require(scope, token);
        return copy;
    }
    public static (string Name, string Avatar) Sender(CircleChatScope scope)
    {
        Current.Require(scope);
        var owner = UserProfileService.Instance.CaptureOwner();
        var profile = UserProfileService.Instance.ReadCached(scope.Account.UserId);
        string name = profile is null ? "" : $"{profile.FirstName} {profile.LastName}".Trim();
        if (name.Length == 0) name = profile?.Username ?? "";
        if (name.Length == 0)
        {
            var metadata = SupabaseService.Instance.Client?.Auth.CurrentUser?.UserMetadata;
            if (metadata?.TryGetValue("full_name", out var full) == true) name = full?.ToString() ?? "";
        }
        UserProfileService.Instance.RequireOwner(owner); Current.Require(scope);
        return (name.Length > 0 ? name : "Member", CirclePresencePolicy.SafeAvatar(profile?.AvatarUrl));
    }
}

internal sealed class SupabaseCircleChatRemote : ICircleChatRemote
{
    private static void Require(CircleChatScope scope, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); CircleMembershipRuntime.Context.Require(scope.Account);
        if (!CircleMembershipRuntime.Context.IsOnline) throw new CircleMembershipOfflineException("Connect to check Circle messages.");
    }
    private static async Task<Supabase.Client> Member(CircleChatScope scope, CancellationToken token)
    {
        Require(scope, token);
        var client = await SupabaseService.Instance.GetClientAsync().WaitAsync(token) ?? throw new InvalidOperationException("Please sign in again.");
        Require(scope, token);
        var membership = await client.From<SupabaseCircleMember>().Where(member => member.CircleId == scope.CircleId).Where(member => member.UserId == scope.Account.UserId).Get(token);
        Require(scope, token);
        if (!membership.Models.Any()) throw new UnauthorizedAccessException("Current Circle membership was not confirmed.");
        return client;
    }
    private static DateTimeOffset Utc(DateTime value) => new(value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime());
    private static CircleChatMessage Convert(SupabaseCircleMessage item) => new(item.Id, item.CircleId, item.UserId,
        string.IsNullOrWhiteSpace(item.SenderName) ? "Member name unavailable" : item.SenderName, CirclePresencePolicy.SafeAvatar(item.SenderAvatarUrl),
        item.MessageText ?? "", item.MediaUrl ?? "", item.MediaType ?? "Text", Utc(item.CreatedAt), ChatDelivery.Sent);
    public async Task<int?> ReadMemberCountAsync(CircleChatScope scope, CancellationToken token)
    {
        var client = await Member(scope, token);
        int count = await client.From<SupabaseCircleMember>().Where(member => member.CircleId == scope.CircleId)
            .Count(Supabase.Postgrest.Constants.CountType.Exact, token);
        Require(scope, token);
        return count > 0 ? count : null;
    }
    public async Task<List<CircleChatMessage>> ReadAsync(CircleChatScope scope, CancellationToken token)
    {
        var client = await Member(scope, token);
        var response = await client.From<SupabaseCircleMessage>().Where(message => message.CircleId == scope.CircleId)
            .Order("created_at", Supabase.Postgrest.Constants.Ordering.Ascending).Get(token);
        Require(scope, token); return response.Models.Select(Convert).ToList();
    }
    private static async Task<CircleChatMessage?> Confirm(Supabase.Client client, CircleChatScope scope, string id, CancellationToken token)
    {
        Require(scope, token);
        var response = await client.From<SupabaseCircleMessage>().Where(message => message.Id == id).Where(message => message.CircleId == scope.CircleId)
            .Where(message => message.UserId == scope.Account.UserId).Get(token);
        Require(scope, token);
        return response.Models.FirstOrDefault() is { } row ? Convert(row) : null;
    }
    public async Task<CircleChatMessage?> FindAsync(CircleChatScope scope, string id, CancellationToken token)
        => await Confirm(await Member(scope, token), scope, id, token);
    public async Task<CircleChatMessage> SendAsync(CircleChatScope scope, CircleChatMessage message, CancellationToken token)
    {
        var client = await Member(scope, token);
        if (message.UserId != scope.Account.UserId || message.CircleId != scope.CircleId) throw new UnauthorizedAccessException("The queued message has a different account or Circle.");
        var existing = await Confirm(client, scope, message.Id, token);
        if (existing is not null) return existing;
        try { await client.From<CircleChatWriteRow>().Insert(CircleChatWriteRow.From(message), null, token); }
        catch (OperationCanceledException) { throw; }
        catch
        {
            var confirmed = await Confirm(client, scope, message.Id, token);
            if (confirmed is not null) return confirmed;
            throw;
        }
        Require(scope, token);
        return await Confirm(client, scope, message.Id, token) ?? throw new InvalidOperationException("Message submission was not confirmed.");
    }
}

internal sealed class CircleChatMediaUploader : ICircleChatUploader
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(2) };
    public async Task<string> UploadAsync(CircleChatScope scope, ChatFile file, CancellationToken token)
    {
        CircleChatRuntime.Current.Require(scope, token);
        if (!CircleChatRuntime.Files.Owns(scope, file) || !File.Exists(file.Path) || file.Kind is not ("Image" or "Video"))
            throw new IOException("The selected attachment is unavailable for this account and Circle.");
        string resource = file.Kind == "Video" ? "video" : "image";
        // Cloudinary's unsigned REST API requires multipart binary data and a resource-specific endpoint.
        // https://cloudinary.com/documentation/image_upload_api_reference#unsigned_upload_syntax
        using var form = new MultipartFormDataContent();
        using var stream = File.OpenRead(file.Path);
        using var content = new StreamContent(stream);
        string mime = Path.GetExtension(file.Name).ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4", ".mov" => "video/quicktime", ".webm" => "video/webm", ".3gp" => "video/3gpp", ".m4v" => "video/x-m4v",
            ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".gif" => "image/gif", ".webp" => "image/webp", ".heic" => "image/heic", _ => "application/octet-stream"
        };
        content.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(content, "file", Path.GetFileName(file.Name));
        form.Add(new StringContent(CloudinaryService.UploadPreset), "upload_preset");
        CircleChatRuntime.Current.Require(scope, token);
        using var response = await Client.PostAsync($"https://api.cloudinary.com/v1_1/{Uri.EscapeDataString(CloudinaryService.CloudName)}/{resource}/upload", form, token);
        CircleChatRuntime.Current.Require(scope, token);
        if (!response.IsSuccessStatusCode) throw new IOException("The attachment upload was rejected. Its local draft is preserved.");
        var data = JObject.Parse(await response.Content.ReadAsStringAsync(token));
        CircleChatRuntime.Current.Require(scope, token);
        string url = data["secure_url"]?.ToString() ?? "";
        if (data["resource_type"]?.ToString() != resource || CirclePresencePolicy.SafeAvatar(url).Length == 0)
            throw new IOException("The attachment upload did not confirm its media type and secure URL.");
        return url;
    }
}
