using RescuAR.App.Services.Storage;
using RescuAR.Navigation.Guidance;

namespace RescuAR.App.Services.Evacuation;

public enum DirectoryOrigin { Historical, Saved, Live, Previous }
public sealed record EvacuationDirectorySnapshot(string SourceKey, long Revision,
    IReadOnlyList<CachedEvacuationCenterItem> Centers, DirectoryOrigin Origin, DateTimeOffset? UpdatedAt, string Notice);

public interface IEvacuationDirectorySource
{
    string SourceKey { get; }
    bool IsOnline { get; }
    EvacuationCenterCacheEnvelope? Load();
    Task<IReadOnlyList<CachedEvacuationCenterItem>> FetchAsync(CancellationToken token);
    void Save(string expectedSourceKey, IReadOnlyList<CachedEvacuationCenterItem> centers, DateTimeOffset updatedAt);
}

/// <summary>One directory snapshot for list, search, map pins and repository consumers.</summary>
public sealed class EvacuationDirectory
{
    private readonly IEvacuationDirectorySource source;
    private readonly Func<DateTimeOffset> clock;
    private readonly SemaphoreSlim refreshing = new(1, 1);
    private readonly object sync = new();
    private long revision;
    private EvacuationDirectorySnapshot current;
    private DateTimeOffset lastAttempt;
    public event Action<EvacuationDirectorySnapshot>? Changed;

    public CachedEvacuationCenterItem? ResolveSelected(string id, double latitude, double longitude)
    {
        var found = Current.Centers.FirstOrDefault(center => center.Id == id && center.Latitude == latitude && center.Longitude == longitude);
        return found is null ? null : Copy(found);
    }

    public EvacuationDirectory(IEvacuationDirectorySource source, Func<DateTimeOffset> clock)
    {
        this.source = source; this.clock = clock;
        current = Restore(source.SourceKey);
    }

    public EvacuationDirectorySnapshot Current
    {
        get
        {
            lock (sync)
            {
                if (current.SourceKey != source.SourceKey) { current = Restore(source.SourceKey); lastAttempt = default; }
                return current;
            }
        }
    }

    public async Task<EvacuationDirectorySnapshot> RefreshAsync(bool force = false, CancellationToken token = default)
    {
        await refreshing.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var prior = Current; string key = prior.SourceKey;
            if (!source.IsOnline) return Publish(prior with { Origin = Retained(prior.Origin),
                Notice = Label(Retained(prior.Origin), prior.UpdatedAt, prior.Centers.Count) + " · offline" }, key);
            if (!force && clock() - lastAttempt < TimeSpan.FromSeconds(30)) return prior;
            lastAttempt = clock();
            try
            {
                var received = await source.FetchAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested(); RequireSource(key);
                var centers = Valid(received);
                // A real empty response is authoritative; a nonempty unusable response is a failure.
                if (received.Count > 0 && centers.Count == 0) throw new InvalidDataException("The directory contains no usable location records.");
                var updatedAt = clock();
                string notice = Label(DirectoryOrigin.Live, updatedAt, centers.Count);
                if (received.Count > centers.Count) notice += $" · {received.Count - centers.Count} invalid/duplicate records omitted";
                try { source.Save(key, centers, updatedAt); }
                catch { notice += " · offline copy could not be updated"; }
                token.ThrowIfCancellationRequested(); RequireSource(key);
                return Publish(new(key, 0, centers, DirectoryOrigin.Live, updatedAt, notice), key);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                token.ThrowIfCancellationRequested(); RequireSource(key);
                var origin = Retained(prior.Origin);
                return Publish(prior with { Origin = origin, Notice = Label(origin, prior.UpdatedAt, prior.Centers.Count) + " · refresh failed" }, key);
            }
        }
        finally { refreshing.Release(); }
    }

    private EvacuationDirectorySnapshot Restore(string key)
    {
        try
        {
            var saved = source.Load();
            if (saved is not null && (saved.SourceKey.Length == 0 || saved.SourceKey == key))
            {
                var valid = Valid(saved.Centers);
                if (valid.Count > 0 || (saved.DirectoryConfirmed && saved.Centers.Count == 0))
                {
                    DateTimeOffset? at = saved.LastUpdatedUtc > DateTimeOffset.UnixEpoch ? saved.LastUpdatedUtc : null;
                    return new(key, ++revision, valid, DirectoryOrigin.Saved, at, Label(DirectoryOrigin.Saved, at, valid.Count));
                }
            }
        }
        catch { /* A missing/corrupt saved copy cannot become live directory evidence. */ }
        var historical = Historical();
        return new(key, ++revision, historical, DirectoryOrigin.Historical, null, Label(DirectoryOrigin.Historical, null, historical.Count));
    }

    private void RequireSource(string key)
    {
        if (key != source.SourceKey) throw new OperationCanceledException("The directory server changed.");
    }
    private EvacuationDirectorySnapshot Publish(EvacuationDirectorySnapshot next, string key)
    {
        lock (sync) { RequireSource(key); current = next with { Revision = ++revision }; next = current; }
        if (Changed is { } listeners)
            foreach (Action<EvacuationDirectorySnapshot> listener in listeners.GetInvocationList())
                try { listener(next); } catch { /* A detached view cannot fail another consumer's directory refresh. */ }
        return next;
    }
    public static string Label(DirectoryOrigin origin, DateTimeOffset? updatedAt, int count) => origin switch
    {
        DirectoryOrigin.Historical => "Historical locations · current availability unknown",
        DirectoryOrigin.Saved => $"Saved directory{(updatedAt is { } at ? " · " + at.ToLocalTime().ToString("MMM d, HH:mm") : " · date unknown")}{(count == 0 ? " · no centers recorded" : "")}",
        DirectoryOrigin.Previous => $"Previously loaded directory{(updatedAt is { } at ? " · " + at.ToLocalTime().ToString("MMM d, HH:mm") : "")}{(count == 0 ? " · no centers recorded" : "")}",
        _ => count == 0 ? "Directory updated · no centers listed" : "Directory updated · live availability unverified"
    };
    private static DirectoryOrigin Retained(DirectoryOrigin origin) => origin == DirectoryOrigin.Live ? DirectoryOrigin.Previous : origin;

    private static IReadOnlyList<CachedEvacuationCenterItem> Valid(IEnumerable<CachedEvacuationCenterItem> centers) => centers
        .Where(center => !string.IsNullOrWhiteSpace(center.Id) && !string.IsNullOrWhiteSpace(center.Name)
            && double.IsFinite(center.Latitude) && double.IsFinite(center.Longitude)
            && Math.Abs(center.Latitude) <= 85.05112878 && Math.Abs(center.Longitude) <= 180
            && (center.Latitude != 0 || center.Longitude != 0))
        .GroupBy(center => center.Id, StringComparer.Ordinal).Select(group => Copy(group.First()))
        .OrderBy(center => center.Name, StringComparer.OrdinalIgnoreCase).ThenBy(center => center.Id, StringComparer.Ordinal).ToArray();

    public static CachedEvacuationCenterItem Copy(CachedEvacuationCenterItem item) => new()
    {
        Id = item.Id, Name = item.Name, Barangay = item.Barangay, Classification = item.Classification,
        Address = item.Address, VerifiedBy = item.VerifiedBy, FacilityImageUrl = item.FacilityImageUrl, MapImageSource = item.MapImageSource,
        Latitude = item.Latitude, Longitude = item.Longitude, Capacity = item.Capacity, CurrentEvacuees = item.CurrentEvacuees,
        Status = item.Status, HeadOfficer = item.HeadOfficer, Contact = item.Contact, Facilities = item.Facilities?.ToList() ?? new()
    };

    private static IReadOnlyList<CachedEvacuationCenterItem> Historical() => BundledEvacuationCenterCatalog.Centers.Select((center, index) => new CachedEvacuationCenterItem
    {
        Id = "historical:" + (index + 1), Name = center.Name, Barangay = center.Barangay, Classification = center.Classification,
        Address = center.Address, Latitude = center.Coordinate.Latitude, Longitude = center.Coordinate.Longitude,
        Capacity = 0, CurrentEvacuees = 0, Status = "Unknown", VerifiedBy = "Historical location", HeadOfficer = "Not recorded", Contact = "Not recorded", Facilities = new()
    }).ToArray();
}
