using RescuAR.Navigation.Progress;

namespace RescuAR.AR;

/// <summary>
/// Combines the critical inputs used by AR navigation into one route-visibility
/// decision. Camera passthrough remains available even when cyan geometry is
/// withheld, allowing text guidance and recovery instructions to continue.
/// </summary>
public static class ARGuidanceConfidencePolicy
{
    public static GuidanceConfidenceSnapshot Evaluate(
        bool destinationAvailable,
        bool routeAvailable,
        RouteVisualKind routeVisualKind,
        bool progressAvailable,
        bool gpsFresh,
        GpsPdrFusionPolicy.GpsConfidence gpsConfidence,
        RouteMatchConfidence routeMatchConfidence,
        bool routeIdentitySuspended,
        bool headingTrusted,
        ARCameraSpatialController.SpatialContinuitySnapshot spatial,
        bool rerouteInProgress,
        bool recoveryConnectorVerified)
    {
        int score =
            GetSpatialScore(
                spatial) +
            (headingTrusted ? 20 : 0) +
            GetGpsScore(
                gpsFresh,
                gpsConfidence) +
            GetRouteMatchScore(
                gpsFresh,
                routeMatchConfidence) +
            (routeAvailable ? 10 : 0) +
            (progressAvailable ? 10 : 0);

        if (!destinationAvailable)
        {
            return Hidden(
                score,
                "No active evacuation route.");
        }

        if (!routeAvailable)
        {
            return new GuidanceConfidenceSnapshot(
                GuidanceConfidenceState.Recovery,
                score,
                false,
                "Preparing AR route…");
        }

        if (spatial.State ==
                ARCameraSpatialController.SpatialContinuityState.LongLoss ||
            spatial.State ==
                ARCameraSpatialController.SpatialContinuityState.Untrusted ||
            spatial.State ==
                ARCameraSpatialController.SpatialContinuityState.Degraded)
        {
            return Hidden(
                score,
                GetSpatialMessage(
                    spatial.State));
        }

        if (!headingTrusted)
        {
            return Hidden(
                score,
                "Calibrating direction — AR route hidden");
        }

        if (spatial.State ==
            ARCameraSpatialController.SpatialContinuityState.ShortHold)
        {
            return new GuidanceConfidenceSnapshot(
                GuidanceConfidenceState.Degraded,
                score,
                true,
                "Tracking interrupted — hold the phone steady");
        }

        if (rerouteInProgress)
        {
            return new GuidanceConfidenceSnapshot(
                GuidanceConfidenceState.Recovery,
                score,
                false,
                "Updating your route — follow text guidance");
        }

        if (!gpsFresh ||
            gpsConfidence ==
                GpsPdrFusionPolicy.GpsConfidence.Unavailable ||
            routeMatchConfidence ==
                RouteMatchConfidence.Unavailable)
        {
            return new GuidanceConfidenceSnapshot(
                GuidanceConfidenceState.Recovery,
                score,
                false,
                "Locating you on the route — AR route hidden");
        }

        if (routeIdentitySuspended)
        {
            return new GuidanceConfidenceSnapshot(
                GuidanceConfidenceState.Recovery,
                score,
                false,
                "Confirming your route position — AR route hidden");
        }

        bool verifiedRecovery =
            routeVisualKind ==
                RouteVisualKind.ApproachConnector &&
            recoveryConnectorVerified &&
            gpsConfidence >=
                GpsPdrFusionPolicy.GpsConfidence.Medium &&
            routeMatchConfidence >=
                RouteMatchConfidence.Medium;

        if (verifiedRecovery)
        {
            return new GuidanceConfidenceSnapshot(
                GuidanceConfidenceState.Recovery,
                score,
                true,
                "Returning to the route — follow the short cyan arrow");
        }

        bool fullGuidance =
            routeVisualKind ==
                RouteVisualKind.RouteWindow &&
            progressAvailable &&
            gpsConfidence >=
                GpsPdrFusionPolicy.GpsConfidence.Medium &&
            routeMatchConfidence >=
                RouteMatchConfidence.Medium;

        if (fullGuidance)
        {
            return new GuidanceConfidenceSnapshot(
                GuidanceConfidenceState.Full,
                score,
                true,
                "AR guidance readiness: Full");
        }

        bool degradedGuidance =
            routeVisualKind ==
                RouteVisualKind.RouteWindow &&
            progressAvailable &&
            gpsConfidence !=
                GpsPdrFusionPolicy.GpsConfidence.Unavailable &&
            routeMatchConfidence !=
                RouteMatchConfidence.Unavailable;

        if (degradedGuidance)
        {
            return new GuidanceConfidenceSnapshot(
                GuidanceConfidenceState.Degraded,
                score,
                true,
                "AR accuracy reduced — confirm with text guidance");
        }

        return new GuidanceConfidenceSnapshot(
            GuidanceConfidenceState.Recovery,
            score,
            false,
            "Preparing trustworthy AR guidance…");
    }

    private static GuidanceConfidenceSnapshot Hidden(
        int score,
        string message) =>
        new(
            GuidanceConfidenceState.Hidden,
            score,
            false,
            message);

    private static int GetSpatialScore(
        ARCameraSpatialController.SpatialContinuitySnapshot spatial) =>
        spatial.State switch
        {
            ARCameraSpatialController.SpatialContinuityState.Live =>
                30,

            ARCameraSpatialController.SpatialContinuityState.ShortHold =>
                20,

            ARCameraSpatialController.SpatialContinuityState.Degraded =>
                10,

            _ =>
                0
        };

    private static int GetGpsScore(
        bool gpsFresh,
        GpsPdrFusionPolicy.GpsConfidence confidence)
    {
        if (!gpsFresh)
        {
            return 0;
        }

        return confidence switch
        {
            GpsPdrFusionPolicy.GpsConfidence.High =>
                15,

            GpsPdrFusionPolicy.GpsConfidence.Medium =>
                12,

            GpsPdrFusionPolicy.GpsConfidence.Low =>
                5,

            _ =>
                0
        };
    }

    private static int GetRouteMatchScore(
        bool gpsFresh,
        RouteMatchConfidence confidence)
    {
        if (!gpsFresh)
        {
            return 0;
        }

        return confidence switch
        {
            RouteMatchConfidence.High =>
                15,

            RouteMatchConfidence.Medium =>
                12,

            RouteMatchConfidence.Low =>
                5,

            _ =>
                0
        };
    }

    private static string GetSpatialMessage(
        ARCameraSpatialController.SpatialContinuityState state) =>
        state switch
        {
            ARCameraSpatialController.SpatialContinuityState.Degraded =>
                "AR tracking weak — follow text guidance",

            ARCameraSpatialController.SpatialContinuityState.LongLoss =>
                "AR unavailable — move slowly to a well-lit area",

            _ =>
                "AR placement recovering — follow text guidance"
        };

    public enum GuidanceConfidenceState
    {
        Hidden,
        Recovery,
        Degraded,
        Full
    }

    public readonly record struct GuidanceConfidenceSnapshot(
        GuidanceConfidenceState State,
        int Score,
        bool AllowsRouteGeometry,
        string DisplayMessage)
    {
        public static GuidanceConfidenceSnapshot NotReady =>
            new(
                GuidanceConfidenceState.Hidden,
                0,
                false,
                "AR guidance not ready");
    }
}
