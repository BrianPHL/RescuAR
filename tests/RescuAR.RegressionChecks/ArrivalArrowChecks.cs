using RescuAR.AR;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Projection;

internal static class ArrivalArrowChecks
{
    private static GeoCoordinate C(double metres) => new(0, metres / 6371008.8 * 180 / Math.PI);
    private static ArHorizontalRoutePoint P(float x, float z, double d) => new(x, z, d);

    public static void Run(Action<bool, string> check)
    {
        foreach (string provider in new[] { "AStar", "MLD (Railway OSRM)" })
        {
            foreach (float length in new[] { 7.5f, 40f })
            {
                var route = new[] { P(3, -2, 0), P(3, -2 - length, length) };
                var original = route.ToArray();
                check(ARRouteVisualPolicy.ShouldRenderForwardArrow(RouteVisualKind.RouteWindow) &&
                    ARRouteVisualPolicy.TryGetForwardArrowLeg(route, RouteVisualKind.RouteWindow, out var start, out var tip) &&
                    start == route[0] && Math.Abs(tip.X - 3) < .001 && Math.Abs(tip.Z + 8) < .001 &&
                    tip.DistanceFromWindowStartMeters == 6 && route.SequenceEqual(original),
                    $"{provider}: a {length}m qualified ribbon has a nearby arrow along its retained mapped geometry");
            }
            var corner = new[] { P(0, 0, 0), P(0, -4, 4), P(5, -4, 9) };
            check(ARRouteVisualPolicy.TryGetForwardArrowLeg(corner, RouteVisualKind.RouteWindow, out var leg, out var point) &&
                leg == corner[1] && point == P(2, -4, 6),
                $"{provider}: the arrow follows the outgoing mapped leg instead of cutting across the corner");
            var closeCorner = new[] { P(0, 0, 0), P(0, -5.9f, 5.9), P(4, -5.9f, 9.9) };
            check(ARRouteVisualPolicy.TryGetForwardArrowLeg(closeCorner, RouteVisualKind.RouteWindow, out leg, out point) &&
                leg == closeCorner[0] && point == closeCorner[1],
                $"{provider}: an arrow immediately after a corner retains the incoming leg without a diagonal or tiny flicker");
            check(ARRouteVisualPolicy.TryGetForwardArrowLeg(new[] { P(0, 0, 0), P(0, -2, 2) },
                    RouteVisualKind.RouteWindow, out _, out point) && point == P(0, -2, 2),
                $"{provider}: the arrow never extends beyond a short route or hazard-safe prefix");
        }
        check(!ARRouteVisualPolicy.TryGetForwardArrowLeg(new[] { P(0, 0, 0), P(0, -40, 40) },
                RouteVisualKind.Unavailable, out _, out _) &&
            !ARRouteVisualPolicy.TryGetForwardArrowLeg(new[] { P(0, 0, 0), P(float.NaN, -2, 2) },
                RouteVisualKind.RouteWindow, out _, out _) &&
            !ARRouteVisualPolicy.TryGetForwardArrowLeg(new[] { P(0, 0, 0), P(0, -8, 8), P(0, -3, 3) },
                RouteVisualKind.RouteWindow, out _, out _),
            "arrow selection rejects unavailable, invalid and reversed windows");
        check(ARRouteVisualPolicy.TryGetForwardArrowLeg(new[] { P(0, 0, 0), P(0, -12, 12) },
                RouteVisualKind.ApproachConnector, out _, out var approachTip) && approachTip == P(0, -12, 12),
            "an independently qualified access connector retains its endpoint arrow behavior");

        var at = DateTimeOffset.Parse("2026-10-31T08:00:00Z");
        foreach (double distance in new[] { 75, 61, 34, 15.1 })
            check(!UserArrivalConfirmationPolicy.CanOffer(C(distance), C(0), 4, at, at),
                $"manual arrival: a {distance}m position cannot expose the button or replace turn guidance");
        check(UserArrivalConfirmationPolicy.CanOffer(C(10), C(0), 4, at, at.AddSeconds(4)) &&
            UserArrivalConfirmationPolicy.CanShowNotice(C(10), C(0), 4, at, at.AddSeconds(4)),
            "manual arrival: a fresh precise near-destination position enables the explicit user action and matching notice");
        foreach (double? accuracy in new double?[] { null, 16, 50, double.NaN })
            check(!UserArrivalConfirmationPolicy.CanOffer(C(10), C(0), accuracy, at, at),
                "manual arrival: uncertain accuracy cannot expose the action");
        check(!UserArrivalConfirmationPolicy.CanOffer(C(10), C(0), 4, at, at.AddSeconds(6)) &&
            !UserArrivalConfirmationPolicy.CanShowNotice(C(10), C(0), 4, at, at.AddSeconds(6)) &&
            !UserArrivalConfirmationPolicy.CanOffer(C(10), C(0), 4, at.AddSeconds(3), at) &&
            !UserArrivalConfirmationPolicy.CanOffer(C(10), C(0), 4, at, at, majorRoadBarrier: true) &&
            !UserArrivalConfirmationPolicy.CanOffer(C(10), C(0), 4, at, at, observationHeld: true),
            "manual arrival: stale/future, held and separated-by-major-road fixes remain ineligible");

        var arrival = new SafeZoneConfirmationService();
        SafeZoneConfirmationService.SafeZoneDecision Observe(double distance, double seconds,
            double accuracy = 4, bool barrier = false, long trip = 1) => arrival.Evaluate(C(distance), C(0),
                30, accuracy, 0, at.AddSeconds(seconds), barrier, at.AddSeconds(seconds), trip);
        check(!Observe(61, 0).IsCandidate && !Observe(34, 3).IsConfirmed,
            "automatic arrival: route-end progress alone cannot confirm a distant destination");
        check(!Observe(10, 6).IsConfirmed && !Observe(10, 6).IsConfirmed && !Observe(10, 8).IsConfirmed &&
            Observe(10, 9).IsConfirmed,
            "automatic arrival: distinct accurate fixes and the existing three-second duration still confirm valid vicinity");
        check(!Observe(10, 10, trip: 2).IsConfirmed && !Observe(10, 13, barrier: true, trip: 2).IsConfirmed,
            "automatic arrival: a new destination trip and a mapped crossing cannot inherit a previous confirmation");
    }
}
