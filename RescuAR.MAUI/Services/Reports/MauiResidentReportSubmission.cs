using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using RescuAR.App.Models;
using RescuAR.App.Services.Cloud;
using RescuAR.App.Services.Profile;
using RescuAR.Services;

namespace RescuAR.App.Services.Reports;

internal static class ResidentReportRuntime
{
    public static MauiResidentReportContext Context { get; } = new();
    public static ResidentReportSubmissionFlow Submission { get; } = new(Context, new MauiResidentReportTransport());
    private static readonly HttpClient MediaClient = new() { Timeout = TimeSpan.FromMinutes(2) };

    public static string Sender(ResidentReportScope owner)
    {
        Context.Require(owner);
        var profile = UserProfileService.Instance.ReadCached(owner.UserId);
        string name = profile is null ? "" : $"{profile.FirstName} {profile.LastName}".Trim();
        if (name.Length == 0) name = profile?.Username?.Trim() ?? "";
        var metadata = SupabaseService.Instance.Client?.Auth.CurrentUser?.UserMetadata;
        if (name.Length == 0 && metadata?.TryGetValue("full_name", out var full) == true) name = full?.ToString()?.Trim() ?? "";
        Context.Require(owner);
        return name.Length > 0 ? name : "Resident";
    }
    public static async Task<string> UploadPhotoAsync(ResidentReportScope owner, FileResult photo, CancellationToken token)
    {
        Context.Require(owner, token);
        using var input = await photo.OpenReadAsync().WaitAsync(token);
        Context.Require(owner, token);
        using var buffer = new MemoryStream(); byte[] block = new byte[81920]; int count;
        while ((count = await input.ReadAsync(block, token)) > 0)
        {
            if (buffer.Length + count > 10 * 1024 * 1024) throw new ResidentReportInputException("Choose a report photo up to 10 MB.");
            await buffer.WriteAsync(block.AsMemory(0, count), token);
        }
        if (buffer.Length == 0) throw new ResidentReportInputException("The selected report photo is empty.");
        buffer.Position = 0; Context.Require(owner, token);
        using var form = new MultipartFormDataContent();
        using var file = new StreamContent(buffer);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", Path.GetFileName(photo.FileName));
        form.Add(new StringContent(CloudinaryService.UploadPreset), "upload_preset");
        // Same configured provider; multipart format follows the existing Circle upload adapter.
        using var response = await MediaClient.PostAsync($"https://api.cloudinary.com/v1_1/{Uri.EscapeDataString(CloudinaryService.CloudName)}/image/upload", form, token);
        Context.Require(owner, token);
        if (!response.IsSuccessStatusCode) throw new ResidentReportUnconfirmedException("Photo upload was not confirmed. Your photo and report draft are retained.");
        var result = JObject.Parse(await response.Content.ReadAsStringAsync(token)); Context.Require(owner, token);
        string url = result["secure_url"]?.ToString() ?? "";
        if (result["resource_type"]?.ToString() != "image" || url.Length == 0 || !ResidentReportSubmissionFlow.SecureMedia(url))
            throw new ResidentReportUnconfirmedException("The provider did not confirm this photo upload.");
        return url;
    }
}

internal sealed class MauiResidentReportContext : IResidentReportContext
{
    private static string Server => new Uri(SupabaseService.Instance.SupabaseUrl).AbsoluteUri.TrimEnd('/');
    public ResidentReportScope Capture()
    {
        var profile = UserProfileService.Instance.CaptureOwner();
        var owner = new ResidentReportScope(Server, profile.UserId, profile.Revision); Require(owner);
        return owner;
    }
    public void Require(ResidentReportScope owner, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var service = SupabaseService.Instance;
        if (owner.Server != Server || owner.Revision != service.SessionRevision || string.IsNullOrWhiteSpace(owner.UserId) ||
            service.Client?.Auth.CurrentUser?.Id != owner.UserId || string.IsNullOrWhiteSpace(service.Client?.Auth.CurrentSession?.AccessToken))
            throw new OperationCanceledException("The signed-in report account or server changed.", token);
    }
}

internal sealed class MauiResidentReportTransport : IResidentReportTransport
{
    private static readonly HttpClient Rest = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static async Task<Supabase.Client> Client(ResidentReportScope owner, CancellationToken token)
    {
        ResidentReportRuntime.Context.Require(owner, token);
        var client = await SupabaseService.Instance.GetClientAsync().WaitAsync(token) ?? throw new InvalidOperationException("Please sign in again.");
        ResidentReportRuntime.Context.Require(owner, token);
        return client;
    }
    public async Task<ResidentReportReceipt?> FindAsync(ResidentReportScope owner, string id, CancellationToken token)
    {
        var client = await Client(owner, token);
        var response = await client.From<CommunityReport>().Where(report => report.Id == id).Get(token);
        ResidentReportRuntime.Context.Require(owner, token);
        return response.Models.FirstOrDefault() is { } row ? ResidentReportWire.Receipt(row) : null;
    }
    public async Task<ResidentReportReceipt?> InsertSdkAsync(ResidentReportScope owner, ResidentReportPayload report, CancellationToken token)
    {
        var client = await Client(owner, token);
        var response = await client.From<CommunityReport>().Insert(ResidentReportWire.ToRow(report), null, token);
        ResidentReportRuntime.Context.Require(owner, token);
        return response.Models.FirstOrDefault() is { } row ? ResidentReportWire.Receipt(row) : null;
    }
    public async Task<ResidentReportReceipt?> InsertRestAsync(ResidentReportScope owner, ResidentReportPayload report, CancellationToken token)
    {
        var client = await Client(owner, token);
        string accessToken = client.Auth.CurrentSession?.AccessToken ?? throw new OperationCanceledException("A signed-in report session is required.", token);
        using var request = ResidentReportWire.Request(owner, report, SupabaseService.Instance.SupabaseKey, accessToken);
        ResidentReportRuntime.Context.Require(owner, token);
        using var response = await Rest.SendAsync(request, token);
        ResidentReportRuntime.Context.Require(owner, token);
        if (!response.IsSuccessStatusCode) throw new ResidentReportUnconfirmedException("Report submission was not confirmed. Keep the draft and retry when access and connection are available.");
        string json = await response.Content.ReadAsStringAsync(token); ResidentReportRuntime.Context.Require(owner, token);
        return string.IsNullOrWhiteSpace(json) ? null : ResidentReportWire.ReadReceipt(json);
    }
}
