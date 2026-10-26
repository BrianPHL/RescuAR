using Newtonsoft.Json.Linq;
using RescuAR.App.Services.Reports;

internal static class ResidentReportChecks
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly IncidentPoint Point = IncidentPointPolicy.FromMap(14.65, 121.1, Now);
    private static ResidentReportPayload Payload() => new(Guid.NewGuid().ToString(), "Road hazard", "Debris on the road", "Road Hazard", "Chosen street", Point, "Resident", "Medium", "", Now);
    public static async Task RunAsync(Action<bool, string> check)
    {
        check(!IncidentPointPolicy.Chosen(null), "preset report browsing coordinates alone cannot qualify as a chosen incident point");
        check(!IncidentPointPolicy.Coordinates(double.NaN, 121) && !IncidentPointPolicy.Coordinates(14, double.PositiveInfinity), "invalid coordinates cannot be posted by the report fallback");
        check(!IncidentPointPolicy.Coordinates(0, 0) && !IncidentPointPolicy.Coordinates(91, 121) && !IncidentPointPolicy.Coordinates(14, 181), "placeholder and out-of-range report points are rejected");
        check(IncidentPointPolicy.Coordinates(0, 121) && IncidentPointPolicy.Coordinates(14, 0), "a valid coordinate component of zero is not replaced by a Marikina default");
        check(IncidentPointPolicy.Chosen(Point), "an explicitly selected map/search point qualifies for an incident report");
        var gps = IncidentPointPolicy.FromGps(14.65, 121.1, 10, Now.AddSeconds(-4), false, Now);
        check(gps.Source == IncidentPointSource.Gps && gps.SelectedAt == Now.AddSeconds(-4), "a qualified GPS selection retains its acquisition time");
        await Reject<ResidentReportInputException>(() => Task.FromResult(IncidentPointPolicy.FromGps(14.65, 121.1, 10, Now.AddMinutes(-2), false, Now)), check, "stale GPS cannot silently become a chosen current report location");
        await Reject<ResidentReportInputException>(() => Task.FromResult(IncidentPointPolicy.FromGps(14.65, 121.1, null, Now, false, Now)), check, "GPS with unknown accuracy requires manual map selection");
        await Reject<ResidentReportInputException>(() => Task.FromResult(IncidentPointPolicy.FromGps(14.65, 121.1, 101, Now, false, Now)), check, "an inaccurate GPS reading cannot invent an incident point");
        await Reject<ResidentReportInputException>(() => Task.FromResult(IncidentPointPolicy.FromGps(14.65, 121.1, 10, Now, true, Now)), check, "mock GPS cannot populate a real incident report");
        check(IncidentPointPolicy.Chosen(gps), "an incident point already chosen by GPS remains a fixed report target while the resident writes details");

        var rig = new Rig(); var owner = rig.Context.Current; var report = Payload();
        await Reject<ResidentReportInputException>(() => rig.Flow.SubmitAsync(owner, report with { Point = null! }), check, "report submission requires a chosen point before any transport call");
        await Reject<ResidentReportInputException>(() => rig.Flow.SubmitAsync(owner, report with { Title = " " }), check, "empty report titles cannot enter the retry pipeline");
        await Reject<ResidentReportInputException>(() => rig.Flow.SubmitAsync(owner, report with { MediaUrl = "file:///private/photo.jpg" }), check, "local photo paths are never treated as uploaded report media");
        check(rig.Transport.SdkCalls == 0 && rig.Transport.RestCalls == 0 && rig.Transport.Reads == 0, "invalid report drafts never query or write the server");

        var accepted = await rig.Flow.SubmitAsync(owner, report);
        check(accepted.Id == report.Id && rig.Transport.SdkCalls == 1 && rig.Transport.RestCalls == 0, "a confirmed SDK report uses the draft's stable ID and needs no REST retry");
        accepted = await rig.Flow.SubmitAsync(owner, report);
        check(accepted.Id == report.Id && rig.Transport.SdkCalls == 1 && rig.Transport.Rows.Count == 1, "retry checks an already accepted report ID before inserting a duplicate");
        await Reject<ResidentReportUnconfirmedException>(() => rig.Flow.SubmitAsync(owner, report with { Description = "Edited after an unknown result" }), check, "retry cannot silently overwrite a previously accepted report with edited details");

        rig = new Rig(); owner = rig.Context.Current; report = Payload(); rig.Transport.FailSdk = true;
        accepted = await rig.Flow.SubmitAsync(owner, report);
        check(accepted.Id == report.Id && rig.Transport.SdkCalls == 1 && rig.Transport.RestCalls == 1 && rig.Transport.LastRestId == report.Id, "the verified REST fallback retains the same report ID");
        check(rig.Transport.LastScope == owner, "SDK and REST retries retain the captured signed-in account and backend");
        rig = new Rig(); owner = rig.Context.Current; report = Payload(); rig.Transport.LoseSdkResponse = true;
        accepted = await rig.Flow.SubmitAsync(owner, report);
        check(accepted.Id == report.Id && rig.Transport.RestCalls == 0 && rig.Transport.Rows.Count == 1, "a lost SDK response is reconciled before a REST fallback could duplicate the report");
        rig = new Rig(); owner = rig.Context.Current; report = Payload(); rig.Transport.EmptySdk = true; rig.Transport.EmptyRest = true;
        await Reject<ResidentReportUnconfirmedException>(() => rig.Flow.SubmitAsync(owner, report), check, "two unconfirmed submission attempts cannot return a success result");
        check(rig.Transport.LastRestId == report.Id && report.Point == Point, "unconfirmed attempts do not replace the draft identity or chosen coordinates");
        rig.Transport.EmptySdk = false; rig.Transport.EmptyRest = false;
        accepted = await rig.Flow.SubmitAsync(owner, report);
        check(accepted.Id == report.Id, "the retained report draft can explicitly retry using its original ID");

        rig = new Rig(); owner = rig.Context.Current; report = Payload(); rig.Transport.WrongReceipt = true;
        await Reject<ResidentReportUnconfirmedException>(() => rig.Flow.SubmitAsync(owner, report), check, "a mismatched server receipt cannot trigger report Success");
        check(rig.Transport.RestCalls == 0, "a contradictory SDK receipt is not overwritten through the fallback");
        rig = new Rig(); owner = rig.Context.Current; report = Payload(); rig.Transport.DenyRead = true;
        await Reject<UnauthorizedAccessException>(() => rig.Flow.SubmitAsync(owner, report), check, "a read/access denial cannot be mistaken for a missing report and retried anonymously");
        check(rig.Transport.SdkCalls == 0 && rig.Transport.RestCalls == 0, "denied report confirmation never starts another write path");
        rig = new Rig(); owner = rig.Context.Current; report = Payload(); rig.Context.SignedIn = false;
        await Reject<OperationCanceledException>(() => rig.Flow.SubmitAsync(owner, report), check, "a report cannot be submitted using a saved flag without the active account");

        rig = new Rig(); owner = rig.Context.Current; report = Payload();
        var paused = new TaskCompletionSource<ResidentReportReceipt?>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Transport.Insert = (_, _, _) => paused.Task;
        var sending = rig.Flow.SubmitAsync(owner, report);
        rig.Context.Current = owner with { UserId = "resident-b", Revision = 2 };
        paused.SetResult(Receipt(report));
        await Reject<OperationCanceledException>(() => sending, check, "a late report receipt cannot show Success after account switching");
        check(rig.Transport.RestCalls == 0, "an account switch cannot start a retry under another identity");
        rig = new Rig(); owner = rig.Context.Current; report = Payload();
        using var cancellation = new CancellationTokenSource();
        rig.Transport.Insert = (_, _, _) => { cancellation.Cancel(); return Task.FromResult<ResidentReportReceipt?>(Receipt(report)); };
        await Reject<OperationCanceledException>(() => rig.Flow.SubmitAsync(owner, report, cancellation.Token), check, "cancelling or leaving a report form suppresses a late success receipt");

        rig = new Rig(); owner = rig.Context.Current; report = Payload();
        int uploads = 0; var media = new ResidentReportMediaDraft(rig.Context);
        Task<string> Upload(CancellationToken _) { uploads++; return Task.FromResult("https://example.test/photo.jpg"); }
        string url = await media.PrepareAsync(owner, "photo-selection-a", Upload);
        check(url.StartsWith("https://") && uploads == 1, "report media requires a confirmed secure upload URL");
        check(await media.PrepareAsync(owner, "photo-selection-a", Upload) == url && uploads == 1, "retry of the same selected report photo reuses its confirmed upload");
        await media.PrepareAsync(owner, "photo-selection-b", Upload);
        check(uploads == 2, "choosing a different report photo cannot reuse the prior photo URL");
        check(await media.PrepareAsync(owner, null, Upload) == "" && uploads == 2, "removing a report attachment clears its selected upload without posting");
        await Reject<ResidentReportUnconfirmedException>(() => media.PrepareAsync(owner, "failed-photo", _ => Task.FromResult("")), check, "failed report photo upload cannot be submitted as a successful attachment");
        var uploading = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparing = media.PrepareAsync(owner, "late-photo", _ => uploading.Task);
        rig.Context.Current = owner with { Server = "https://other.example", Revision = 2 }; uploading.SetResult("https://example.test/late.jpg");
        await Reject<OperationCanceledException>(() => preparing, check, "a server change rejects a late report photo URL before it can be submitted");

        report = Payload() with { MediaUrl = "https://example.test/image.jpg" };
        var body = JObject.Parse(ResidentReportWire.Serialize(report));
        check((string?)body["id"] == report.Id && (double?)body["latitude"] == Point.Latitude && (double?)body["longitude"] == Point.Longitude, "the actual report serializer preserves stable ID and chosen coordinates without substitution");
        check((string?)body["status"] == "Pending" && body["StatusBadgeColor"] is null && body["Point"] is null && body["MediaType"] is null, "report submission sends only stored fields and always begins in moderation");
        var roundTrip = ResidentReportWire.ReadReceipt("[" + body + "]");
        check(roundTrip?.Id == report.Id && roundTrip.MediaUrl == report.MediaUrl, "REST confirmation reads the stored ID and media URL through the installed SDK mapping");
        owner = new("https://reports.example", "resident-a", 1);
        using var request = ResidentReportWire.Request(owner, report, "public-app-key", "verified-session-token");
        check(request.Headers.Authorization?.Parameter == "verified-session-token" && request.Headers.GetValues("apikey").Single() == "public-app-key", "the REST request authenticates with the resident token while using the app key only as apikey");
        check(request.RequestUri?.AbsoluteUri == "https://reports.example/rest/v1/community_reports" && JObject.Parse(await request.Content!.ReadAsStringAsync())["id"]?.ToString() == report.Id, "fallback request keeps the captured project endpoint and report ID");
        await Reject<OperationCanceledException>(() => Task.FromResult(ResidentReportWire.Request(owner, report, "public-app-key", "")), check, "the report fallback cannot construct an anonymous request when the resident token is absent");
    }
    private static ResidentReportReceipt Receipt(ResidentReportPayload report) => new(report.Id, report.Title, report.Description, report.Category, report.Address,
        report.Point.Latitude, report.Point.Longitude, report.PostedBy, report.Severity, report.MediaUrl, "Pending");
    private static async Task Reject<T>(Func<Task> action, Action<bool, string> check, string name) where T : Exception
    { bool rejected = false; try { await action(); } catch (T) { rejected = true; } check(rejected, name); }
    private sealed class Rig
    {
        public Context Context { get; } = new(); public Transport Transport { get; } = new(); public ResidentReportSubmissionFlow Flow { get; }
        public Rig() => Flow = new(Context, Transport);
    }
    private sealed class Context : IResidentReportContext
    {
        public ResidentReportScope Current { get; set; } = new("https://reports.example", "resident-a", 1); public bool SignedIn = true;
        public ResidentReportScope Capture() => Current;
        public void Require(ResidentReportScope scope, CancellationToken token = default) { token.ThrowIfCancellationRequested(); if (!SignedIn || scope != Current) throw new OperationCanceledException(); }
    }
    private sealed class Transport : IResidentReportTransport
    {
        public Dictionary<string, ResidentReportReceipt> Rows { get; } = new();
        public int SdkCalls, RestCalls, Reads; public bool FailSdk, LoseSdkResponse, EmptySdk, EmptyRest, WrongReceipt, DenyRead;
        public string LastRestId = ""; public ResidentReportScope LastScope;
        public Func<ResidentReportScope, ResidentReportPayload, CancellationToken, Task<ResidentReportReceipt?>>? Insert;
        public Task<ResidentReportReceipt?> FindAsync(ResidentReportScope owner, string id, CancellationToken token)
        { Reads++; LastScope = owner; if (DenyRead) throw new UnauthorizedAccessException(); return Task.FromResult(Rows.GetValueOrDefault(id)); }
        public Task<ResidentReportReceipt?> InsertSdkAsync(ResidentReportScope owner, ResidentReportPayload report, CancellationToken token)
        {
            SdkCalls++; LastScope = owner; if (Insert != null) return Insert(owner, report, token);
            if (FailSdk) throw new HttpRequestException("SDK transport failed"); if (EmptySdk) return Task.FromResult<ResidentReportReceipt?>(null);
            var receipt = Receipt(report) with { Description = WrongReceipt ? "different contents" : report.Description }; Rows[report.Id] = receipt;
            if (LoseSdkResponse) throw new HttpRequestException("response lost"); return Task.FromResult<ResidentReportReceipt?>(receipt);
        }
        public Task<ResidentReportReceipt?> InsertRestAsync(ResidentReportScope owner, ResidentReportPayload report, CancellationToken token)
        { RestCalls++; LastScope = owner; LastRestId = report.Id; if (EmptyRest) return Task.FromResult<ResidentReportReceipt?>(null); var receipt = Receipt(report); Rows[report.Id] = receipt; return Task.FromResult<ResidentReportReceipt?>(receipt); }
    }
}
