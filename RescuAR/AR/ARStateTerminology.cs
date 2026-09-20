namespace RescuAR.AR;

/// <summary>
/// Canonical AR operational vocabulary used by code, UI, logs, and field
/// validation. These states are related but never interchangeable:
///
/// Lifecycle: whether the AR session is created, running, suspended, failed,
/// or disposed.
/// Tracking: ARCore's current ability to track the device, including its
/// failure reason and transition duration.
/// Pose: a fresh, current-generation camera transform from one ARCore frame.
/// Ground trust: None, Provisional, or Verified confidence in the local floor
/// reference; it is not the same as tracking.
/// Route visibility: whether cyan geometry is permitted to render after all
/// spatial and navigation gates.
/// Guidance readiness: Hidden, Recovery, Degraded, or Full combined guidance
/// confidence; camera visibility alone does not imply readiness.
/// Flood state: unavailable, estimated, or verified ground-relative depth;
/// advisory gauge level is not local flood depth.
/// </summary>
public static class ARStateTerminology
{
    public const string Version =
        "AR_STATE_GLOSSARY_V1";

    public const string Lifecycle =
        "lifecycle";

    public const string Tracking =
        "tracking";

    public const string Pose =
        "pose";

    public const string GroundTrust =
        "groundTrust";

    public const string RouteVisibility =
        "routeVisibility";

    public const string GuidanceReadiness =
        "guidanceReadiness";

    public const string FloodState =
        "floodState";
}
