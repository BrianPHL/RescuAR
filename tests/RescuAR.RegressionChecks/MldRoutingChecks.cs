using System.Globalization;
using System.Net;
using System.Text.Json;
using RescuAR.AR;
using RescuAR.Navigation.Hazards;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Routing;

internal static class MldRoutingChecks
{
    private static GeoCoordinate C(double east, double north = 0) => new(north / 111_195, east / 111_195);

    public static async Task RunAsync(Action<bool, string> check)
    {
        using var handler = new Alternatives();
        using var client = new HttpClient(handler);
        var mld = new MLDRoutingService(client, "https://routing.unit.test");
        var hybrid = new HybridRoutingService(mld,
            _ => throw new InvalidOperationException("Healthy MLD must not initialize the offline graph"), () => true);
        var first = await hybrid.FindRouteAsync(C(0), C(100));
        check(first is { Provenance.Provider: "MLD", Points.Count: 2 } && first.TotalDistanceMeters is > 99 and < 101,
            "MLD: the lower evacuation-cost alternative wins over the server's first backward/two-turn detour");
        check(handler.Urls.Single().Query.Contains("alternatives=3") &&
                handler.Urls.Single().Query.Contains("radiuses=20;50"),
            "MLD: ordinary requests ask for bounded alternatives without relaxing road matching");
        check(handler.FreshRequests.All(value => value),
            "MLD: route requests instruct the transport and proxies to bypass stored responses");

        long window = ARRouteBridge.Current.Version;
        var replacement = await hybrid.FindReplacementRouteResultAsync(first!, C(20), C(100),
            _ => throw new InvalidOperationException("A matched MLD origin must not repeat road-access search"));
        check(handler.Urls.Count == 2 && handler.Urls[0].AbsolutePath != handler.Urls[1].AbsolutePath,
            "MLD: recalculation makes a second HTTP request using the new real origin");
        check(replacement.Route is { Provenance.Provider: "MLD" } next &&
                next.Provenance.RequestedCoordinates![0] == C(20) && next.TotalDistanceMeters is > 79 and < 81,
            "MLD: the recalculated route uses the new API response rather than slicing the accepted route");
        check(first!.Points[0].Coordinate == C(0) && ARRouteBridge.Current.Version == window,
            "MLD: API replanning leaves the active geographic route and AR window untouched until acceptance");

        handler.PreferLonger = true;
        var preferred = await mld.FindRouteAsync(C(0), C(100));
        check(preferred is not null && preferred.TotalDistanceMeters > 200,
            "MLD: a longer lower-cost evacuation alternative is not replaced by an inconsistent distance-only shortcut");
        handler.OmitWeights = true;
        check((await mld.FindRouteAsync(C(0), C(100)))!.TotalDistanceMeters > 200,
            "MLD: missing comparable weights preserve the server's recommended route order");
        handler.PreferLonger = handler.OmitWeights = false;

        handler.HazardCase = true;
        var hazard = new RouteHazard("closed-road", C(50), 10, "flood", "high", "fixture", "fixture", DateTimeOffset.UtcNow);
        var safe = await mld.FindRouteAvoidingHazardsAsync(C(0), C(100), [hazard]);
        check(safe is not null && RouteHazardGeometry.RouteAvoidsHazardsFromOrigin(safe, C(0), [hazard]),
            "MLD: evacuation-cost preference cannot select a walking alternative through a hazard");
        check(safe is { Points.Count: 4 } && safe.Points[1].Coordinate == C(0, 20) && safe.TotalDistanceMeters < 141,
            "MLD: hazard routing chooses the lowest-cost safe returned alternative");

        handler.NoRoute = true;
        check(await hybrid.FindRouteAsync(C(0), C(100)) is null,
            "MLD: a genuine API NoRoute stays NoRoute rather than fabricating a straight shortcut");
        check(handler.FreshRequests.Count == handler.Urls.Count && handler.FreshRequests.All(value => value),
            "MLD: initial, replacement and hazard requests all reach the HTTP transport with cache bypass");
        check(handler.Urls.All(uri => uri.Query.Contains("steps=false") &&
                uri.Query.Contains("overview=full") && uri.Query.Contains("geometries=geojson")),
            "MLD: every route request omits unused turn steps while retaining full geographic geometry");
    }

    private sealed class Alternatives : HttpMessageHandler
    {
        public List<Uri> Urls { get; } = [];
        public List<bool> FreshRequests { get; } = [];
        public bool HazardCase { get; set; }
        public bool NoRoute { get; set; }
        public bool PreferLonger { get; set; }
        public bool OmitWeights { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            Urls.Add(uri);
            FreshRequests.Add(request.Method == HttpMethod.Get &&
                request.Headers.CacheControl is { NoCache: true, NoStore: true });
            var pair = uri.AbsolutePath.Split('/')[4].Split(';')[0].Split(',');
            var origin = new GeoCoordinate(double.Parse(pair[1], CultureInfo.InvariantCulture),
                double.Parse(pair[0], CultureInfo.InvariantCulture));
            GeoCoordinate[][] paths = HazardCase
                ? [[origin, C(0, 60), C(100, 60), C(100)], [origin, C(100)], [origin, C(0, 20), C(100, 20), C(100)]]
                : [[origin, C(-30), C(-30, 40), C(100, 40), C(100)], [origin, C(100)]];
            string json = NoRoute ? "{\"code\":\"NoRoute\",\"routes\":[]}" : JsonSerializer.Serialize(new
            {
                code = "Ok",
                // Costs encode the shared walking profile's preferences.
                routes = paths.Select((points, index) => new { distance = index + 1,
                    weight_name = "duration",
                    weight = OmitWeights ? (double?)null : HazardCase ? new double[] { 300, 100, 140 }[index]
                        : PreferLonger ? new double[] { 50, 100 }[index] : new double[] { 300, 100 }[index],
                    geometry = new { coordinates = points.Select(point => new[] { point.Longitude, point.Latitude }) } })
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
