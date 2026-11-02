using RescuAR.AR;
using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Timing;

internal static class NavigationDeviceReliabilityChecks
{
    private static GeoCoordinate C(double east) => new(0, east / 111_195);

    public static async Task RunAsync(Action<bool, string> check)
    {
        long mono = Environment.TickCount64;
        var now = NavigationClock.AtMonotonicMilliseconds(mono);
        check(NavigationClock.TryMapElapsedNanoseconds(9_000_000_000, 10_000_000_000, 40_000, out long mapped) && mapped == 39_000,
            "clock: native GPS acquisition maps into the camera clock through a simultaneous clock pair");
        check(!NavigationClock.TryMapElapsedNanoseconds(11_000_000_000, 10_000_000_000, 40_000, out _) &&
            !NavigationClock.TryMapElapsedNanoseconds(0, 10_000_000_000, 40_000, out _),
            "clock: future and unavailable native acquisition times cannot be accepted as fresh");
        var utc = DateTimeOffset.UtcNow.AddHours(6);
        var reading = new LocationReading(C(0), 4, null, 1, 90, utc)
            { Provider = "gps", AcquisitionMonotonicMilliseconds = mono - 1000 };
        check(LocationAcquisitionPolicy.IsCurrent(reading, now) && reading.Timestamp == utc &&
            now - reading.ObservationTime == TimeSpan.FromSeconds(1),
            "clock: a provider/system UTC disagreement cannot reject a fresh native fix or rewrite its original timestamp");
        var stale = reading with { Timestamp = DateTimeOffset.UtcNow, AcquisitionMonotonicMilliseconds = mono - 4000 };
        check(!LocationAcquisitionPolicy.IsCurrent(stale, now) && !AStarLocationAcquisitionPolicy.IsEligible(stale, now, null),
            "clock: recent receipt or UTC metadata cannot revive an old native acquisition for either router");
        check(!LocationAcquisitionPolicy.IsNewObservation(reading with { Timestamp = utc.AddHours(2) }, reading.ObservationTime),
            "clock: changing provider UTC cannot turn the same native observation into new movement evidence");
        check(AStarLocationAcquisitionPolicy.MaximumFixAge == LocationAcquisitionPolicy.MaximumFixAge &&
            ReferenceEquals(LocationAcquisitionPolicy.Select(reading, null, now),
                AStarLocationAcquisitionPolicy.Select(reading, null, null, now, null)),
            "acquisition: both route providers share native freshness and ranking");

        var source = new NativeSource(reading);
        await AStarLocationAcquisitionPolicy.GetAsync(source, false, now.AddSeconds(-2));
        await AStarLocationAcquisitionPolicy.GetAsync(source, true, now.AddSeconds(-2));
        check(source.NavigationCalls == 2 && source.DefaultCalls == 0 && source.NewerThan == now.AddSeconds(-2),
            "acquisition: both routers dispatch to the common native source with the consumed-fix watermark");
        check(await AStarLocationAcquisitionPolicy.GetAsync(source, false, reading.ObservationTime) is null,
            "acquisition: the common source cannot reuse an already consumed native fix");

        Registration(check, now);
        Recovery(check);
        PosePairing(check);
    }

    private static void Registration(Action<bool, string> check, DateTimeOffset at)
    {
        foreach (string algorithm in new[] { "AStar", "MLD (Railway OSRM)" })
        {
            var route = new RouteResult([new(C(0), 0), new(C(100), 100)], 100, algorithm);
            var policy = new RouteDisplayProgressPolicy(requireInitialConfirmation: true);
            RouteDisplayProgressPolicy.RegistrationDecision Observe(double gps, float walked, int seconds)
            {
                var tracker = new RouteProgressTracker(); tracker.SetRoute(route);
                var update = tracker.Update(C(gps), 4);
                policy.ObserveGps(route, update, at.AddSeconds(seconds));
                return policy.ObserveRegistration(route, update, at.AddSeconds(seconds), at.AddSeconds(seconds),
                    new RouteBacktrackingPolicy.TrackedMotion(walked, 0, 0, 1, 1));
            }
            var first = Observe(0, 0, 0);
            check(first.State == "ACQUISITION_CONFIRMATION_PENDING" && policy.RegistrationSupportedAt is null,
                $"{algorithm}: one fix cannot seed the production AR registration");
            var jump = Observe(20, 0, 1);
            check(jump.State == "ACQUISITION_CONFIRMATION_PENDING" && policy.RegistrationSupportedAt is null,
                $"{algorithm}: a 20m GPS jump without physical movement restarts initial confirmation");
            var confirmed = Observe(20.5, .5f, 2);
            check(confirmed.State == "ACQUISITION_SEEDED" && policy.RegistrationSupportedAt == at.AddSeconds(2),
                $"{algorithm}: two spaced observations agreeing with tracked movement can seed registration");
            policy.Reset();
            check(Observe(20.5, .5f, 3).State == "ACQUISITION_CONFIRMATION_PENDING",
                $"{algorithm}: a reset cannot inherit confirmation evidence from the previous registration");
        }
    }

    private static void Recovery(Action<bool, string> check)
    {
        var policy = new NavigationRecoveryPolicy();
        policy.Evaluate(1, NavigationBlockers.Gps, 0);
        check(!policy.Evaluate(1, NavigationBlockers.Heading, 14_999).RecommendMap,
            "recovery: transient waiting does not prematurely report the long-recovery action");
        var longWait = policy.Evaluate(1, NavigationBlockers.Tracking, 15_000);
        check(longWait.RecommendMap && longWait.Message.Contains("Camera tracking") && longWait.Message.Contains("2D Map"),
            "recovery: changing failure causes cannot reset a continuous 15-second wait");
        policy.Evaluate(1, NavigationBlockers.None, 15_100);
        check(policy.Evaluate(1, NavigationBlockers.Gps, 16_000).RecommendMap,
            "recovery: a brief good frame cannot erase a prolonged intermittent failure");
        policy.Evaluate(1, NavigationBlockers.None, 17_000);
        policy.Evaluate(1, NavigationBlockers.None, 19_001);
        check(!policy.Evaluate(1, NavigationBlockers.Gps, 20_000).RecommendMap &&
            !policy.Evaluate(2, NavigationBlockers.Heading, 50_000).RecommendMap,
            "recovery: sustained health and a new trip each reset the recovery timer");
        var points = new RoutePoint[] { new(C(0), 0), new(C(1), 1) };
        check(NavigationRecoveryPolicy.ProviderLabel(new(points, 1, "AStar", new("ASTAR"))) == "A-STAR" &&
            NavigationRecoveryPolicy.ProviderLabel(new(points, 1, "MLD (Railway OSRM)", new("MLD"))) == "MLD",
            "presentation: the actual route provider is reported independently of connectivity icons");
    }

    private static void PosePairing(Action<bool, string> check)
    {
        var previous = ARRenderGenerationBridge.Current;
        bool accepting = ARRenderGenerationBridge.IsAcceptingFrames;
        var generation = new ARRenderGenerationToken(6300, 6301, 6302);
        ARRenderGenerationBridge.Activate(generation);
        ARCameraPoseBridge.Clear(); ARTrackingStateBridge.Clear();
        long mono = Environment.TickCount64;
        long timestamp = 100;
        void Pose(long acquired, bool repeat = false)
        {
            if (!repeat) timestamp++;
            ARTrackingStateBridge.PublishObservation(generation.SessionGeneration, "TRACKING", "NONE", timestamp, false);
            var metadata = new ARFrameMetadata(generation, timestamp, new(generation.GeometryGeneration, 0, 1080, 1920))
                { AcquiredAtMonotonicMilliseconds = acquired };
            var ground = new ARGroundStateBridge.GroundStateSnapshot(timestamp, generation, 1,
                ARGroundLifecycleState.Verified, ARGroundTrust.Verified, mono, "fixture");
            ARCameraPoseBridge.PublishFrame(true, "NONE", 0, 1.35f, 0, 0, 0, 0, 1,
                [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, -1, 0, 0, -.2f, 0], .1f, 100, ground, 0, 0, 0, metadata);
        }
        try
        {
            Pose(mono - 1500); long observation = ARCameraPoseBridge.CurrentFrame.Version;
            Pose(mono);
            check(ARCameraPoseBridge.TryGetObservationFrame(mono - 1500, mono, out var paired, out long skew) &&
                paired.Version == observation && skew == 0,
                "pairing: delayed GPS delivery finds its acquisition pose rather than the newest camera pose");
            check(!ARCameraPoseBridge.TryGetObservationFrame(mono - 700, mono, out _, out _),
                "pairing: a gap outside the bounded pose tolerance cannot fabricate a synchronized observation");
            ARCameraPoseBridge.Clear();
            Pose(mono - 2900);
            long oldest = ARCameraPoseBridge.CurrentFrame.Version;
            for (int i = 1; i <= 300; i++) Pose(mono - 2900 + i * 2900 / 300);
            for (int i = 0; i < 700; i++) Pose(mono, repeat: true);
            check(ARCameraPoseBridge.TryGetObservationFrame(mono - 2900, mono, out var retained, out _) && retained.Version == oldest,
                "pairing: high-rate and repeated camera publications cannot evict a still-eligible GPS acquisition pose");
            ARTrackingStateBridge.PublishLifecycleTransition(generation.SessionGeneration, "Paused", "fixture", false);
            ARTrackingStateBridge.PublishLifecycleTransition(generation.SessionGeneration, "Running", "fixture", false);
            Pose(mono);
            check(!ARCameraPoseBridge.TryGetObservationFrame(mono - 1500, mono, out _, out _),
                "pairing: resuming AR cannot reuse a GPS acquisition from the previous tracking epoch");
        }
        finally
        {
            ARCameraPoseBridge.Clear(); ARTrackingStateBridge.Clear();
            if (accepting) ARRenderGenerationBridge.Activate(previous); else ARRenderGenerationBridge.Invalidate();
        }
    }

    private sealed class NativeSource(LocationReading reading) : ILocationService, INavigationLocationSource
    {
        public int NavigationCalls, DefaultCalls;
        public DateTimeOffset? NewerThan;
        public bool IsLocationServiceEnabled => true;
        public Task<bool> EnsurePermissionAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<LocationReading?> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
        { DefaultCalls++; return Task.FromResult<LocationReading?>(reading); }
        public Task<LocationReading?> GetLastKnownLocationAsync(CancellationToken cancellationToken = default) => Task.FromResult<LocationReading?>(null);
        public Task<LocationReading?> GetNavigationLocationAsync(DateTimeOffset? newerThan, CancellationToken cancellationToken = default)
        { NavigationCalls++; NewerThan = newerThan; return Task.FromResult<LocationReading?>(reading); }
    }
}
