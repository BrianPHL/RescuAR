using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RescuAR.App.Services.Reports;

public interface IOsmGeocodingService
{
    Task<List<OsmSearchResult>> SearchLocationsAsync(string query);
}

public sealed class OsmSearchResult
{
    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }
}

public sealed class OsmGeocodingService : IOsmGeocodingService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static readonly SemaphoreSlim RequestLock = new(1, 1);

    private static readonly Dictionary<string, List<OsmSearchResult>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static DateTime lastRequestUtc = DateTime.MinValue;

    public async Task<List<OsmSearchResult>> SearchLocationsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        string normalizedQuery = query.Trim();

        if (Cache.TryGetValue(normalizedQuery, out var cached))
            return cached;

        await RequestLock.WaitAsync();

        try
        {
            if (Cache.TryGetValue(normalizedQuery, out cached))
                return cached;

            TimeSpan elapsed = DateTime.UtcNow - lastRequestUtc;

            if (elapsed < TimeSpan.FromSeconds(1))
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(1) - elapsed);
            }

            string encodedQuery =
                Uri.EscapeDataString(normalizedQuery);

            string requestUri =
                "https://nominatim.openstreetmap.org/search" +
                "?format=jsonv2" +
                "&limit=8" +
                "&countrycodes=ph" +
                $"&q={encodedQuery}";

            using HttpResponseMessage response =
                await HttpClient.GetAsync(requestUri);

            lastRequestUtc = DateTime.UtcNow;

            response.EnsureSuccessStatusCode();

            string json =
                await response.Content.ReadAsStringAsync();

            List<NominatimResult>? rawResults =
                JsonSerializer.Deserialize<List<NominatimResult>>(json);

            List<OsmSearchResult> results =
                rawResults?
                    .Select(ConvertResult)
                    .Where(result => result is not null)
                    .Cast<OsmSearchResult>()
                    .ToList()
                ?? [];

            Cache[normalizedQuery] = results;

            return results;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"OSM geocoding error: {exception.Message}");

            return [];
        }
        finally
        {
            RequestLock.Release();
        }
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new();

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "RescuAR-Capstone/1.0");

        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(
            "en");

        client.Timeout = TimeSpan.FromSeconds(10);

        return client;
    }

    private static OsmSearchResult? ConvertResult(
        NominatimResult result)
    {
        if (!double.TryParse(
                result.Latitude,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double latitude))
        {
            return null;
        }

        if (!double.TryParse(
                result.Longitude,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double longitude))
        {
            return null;
        }

        string displayName =
            result.DisplayName ?? string.Empty;

        string name = result.Name;

        if (string.IsNullOrWhiteSpace(name))
        {
            name =
                displayName
                    .Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?
                    .Trim()
                ?? displayName;
        }

        return new OsmSearchResult
        {
            Name = name,
            DisplayName = displayName,
            Latitude = latitude,
            Longitude = longitude
        };
    }

    private sealed class NominatimResult
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("lat")]
        public string Latitude { get; set; } = string.Empty;

        [JsonPropertyName("lon")]
        public string Longitude { get; set; } = string.Empty;
    }
}
