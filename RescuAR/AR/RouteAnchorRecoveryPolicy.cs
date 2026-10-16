namespace RescuAR.AR;

public enum RouteAnchorRecoveryAction { WaitForFloor, DiscardPreview, RebaseRoute }

/// <summary>An unpublished approach route must not block a tracked anchor handoff.</summary>
public static class RouteAnchorRecoveryPolicy
{
    public static RouteAnchorRecoveryAction Evaluate(bool roadFollowingConfirmed,
        bool freshTracking, bool coherentGround) => !freshTracking || !coherentGround
            ? RouteAnchorRecoveryAction.WaitForFloor
            : roadFollowingConfirmed ? RouteAnchorRecoveryAction.RebaseRoute
            : RouteAnchorRecoveryAction.DiscardPreview;
}
