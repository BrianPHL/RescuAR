using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;

internal static class ArrivalGuidanceChecks
{
    private static GeoCoordinate C(double east) => new(0, east / 6371008.8 * 180 / Math.PI);

    public static void Run(Action<bool, string> check)
    {
        var at = DateTimeOffset.Parse("2026-11-05T09:30:00Z");
        var vicinity = new SafeZoneConfirmationService();
        var policy = new ArrivalGuidancePolicy();
        SafeZoneConfirmationService.SafeZoneDecision Observe(double metres, int seconds,
            double accuracy = 7.5, string provider = "gps", bool held = false,
            double remaining = double.PositiveInfinity, long trip = 1, bool barrier = false) =>
            policy.Evaluate(vicinity, new(C(metres), accuracy, provider, at.AddSeconds(seconds)),
                C(0), 30, remaining, at.AddSeconds(seconds), trip, barrier, held);

        var first = Observe(23.4, 0);
        var nearby = Observe(24.3, 4, 8.5);
        check(first.IsCandidate && nearby.IsConfirmed && double.IsPositiveInfinity(nearby.RemainingRouteMeters) &&
            !ArrivalGuidancePolicy.ShouldComplete(nearby, false, false),
            "nearby startup: repeated vicinity observations without a route never complete or suppress geographic guidance");
        for (int seconds = 8; seconds <= 24; seconds += 4)
            nearby = Observe(24, seconds);
        check(nearby.IsConfirmed && !ArrivalGuidancePolicy.ShouldComplete(nearby, false, true),
            "vicinity: repeated raw GPS votes remain informational even when the explicit action is eligible");

        policy.Reset();
        vicinity.Reset();
        check(!Observe(10, 0, 21.9, "network").IsCandidate && vicinity.Current.ConfirmationCount == 0,
            "arrival evidence: the field's 21.9m network observation cannot vote despite the legacy 25m vicinity cap");
        check(Observe(10, 1).ConfirmationCount == 1 &&
            !Observe(10, 5, held: true).IsConfirmed && vicinity.Current.ConfirmationCount == 1,
            "arrival evidence: a geographically held navigation sample cannot add a vicinity vote");
        nearby = Observe(10, 9);
        check(nearby.IsConfirmed && !ArrivalGuidancePolicy.ShouldComplete(nearby, false, true),
            "arrival evidence: fresh recovery can resume proximity assessment without AR tracking or an automatic trip stop");

        bool eligible = UserArrivalConfirmationPolicy.CanOffer(C(10), C(0), 7.5,
            at.AddSeconds(9), at.AddSeconds(9));
        var explicitArrival = nearby with { IsCandidate = false, IsConfirmed = true };
        check(eligible && ArrivalGuidancePolicy.ShouldComplete(explicitArrival, true, eligible) &&
            !ArrivalGuidancePolicy.ShouldComplete(explicitArrival, false, eligible) &&
            !ArrivalGuidancePolicy.ShouldComplete(explicitArrival, true, false) &&
            !ArrivalGuidancePolicy.ShouldComplete(explicitArrival with { IsObservationHeld = true }, true, true),
            "explicit arrival: only an eligible confirmed user action completes guidance; proximity and held evidence cannot do so");
        check(!UserArrivalConfirmationPolicy.CanOffer(C(10), C(0), 7.5, at, at.AddSeconds(6)) &&
            !UserArrivalConfirmationPolicy.CanOffer(C(10), C(0), 7.5, at, at, majorRoadBarrier: true),
            "explicit arrival: freshness and mapped crossing checks still apply independently of AR availability");

        policy.Reset();
        vicinity.Reset();
        Observe(40, 0);
        var disputed = Observe(12, 1, 15, "network");
        check(!disputed.IsCandidate && disputed.IsObservationHeld && !policy.LastEvidence.IsAccepted &&
            vicinity.Current.ConfirmationCount == 0,
            "provider replay: a coarser 28m network jump toward the destination cannot vote against a recent precise GPS reference");
        Observe(39, 4);
        disputed = Observe(12, 4, 15, "network");
        check(disputed.IsObservationHeld && vicinity.Current.ConfirmationCount == 0,
            "provider replay: intermittent GPS/network jumps cannot manufacture repeated arrival evidence without AR input");
        check(Observe(12, 20, 15, "network").ConfirmationCount == 1 &&
            Observe(12, 24, 15, "network").IsConfirmed,
            "provider recovery: the bounded old-provider reference expires and fresh consistent observations can qualify again");

        var walkingPolicy = new ArrivalGuidancePolicy();
        var walkingVicinity = new SafeZoneConfirmationService();
        walkingPolicy.Evaluate(walkingVicinity, new(C(40), 7.5, "gps", at), C(0), 30,
            40, at, 1);
        var walking = walkingPolicy.Evaluate(walkingVicinity,
            new(C(15), 15, "network", at.AddSeconds(10)), C(0), 30, 15,
            at.AddSeconds(10), 1);
        check(walkingPolicy.LastEvidence.IsAccepted && walking.IsCandidate && !walking.IsObservationHeld,
            "provider walking: a coarser fix after a plausible 25m walk in ten seconds is not rejected against an old GPS position");

        var departed = Observe(60, 40, 7.5);
        check(!departed.IsConfirmed && departed.ConfirmationCount == 0,
            "vicinity: a qualified geographic departure clears the informational proximity state");
        var newTrip = Observe(10, 44, 30, "network", trip: 2);
        check(!newTrip.IsConfirmed && newTrip.ConfirmationCount == 0 && newTrip.DestinationVersion == 2 &&
            !Observe(10, 45, trip: 1).IsAvailable,
            "arrival ownership: an unqualified first fix on a new trip cannot inherit old votes and stale trip callbacks remain rejected");

        policy.Reset();
        vicinity.Reset();
        Observe(40, 0, 4);
        check(Observe(10, 1, 4).IsObservationHeld && vicinity.Current.ConfirmationCount == 0,
            "geographic replay: a same-provider jump beyond pedestrian motion and uncertainty cannot supply arrival evidence");
        check(!Observe(10, 20, barrier: true).IsConfirmed,
            "vicinity: a loaded mapped barrier still prevents confirmation");
    }
}
