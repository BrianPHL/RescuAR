using Newtonsoft.Json;
using RescuAR.App.Models;

namespace RescuAR.App.Services.Reports;

public static class ResidentReportWire
{
    public static CommunityReport ToRow(ResidentReportPayload report) => new()
    {
        Id = report.Id, Title = report.Title, Description = report.Description, Category = report.Category, Address = report.Address,
        Latitude = report.Point.Latitude, Longitude = report.Point.Longitude, PostedBy = report.PostedBy, Severity = report.Severity,
        MediaUrl = report.MediaUrl, CreatedAt = report.CreatedAt.UtcDateTime, Status = "Pending", LikeCount = 0
    };
    public static ResidentReportReceipt Receipt(CommunityReport report) => new(report.Id, report.Title, report.Description, report.Category,
        report.Address, report.Latitude, report.Longitude, report.PostedBy, report.Severity, report.MediaUrl ?? "", report.Status);
    public static JsonSerializerSettings Settings(bool inserting)
    {
        var settings = Supabase.Postgrest.Client.SerializerSettings(new Supabase.Postgrest.ClientOptions());
        if (settings.ContractResolver is Supabase.Postgrest.PostgrestContractResolver resolver) resolver.SetState(inserting, false, false);
        return settings;
    }
    public static string Serialize(ResidentReportPayload report) => JsonConvert.SerializeObject(ToRow(report), Settings(true));
    public static HttpRequestMessage Request(ResidentReportScope owner, ResidentReportPayload report, string apiKey, string accessToken)
    {
        if (string.IsNullOrWhiteSpace(owner.UserId) || string.IsNullOrWhiteSpace(accessToken)) throw new OperationCanceledException("A verified report session is required.");
        var request = new HttpRequestMessage(HttpMethod.Post, owner.Server.TrimEnd('/') + "/rest/v1/community_reports");
        request.Headers.Add("apikey", apiKey);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Prefer", "return=representation");
        request.Content = new StringContent(Serialize(report), System.Text.Encoding.UTF8, "application/json");
        return request;
    }
    public static ResidentReportReceipt? ReadReceipt(string json) => JsonConvert.DeserializeObject<List<CommunityReport>>(json, Settings(false))?.FirstOrDefault() is { } row ? Receipt(row) : null;
}
