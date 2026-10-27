using System.Globalization;
using RescuAR.App.Models;
using RescuAR.App.Services.Storage;
using RescuAR.Services;

namespace RescuAR.App.Services.Evacuation;

public static class EvacuationDirectoryRuntime
{
    public static EvacuationDirectory Current { get; } = new(new MauiEvacuationDirectorySource(), () => DateTimeOffset.UtcNow);
}

internal sealed class MauiEvacuationDirectorySource : IEvacuationDirectorySource
{
    public string SourceKey => SupabaseService.Instance.SupabaseUrl.TrimEnd('/');
    public bool IsOnline => Microsoft.Maui.Networking.Connectivity.Current.NetworkAccess == Microsoft.Maui.Networking.NetworkAccess.Internet;
    public EvacuationCenterCacheEnvelope? Load() => CacheService.LoadEvacuationCenters();
    public void Save(string expectedSourceKey, IReadOnlyList<CachedEvacuationCenterItem> centers, DateTimeOffset updatedAt)
    {
        if (SourceKey != expectedSourceKey) throw new OperationCanceledException("The directory server changed.");
        var previous = Load();
        var hotlines = previous is not null && (previous.SourceKey.Length == 0 || previous.SourceKey == SourceKey) ? previous.Hotlines : new();
        if (!CacheService.TrySaveEvacuationCenters(centers, hotlines, updatedAt, expectedSourceKey))
            throw new IOException("The offline directory could not be saved.");
    }
    public async Task<IReadOnlyList<CachedEvacuationCenterItem>> FetchAsync(CancellationToken token)
    {
        string key = SourceKey;
        var service = SupabaseService.Instance;
        var client = await service.GetClientAsync().WaitAsync(token) ?? throw new IOException("Directory server unavailable.");
        var rows = new List<CachedEvacuationCenterItem>();
        // Fetch every page rather than relying on the server's default response limit.
        for (int offset = 0; ; offset += 500)
        {
            var response = await client.From<SupabaseEvacuationCenter>().Order("name", Supabase.Postgrest.Constants.Ordering.Ascending)
                .Order("id", Supabase.Postgrest.Constants.Ordering.Ascending).Range(offset, offset + 499).Get(token);
            token.ThrowIfCancellationRequested();
            if (SourceKey != key || !ReferenceEquals(client, service.Client)) throw new OperationCanceledException("The directory server changed.");
            if (response?.Models is null) throw new InvalidDataException("The directory response was unavailable.");
            foreach (var model in response.Models)
            {
                if (!double.TryParse(model.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude)) latitude = double.NaN;
                if (!double.TryParse(model.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude)) longitude = double.NaN;
                rows.Add(new()
                {
                    Id = model.Id ?? "", Name = model.Name ?? "", Barangay = model.Barangay ?? "", Classification = model.Classification ?? "Unspecified",
                    Address = model.Barangay ?? "", VerifiedBy = "Directory record", Latitude = latitude, Longitude = longitude,
                    Capacity = Math.Max(0, model.Capacity), CurrentEvacuees = Math.Max(0, model.CurrentEvacuees), Status = model.Status ?? "",
                    HeadOfficer = model.HeadOfficer ?? "Not recorded", Contact = model.Contact ?? "Not recorded", FacilityImageUrl = model.ImageUrl ?? "",
                    Facilities = Facilities(model.Facilities)
                });
            }
            if (response.Models.Count < 500) break;
        }
        return rows;
    }
    private static List<string> Facilities(object? value)
    {
        if (value is IEnumerable<string> list) return list.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();
        string? text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return new();
        try { return Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(text) ?? new(); }
        catch { return text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); }
    }
}
