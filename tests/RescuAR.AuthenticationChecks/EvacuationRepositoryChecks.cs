using RescuAR.App.Services.Evacuation;
using RescuAR.App.Services.Storage;

internal static class EvacuationRepositoryChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var source = new Source
        {
            Rows = Enumerable.Range(0, 1201).Select(index => new CachedEvacuationCenterItem
            {
                Id = $"server-center-{index}", Name = $"Shelter {index}", Latitude = 14.65,
                Longitude = 121.1, Capacity = 1000, Status = "Standby"
            }).ToArray()
        };
        var directory = new EvacuationDirectory(source, () => DateTimeOffset.UtcNow);
        EvacuationDirectoryRuntime.Current = directory;
        await directory.RefreshAsync(force: true);
        var centers = EvacuationCenterRepository.GetEvacuationCenters();
        check(centers.Count == 1201 && centers.Any(center => center.Id == "server-center-1200"),
            "map repository exposes the entire live directory without eight-, sixteen- or thousand-row caps");
        check(centers.Select(center => center.Id).Distinct().Count() == 1201,
            "map entries preserve server identities rather than generating replacement numeric IDs");

        source.Rows = new[] { new CachedEvacuationCenterItem
        {
            Id = "bayanihan-server-id", Name = "Barangay Bayanihan Hall", Latitude = 14.65,
            Longitude = 121.1, Capacity = 1000, CurrentEvacuees = 17, Status = "Standby"
        } };
        source.SaveFails = true;
        await directory.RefreshAsync(force: true);
        var bayanihan = EvacuationCenterRepository.GetEvacuationCenters().Single();
        check(bayanihan.Name == "Barangay Bayanihan Hall" && bayanihan.Id == "bayanihan-server-id"
            && bayanihan.Capacity == 1000 && bayanihan.Occupancy == 17,
            "map immediately sees newly fetched shelters and metadata even if the offline copy cannot be saved");

        source.Rows = Array.Empty<CachedEvacuationCenterItem>();
        source.SaveFails = false;
        await directory.RefreshAsync(force: true);
        check(EvacuationCenterRepository.GetEvacuationCenters().Count == 0,
            "confirmed empty database results clear the map repository without restoring stale or hardcoded shelters");
    }

    private sealed class Source : IEvacuationDirectorySource
    {
        public string SourceKey => "test-directory";
        public bool IsOnline => true;
        public IReadOnlyList<CachedEvacuationCenterItem> Rows { get; set; } = Array.Empty<CachedEvacuationCenterItem>();
        public bool SaveFails { get; set; }
        public EvacuationCenterCacheEnvelope? Load() => null;
        public Task<IReadOnlyList<CachedEvacuationCenterItem>> FetchAsync(CancellationToken token) => Task.FromResult(Rows);
        public void Save(string expectedSourceKey, IReadOnlyList<CachedEvacuationCenterItem> centers, DateTimeOffset updatedAt)
        {
            if (SaveFails) throw new IOException("Simulated cache write failure.");
        }
    }
}

namespace RescuAR.App.Services.Evacuation
{
    // The application binds this runtime to Supabase; unit checks use the same
    // directory coordinator and repository with a local source.
    public static class EvacuationDirectoryRuntime
    {
        public static EvacuationDirectory Current { get; set; } = null!;
    }
}
