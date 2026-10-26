using System.Net;
using System.Numerics;
using System.Text.Json;
using RescuAR.AR;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Routing;

internal static class MldGuidanceOwnershipChecks
{
    private const long Trip = 4100;
    private const double Yaw = -145.6;
    private static readonly ARRenderGenerationToken Generation = new(4101, 4102, 4103);

    public static async Task RunAsync(Action<bool, string> check)
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MldCBenitezResponse.json"));
        using var document = JsonDocument.Parse(json);
        var source = document.RootElement.GetProperty("routes")[0].GetProperty("geometry")
            .GetProperty("coordinates").EnumerateArray()
            .Select(p => new GeoCoordinate(p[1].GetDouble(), p[0].GetDouble())).ToArray();
        using var handler = new RecordedResponse(json);
        using var client = new HttpClient(handler);
        var route = await new MLDRoutingService(client, "https://offline.invalid")
            .FindRouteAsync(source[0], source[^1]);
        check(route is { Points.Count: 6 } && route.Points.Select(p => p.Coordinate).SequenceEqual(source),
            "MLD: the production HTTP parser preserves the website's six C. Benitez coordinates in order");
        check(route is not null && Math.Abs(route.TotalDistanceMeters - 250.660843) < .001 &&
            document.RootElement.GetProperty("routes")[0].GetProperty("distance").GetDouble() == 251,
            "MLD: progress follows the precise geometry length rather than the server's rounded 251 metres");

        var previous = ARRenderGenerationBridge.Current;
        bool accepting = ARRenderGenerationBridge.IsAcceptingFrames;
        ARRenderGenerationBridge.Activate(Generation);
        MldGuidanceBridge.SetRouteOwner(null, 0);
        ARRouteBridge.Clear();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var tracker = new RouteProgressTracker();
            tracker.SetRoute(route!);
            var fix = tracker.Update(source[0], 5);
            var physical = new RouteDisplayProgressPolicy();
            physical.ObserveGps(route!, fix, now);
            var motion = new RouteBacktrackingPolicy.TrackedMotion(3, -9, Yaw,
                Generation.SessionGeneration, 1, 2, 0);
            physical.ObserveRegistration(route!, fix, now, now, motion);
            var publisher = new MLDARIntegrationService(new UnusedRouter());

            (ARRouteBridge.RouteSnapshot Window, ARCameraPoseBridge.SpatialSnapshot Frame) PublishAt(
                double progress, long ground = 1, double metres = 40, bool renewOrigin = false)
            {
                if (!RouteDisplayProgressPolicy.TryAtDistance(route!, progress, out var position) ||
                    !physical.TryGetRegisteredReference(route!, position.RoadCoordinate, now, motion, out var reference))
                    throw new InvalidOperationException("The recorded MLD route requires a registered display position.");
                float anchorX = renewOrigin ? reference.WorldX : motion.X;
                float anchorZ = renewOrigin ? reference.WorldZ : motion.Z;
                var frame = Frame(ground, reference.WorldX, reference.WorldZ, anchorX, anchorZ);
                var placement = new ARRoutePlacement(Generation, ground, frame.Version, Environment.TickCount64,
                    anchorX, anchorZ, reference.WorldX, reference.WorldZ, now, "MLD_EXACT_RESPONSE", Yaw,
                    TrackingEpoch: frame.TrackingEpoch, WorldFrame: frame.WorldFrame);
                if (!publisher.PublishProgressWindow(route!, progress, position.RoadCoordinate, Yaw,
                        reference.WorldX - anchorX, reference.WorldZ - anchorZ, metres,
                        sourceSegmentIndex: position.SegmentIndex, placement: placement, registeredReference: reference))
                    throw new InvalidOperationException($"The production MLD publisher rejected the {progress} m fixture.");
                return (ARRouteBridge.Current, frame);
            }

            double maximumError = 0;
            foreach (double progress in new[] { 0d, 40, 80, 120, 160, 200, 240 })
            {
                // Independent windows use nearby renewed floor origins; a static
                // origin 240 m behind the camera must fail the normal local limit.
                var sample = PublishAt(progress, (long)progress + 1, renewOrigin: true);
                check(sample.Window.IsAvailable && sample.Window.Algorithm == route!.Algorithm,
                    $"MLD: the actual registered publisher produces the recorded route window at {progress} m");
                foreach (var point in sample.Window.Points)
                {
                    double x = point.X + sample.Window.Placement.AnchorX - motion.X;
                    double z = point.Z + sample.Window.Placement.AnchorZ - motion.Z;
                    double angle = Yaw * Math.PI / 180;
                    double east = x * Math.Cos(angle) - z * Math.Sin(angle);
                    double north = -x * Math.Sin(angle) - z * Math.Cos(angle);
                    var restored = new GeoCoordinate(source[0].Latitude + north / 6371008.8 * 180 / Math.PI,
                        source[0].Longitude + east / (6371008.8 * Math.Cos(source[0].Latitude * Math.PI / 180)) * 180 / Math.PI);
                    maximumError = Math.Max(maximumError,
                        source.Zip(source.Skip(1), (a, b) => DistanceToSegment(restored, a, b)).Min());
                }
            }
            check(maximumError < .01,
                $"MLD: source-to-registered-AR round trip retains the website polyline (maximum cross-track {maximumError:F6} m)");

            void Bind((ARRouteBridge.RouteSnapshot Window, ARCameraPoseBridge.SpatialSnapshot Frame) sample,
                long calibration = 1, bool hazard = false) =>
                MldGuidanceBridge.RebindPublishedWindow(route!, Trip, calibration, sample.Window, sample.Frame,
                    hazard, hazard ? "Stop before the reported hazard" : null);
            MldGuidanceBridge.Snapshot Decide(
                (ARRouteBridge.RouteSnapshot Window, ARCameraPoseBridge.SpatialSnapshot Frame) sample,
                bool allow = true, long calibration = 1, bool hazard = false, string message = "Follow the mapped route") =>
                MldGuidanceBridge.Publish(route!, Trip, calibration, sample.Window, sample.Frame, allow, message, hazard);
            bool Render((ARRouteBridge.RouteSnapshot Window, ARCameraPoseBridge.SpatialSnapshot Frame) sample) =>
                MldGuidanceBridge.CanRenderPath(sample.Window, sample.Frame, Trip);

            MldGuidanceBridge.SetRouteOwner(route, Trip);
            check(MldGuidanceBridge.Current is { State: MldGuidanceBridge.Phase.Preparing, AllowsPath: false } &&
                !MldGuidanceBridge.AllowsStandaloneArrow,
                "MLD: computing a route creates one preparing owner without an independent road/destination arrow");
            var first = PublishAt(0);
            check(!Decide(first).AllowsPath, "MLD: matching frame metadata alone cannot authorize an unbound window");
            Bind(first);
            check(!Render(first), "MLD: publishing geometry cannot independently promote preparing guidance");
            check(Decide(first).State == MldGuidanceBridge.Phase.Following && Render(first),
                "MLD: combined qualified evidence enables the bound mapped path and its route arrow together");
            var cue = new ARRoadApproachBridge.Snapshot(true, Vector2.UnitX, source[0], 5, now,
                Generation, 1, ARGroundTrust.Verified, 1000,
                new(Trip, route!.GeometryFingerprint, 1, 2, 0, "MAPPED_TARGET"), new Vector2(10, 0));
            bool FloorCue() => ARRoadApproachBridge.CanRender(cue, Generation, 1,
                ARGroundTrust.Verified, true, now, 1000);
            check(!FloorCue(), "MLD: an otherwise qualified nearest-road arrow cannot compete with the mapped route");

            var next = PublishAt(5);
            check(!Render(next), "MLD: an unbound replacement window cannot inherit the preceding draw permission");
            Bind(next);
            check(Render(next), "MLD: a normal registered progress window stays drawable without waiting for the UI timer");
            var renewed = PublishAt(10, ground: 2, renewOrigin: true);
            check(!Render(renewed), "MLD: a changed floor cannot draw under the preceding floor's authorization");
            Bind(renewed);
            check(Render(renewed), "MLD: validated retained-world floor handoff preserves the selected route's permission");
            Bind(renewed, calibration: 2);
            check(!Render(renewed) && !Decide(renewed, calibration: 1).AllowsPath,
                "MLD: new heading calibration cannot inherit a previous calibration's permission");
            check(Decide(renewed, calibration: 2).AllowsPath,
                "MLD: changed calibration requires a new combined qualified decision");

            ARCameraSpatialController.SetRouteRenderingEnabled(false, "fixture tracking recovery");
            check(!Render(renewed) && !FloorCue() && MldGuidanceBridge.Current.State == MldGuidanceBridge.Phase.Recovering,
                "MLD: recovery withholds the path and every independent navigation arrow together");
            Bind(renewed, calibration: 2);
            check(!Render(renewed), "MLD: another geometry publication cannot independently exit recovery");
            Decide(renewed, false, 2, message: "Checking direction — use the map");
            ARCameraSpatialController.SetRouteRenderingEnabled(false, "consumer repeated recovery");
            check(MldGuidanceBridge.Current.Message == "Checking direction — use the map",
                "MLD: repeated hide requests preserve the owner's specific recovery explanation");

            check(Decide(renewed, false, 2, true, "Hazard on route").State == MldGuidanceBridge.Phase.Blocked,
                "MLD: an unsafe route with no qualified prefix has one blocked presentation");
            var prefix = PublishAt(10, 2, metres: 5, renewOrigin: true);
            Bind(prefix, 2, true);
            check(!Render(prefix), "MLD: publishing a clipped prefix cannot independently lift a hazard block");
            check(Decide(prefix, true, 2, true).State == MldGuidanceBridge.Phase.SafePrefix && Render(prefix) && !FloorCue(),
                "MLD: an explicitly qualified safe prefix permits only its mapped geometry");
            check(!MldGuidanceBridge.CanRenderPath(prefix.Window, prefix.Frame.AsUnavailable("tracking lost"), Trip) &&
                !MldGuidanceBridge.CanRenderPath(prefix.Window, prefix.Frame, Trip + 1),
                "MLD: draw permission immediately rejects tracking loss and a different selected trip");
            check(!MldGuidanceBridge.CanRenderPath(prefix.Window, Frame(3, 3, -9, 3, -9), Trip) &&
                !MldGuidanceBridge.CanRenderPath(prefix.Window, Frame(2, 3, -9, 3, -9, epoch: 3), Trip) &&
                !MldGuidanceBridge.CanRenderPath(prefix.Window, Frame(2, 3, -9, 3, -9, world: 1), Trip) &&
                !MldGuidanceBridge.CanRenderPath(prefix.Window,
                    Frame(2, 3, -9, 3, -9, publishedAt: Environment.TickCount64 - 10000), Trip),
                "MLD: floor, tracking epoch, world frame and stale-pose changes reject the entire mapped presentation");
            ARRenderGenerationBridge.Invalidate();
            check(!Render(prefix), "MLD: suspended frame admission invalidates mapped guidance immediately");
            ARRenderGenerationBridge.Activate(Generation);

            var replacement = new RouteResult(route.Points, route.TotalDistanceMeters, route.Algorithm);
            MldGuidanceBridge.SetRouteOwner(replacement, Trip);
            Bind(prefix, 2);
            Decide(prefix, calibration: 2);
            check(MldGuidanceBridge.Current.State == MldGuidanceBridge.Phase.Preparing && !Render(prefix),
                "MLD: stale callbacks cannot promote a replacement even when its geometry and trip are identical");
            check(!MldGuidanceBridge.Publish(replacement, Trip, 2, prefix.Window, prefix.Frame, true, "Follow").AllowsPath,
                "MLD: the replacement cannot reuse an unbound window from the old route object");
            MldGuidanceBridge.RebindPublishedWindow(replacement, Trip, 2, prefix.Window, prefix.Frame, false, null);
            MldGuidanceBridge.Publish(replacement, Trip, 2, prefix.Window, prefix.Frame, true, "Follow");
            MldGuidanceBridge.MarkArrived(Trip);
            MldGuidanceBridge.Publish(replacement, Trip, 2, prefix.Window, prefix.Frame, true, "Late callback");
            check(MldGuidanceBridge.Current.State == MldGuidanceBridge.Phase.Arrived && !Render(prefix) && !FloorCue(),
                "MLD: arrival is terminal for the trip and cannot be undone by a late ready callback");
            MldGuidanceBridge.SetRouteOwner(null, 0);
            Decide(prefix, calibration: 2);
            check(!MldGuidanceBridge.Current.Active && !MldGuidanceBridge.AllowsStandaloneArrow,
                "MLD: clearing the owner rejects stale decisions and an orphaned MLD window cannot enable an independent arrow");
            ARRouteBridge.Clear();
            MldGuidanceBridge.SetRouteOwner(new RouteResult(route.Points, route.TotalDistanceMeters, "AStar"), Trip + 1);
            check(MldGuidanceBridge.AllowsStandaloneArrow && FloorCue(),
                "AStar: selecting a non-MLD route releases MLD ownership and preserves its existing access-cue contract");
        }
        finally
        {
            MldGuidanceBridge.SetRouteOwner(null, 0);
            ARRouteBridge.Clear();
            ARCameraSpatialController.SetRouteRenderingEnabled(false, "MLD fixture finished");
            if (accepting) ARRenderGenerationBridge.Activate(previous); else ARRenderGenerationBridge.Invalidate();
        }
    }

    private static ARCameraPoseBridge.SpatialSnapshot Frame(long ground, float x, float z, float anchorX,
        float anchorZ, long epoch = 2, long world = 0, long? publishedAt = null) =>
        new(1, new(Generation, 1, new(Generation.GeometryGeneration, 0, 1080, 1920)),
            publishedAt ?? Environment.TickCount64, true, "NONE", new(true, x, 1.4f, z, 0, 0, 0, 1, 1),
            default, new(new(1, Generation, ground, ARGroundLifecycleState.Verified, ARGroundTrust.Verified,
                Environment.TickCount64, "fixture"), anchorX, 0, anchorZ), epoch, world);

    private static double DistanceToSegment(GeoCoordinate point, GeoCoordinate a, GeoCoordinate b)
    {
        double scale = Math.Cos(point.Latitude * Math.PI / 180);
        double ax = (a.Longitude - point.Longitude) * scale, ay = a.Latitude - point.Latitude;
        double dx = (b.Longitude - a.Longitude) * scale, dy = b.Latitude - a.Latitude;
        double t = Math.Clamp(-(ax * dx + ay * dy) / (dx * dx + dy * dy), 0, 1);
        return Math.Sqrt(Math.Pow(ax + t * dx, 2) + Math.Pow(ay + t * dy, 2)) * 6371008.8 * Math.PI / 180;
    }

    private sealed class RecordedResponse(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    private sealed class UnusedRouter : IRoutingService
    {
        public string AlgorithmName => "Unused";
        public Task<RouteResult?> FindRouteAsync(GeoCoordinate from, GeoCoordinate to, CancellationToken token = default) =>
            throw new InvalidOperationException("The recorded MLD replay must not make a network request.");
    }
}
