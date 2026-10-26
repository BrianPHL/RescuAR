namespace RescuAR.App.Services.Reports;

public readonly record struct ResidentReportScope(string Server, string UserId, long Revision);
public enum IncidentPointSource { Gps, Map }
public sealed record IncidentPoint(double Latitude, double Longitude, IncidentPointSource Source, DateTimeOffset SelectedAt);
public sealed record ResidentReportPayload(string Id, string Title, string Description, string Category, string Address,
    IncidentPoint Point, string PostedBy, string Severity, string MediaUrl, DateTimeOffset CreatedAt);
public sealed record ResidentReportReceipt(string Id, string Title, string Description, string Category, string Address,
    double Latitude, double Longitude, string PostedBy, string Severity, string MediaUrl, string Status);
public interface IResidentReportContext
{
    ResidentReportScope Capture();
    void Require(ResidentReportScope scope, CancellationToken token = default);
}
public interface IResidentReportTransport
{
    Task<ResidentReportReceipt?> FindAsync(ResidentReportScope scope, string id, CancellationToken token);
    Task<ResidentReportReceipt?> InsertSdkAsync(ResidentReportScope scope, ResidentReportPayload report, CancellationToken token);
    Task<ResidentReportReceipt?> InsertRestAsync(ResidentReportScope scope, ResidentReportPayload report, CancellationToken token);
}
public sealed class ResidentReportInputException(string message) : InvalidOperationException(message);
public sealed class ResidentReportUnconfirmedException(string message) : IOException(message);

public static class IncidentPointPolicy
{
    public static bool Coordinates(double latitude, double longitude) => double.IsFinite(latitude) && double.IsFinite(longitude)
        && Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180 && (latitude != 0 || longitude != 0);
    public static IncidentPoint FromMap(double latitude, double longitude, DateTimeOffset now)
    {
        if (!Coordinates(latitude, longitude)) throw new ResidentReportInputException("Choose a valid incident point on the map.");
        return new(latitude, longitude, IncidentPointSource.Map, now);
    }
    public static IncidentPoint FromGps(double latitude, double longitude, double? accuracy, DateTimeOffset observedAt, bool mock, DateTimeOffset now)
    {
        if (!Coordinates(latitude, longitude) || mock || accuracy is not (> 0 and <= 100) ||
            observedAt <= DateTimeOffset.UnixEpoch || now - observedAt < TimeSpan.FromSeconds(-2) || now - observedAt > TimeSpan.FromSeconds(30))
            throw new ResidentReportInputException("GPS could not provide a recent, accurate location. Choose the incident point on the map.");
        return new(latitude, longitude, IncidentPointSource.Gps, observedAt);
    }
    public static bool Chosen(IncidentPoint? point) => point is not null && Coordinates(point.Latitude, point.Longitude)
        && Enum.IsDefined(point.Source) && point.SelectedAt > DateTimeOffset.UnixEpoch;
}

public sealed class ResidentReportSubmissionFlow(IResidentReportContext context, IResidentReportTransport transport)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public ResidentReportScope Capture() => context.Capture();
    public void Require(ResidentReportScope owner, CancellationToken token = default) => context.Require(owner, token);
    public static bool SecureMedia(string value) => value.Length == 0 || Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);
    public static void Validate(ResidentReportPayload report)
    {
        if (!Guid.TryParse(report.Id, out _) || string.IsNullOrWhiteSpace(report.Title) || string.IsNullOrWhiteSpace(report.Description))
            throw new ResidentReportInputException("Enter a title and description for this report.");
        if (!IncidentPointPolicy.Chosen(report.Point)) throw new ResidentReportInputException("Choose a GPS or map point before submitting the report.");
        if (string.IsNullOrWhiteSpace(report.PostedBy) || report.CreatedAt <= DateTimeOffset.UnixEpoch || !SecureMedia(report.MediaUrl))
            throw new ResidentReportInputException("The report identity or selected media could not be confirmed. Review the draft.");
    }
    private static ResidentReportReceipt Confirm(ResidentReportPayload expected, ResidentReportReceipt actual)
    {
        if (actual.Id != expected.Id || actual.Title != expected.Title || actual.Description != expected.Description || actual.Category != expected.Category ||
            actual.Address != expected.Address || actual.PostedBy != expected.PostedBy || actual.Severity != expected.Severity || actual.MediaUrl != expected.MediaUrl ||
            !IncidentPointPolicy.Coordinates(actual.Latitude, actual.Longitude) ||
            Math.Abs(actual.Latitude - expected.Point.Latitude) > 0.0000001 || Math.Abs(actual.Longitude - expected.Point.Longitude) > 0.0000001)
            throw new ResidentReportUnconfirmedException("An earlier submission or a different response uses this report ID. Check its details before starting a new report.");
        return actual;
    }
    public async Task<ResidentReportReceipt> SubmitAsync(ResidentReportScope owner, ResidentReportPayload report, CancellationToken token = default)
    {
        context.Require(owner, token); Validate(report);
        await gate.WaitAsync(token);
        try
        {
            context.Require(owner, token);
            var existing = await transport.FindAsync(owner, report.Id, token); context.Require(owner, token);
            if (existing is not null) return Confirm(report, existing);
            ResidentReportReceipt? response = null;
            try { response = await transport.InsertSdkAsync(owner, report, token); }
            catch (OperationCanceledException) { throw; }
            catch { context.Require(owner, token); }
            context.Require(owner, token);
            if (response is not null) return Confirm(report, response);
            existing = await transport.FindAsync(owner, report.Id, token); context.Require(owner, token);
            if (existing is not null) return Confirm(report, existing);
            response = await transport.InsertRestAsync(owner, report, token); context.Require(owner, token);
            if (response is not null) return Confirm(report, response);
            existing = await transport.FindAsync(owner, report.Id, token); context.Require(owner, token);
            return existing is not null ? Confirm(report, existing) : throw new ResidentReportUnconfirmedException("Report submission was not confirmed. Your draft is retained for retry.");
        }
        finally { gate.Release(); }
    }
}

/// <summary>Remembers a confirmed upload only for the exact account/session and selected file.</summary>
public sealed class ResidentReportMediaDraft(IResidentReportContext context)
{
    private ResidentReportScope? owner;
    private string selection = "";
    private string uploaded = "";
    public void Reset() { owner = null; selection = uploaded = ""; }
    public async Task<string> PrepareAsync(ResidentReportScope scope, string? selectedFile,
        Func<CancellationToken, Task<string>> upload, CancellationToken token = default)
    {
        context.Require(scope, token);
        if (string.IsNullOrEmpty(selectedFile)) { Reset(); return ""; }
        if (owner == scope && selection == selectedFile && uploaded.Length > 0) return uploaded;
        string url = await upload(token); context.Require(scope, token);
        if (url.Length == 0 || !ResidentReportSubmissionFlow.SecureMedia(url))
            throw new ResidentReportUnconfirmedException("The selected photo upload was not confirmed. The photo and report draft are retained.");
        owner = scope; selection = selectedFile; uploaded = url;
        return url;
    }
}
