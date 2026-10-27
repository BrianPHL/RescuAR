using RescuAR.App.Services.Evacuation;
using RescuAR.App.Services.Storage;

internal static class EvacuationDirectoryChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var now = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        var source = new Source();
        var directory = new EvacuationDirectory(source, () => now);
        check(directory.Current.Origin == DirectoryOrigin.Historical && directory.Current.Centers.Count == 9, "no saved directory uses the approved nine historical locations");
        check(directory.Current.Centers.All(center => center.Capacity == 0 && center.Status == "Unknown" && center.Contact == "Not recorded" && center.Facilities.Count == 0), "historical locations cannot invent capacity, availability, contacts or facilities");
        check(directory.Current.Notice.Contains("Historical") && directory.Current.UpdatedAt is null, "historical data has no invented update time");
        source.Rows = Enumerable.Range(0, 17).Select(index => Center(index.ToString())).ToArray();
        var snapshot = await directory.RefreshAsync();
        check(snapshot.Centers.Count == 17 && source.Reads == 1, "the shared directory accepts more than three centers");
        var id = Guid.NewGuid().ToString();
        source.Rows = new[] { Center(id) };
        now = now.AddMinutes(1);
        snapshot = await directory.RefreshAsync(true);
        var center = snapshot.Centers.Single();
        check(center.Id == id && center.Capacity == 321 && center.CurrentEvacuees == 17 && center.Status == "Standby", "real string IDs and supplied metadata survive the shared directory");
        check(source.Saved?.Centers.Single().Id == id && source.Saved.SourceKey == source.SourceKey && source.Saved.DirectoryConfirmed, "the offline copy keeps the same source and backend identity");
        var restored = new EvacuationDirectory(source, () => now);
        check(restored.Current.Origin == DirectoryOrigin.Saved && restored.Current.Centers.Single().Id == id && restored.Current.UpdatedAt == now, "restart restores the same dated saved directory");
        long revision = snapshot.Revision; int events = 0;
        directory.Changed += _ => events++;
        source.Rows = new[] { Center("new-center") };
        snapshot = await directory.RefreshAsync(true);
        check(snapshot.Revision > revision && events == 1 && snapshot.Centers.Single().Id == "new-center", "directory changes have their own revision and notification independent of navigation trips");
        source.Rows = Array.Empty<CachedEvacuationCenterItem>();
        snapshot = await directory.RefreshAsync(true);
        check(snapshot.Origin == DirectoryOrigin.Live && snapshot.Centers.Count == 0 && snapshot.Notice.Contains("no centers"), "a confirmed empty response clears the directory rather than restoring old pins");
        check(new EvacuationDirectory(source, () => now).Current.Centers.Count == 0, "confirmed empty results survive restart without historical substitution");
        source.Rows = new[] { Center("preserved") };
        await directory.RefreshAsync(true);
        source.Fail = true;
        snapshot = await directory.RefreshAsync(true);
        check(snapshot.Centers.Single().Id == "preserved" && snapshot.Notice.Contains("failed") && source.Saved!.Centers.Single().Id == "preserved", "failed refresh preserves previously loaded and saved records");
        source.Fail = false; source.IsOnline = false; int before = source.Reads;
        snapshot = await directory.RefreshAsync(true);
        check(source.Reads == before && snapshot.Notice.Contains("offline") && snapshot.Centers.Count == 1, "offline access does not make a remote request or fabricate a live response");
        source.IsOnline = true;
        source.Rows = new[] { Center("invalid").WithLatitude(double.NaN) };
        snapshot = await directory.RefreshAsync(true);
        check(snapshot.Centers.Single().Id == "preserved" && snapshot.Notice.Contains("failed"), "a nonempty invalid response is not treated as a confirmed empty directory");
        source.Rows = new[] { Center("same"), Center("same"), Center("bad").WithLatitude(91) };
        snapshot = await directory.RefreshAsync(true);
        check(snapshot.Centers.Count == 1 && snapshot.Notice.Contains("omitted"), "duplicate identities and unprojectable coordinates are omitted explicitly");
        check(directory.ResolveSelected("same", 14.65, 121.1)?.Id == "same" && directory.ResolveSelected("same", 14.66, 121.1) is null && directory.ResolveSelected("removed", 14.65, 121.1) is null,
            "starting a journey requires the same selected identity and current coordinates, never a removed or moved entry");
        source.Rows = new[] { Center("") };
        snapshot = await directory.RefreshAsync(true);
        check(snapshot.Centers.Single().Id == "same", "missing backend IDs are rejected instead of replaced with invented numeric IDs");
        source.Rows = new[] { Center("unsaved") }; source.SaveFails = true;
        snapshot = await directory.RefreshAsync(true);
        check(snapshot.Centers.Single().Id == "unsaved" && snapshot.Notice.Contains("could not be updated") && source.Saved!.Centers.Single().Id == "same", "cache-write failure keeps the live result truthful and preserves the previous saved copy");
        source.SaveFails = false; source.SourceKey = "other-server";
        check(directory.Current.Origin == DirectoryOrigin.Historical && directory.Current.Centers.Count == 9, "a different server cannot inherit the previous server's directory");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        bool cancelled = false; try { await directory.RefreshAsync(true, cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled, "a cancelled owner cannot start a directory request");
        using var savedCancellation = new CancellationTokenSource();
        source.Rows = new[] { Center("committed") }; source.AfterSave = savedCancellation.Cancel;
        snapshot = await directory.RefreshAsync(true, savedCancellation.Token);
        check(savedCancellation.IsCancellationRequested && snapshot.Centers.Single().Id == "committed" && directory.Current.Centers.Single().Id == source.Saved!.Centers.Single().Id,
            "leaving a page during the cache commit cannot leave the shared directory older than its saved copy");
        source.AfterSave = null;
        source.Pause = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = directory.RefreshAsync(true);
        source.SourceKey = "third-server"; source.Pause.SetResult();
        cancelled = false; try { await pending; } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled && source.Saved!.SourceKey != "third-server", "a late response from the old server cannot be published or saved as the new server");
    }
    private static CachedEvacuationCenterItem Center(string id) => new()
    { Id = id, Name = "Center " + id, Latitude = 14.65, Longitude = 121.1, Capacity = 321, CurrentEvacuees = 17, Status = "Standby", Facilities = new() { "Water" } };
    private static CachedEvacuationCenterItem WithLatitude(this CachedEvacuationCenterItem center, double latitude) { center.Latitude = latitude; return center; }
    private sealed class Source : IEvacuationDirectorySource
    {
        public string SourceKey { get; set; } = "test-server";
        public bool IsOnline { get; set; } = true;
        public IReadOnlyList<CachedEvacuationCenterItem> Rows { get; set; } = Array.Empty<CachedEvacuationCenterItem>();
        public EvacuationCenterCacheEnvelope? Saved;
        public int Reads; public bool Fail, SaveFails;
        public TaskCompletionSource? Pause; public Action? AfterSave;
        public EvacuationCenterCacheEnvelope? Load() => Saved;
        public async Task<IReadOnlyList<CachedEvacuationCenterItem>> FetchAsync(CancellationToken token)
        { Reads++; if (Pause is not null) await Pause.Task.WaitAsync(token); if (Fail) throw new IOException(); return Rows; }
        public void Save(string key, IReadOnlyList<CachedEvacuationCenterItem> centers, DateTimeOffset at)
        { if (SaveFails) throw new IOException(); Saved = new() { SourceKey = key, Centers = centers.Select(EvacuationDirectory.Copy).ToList(), DirectoryConfirmed = true, LastUpdatedUtc = at }; AfterSave?.Invoke(); }
    }
}
