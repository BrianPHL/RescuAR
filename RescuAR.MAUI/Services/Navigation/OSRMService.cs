using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RescuAR.App.Services.Navigation
{
    public class OSRMService : IRoutingProvider
    {
        private readonly HttpClient _client = new HttpClient();

        public async Task<object> CalculateRouteAsync(object startCoordinate, object endCoordinate)
        {
            // Cast to Microsoft.Maui.Devices.Sensors.Location
            var start = (Microsoft.Maui.Devices.Sensors.Location)startCoordinate;
            var end   = (Microsoft.Maui.Devices.Sensors.Location)endCoordinate;

            // OSRM expects longitude,latitude order
            string url = $"http://127.0.0.1:5000/route/v1/driving/" +
                         $"{start.Longitude},{start.Latitude};" +
                         $"{end.Longitude},{end.Latitude}" +
                         $"?overview=full&geometries=geojson";

            var response = await _client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            // Return the raw JSON document for now; swap for a proper DTO when ready.
            return JsonDocument.Parse(json);
        }
    }
}
