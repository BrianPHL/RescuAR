using System;

namespace RescuAR.Navigation.Projection;

/// <summary>
/// Shared limits for the moving local AR navigation frame.
///
/// The complete route remains in geographic space. Only a bounded section
/// around the pedestrian is projected into the current ARCore world frame.
/// Keeping these limits in one place prevents the renderer, route publisher,
/// and ground-anchor service from drifting into incompatible policies.
/// </summary>
public static class LocalArNavigationPolicy
{
    /// <summary>
    /// Forward route distance rendered during normal road-following guidance.
    /// </summary>
    public const double RoadFollowingWindowMeters =
        40.0;

    /// <summary>
    /// Absolute upper bound accepted by the AR publication layer. Callers may
    /// choose a shorter window, but cannot restore a long city-scale horizon.
    /// </summary>
    public const double MaximumVisibleWindowMeters =
        50.0;

    /// <summary>
    /// Short visual used before route-corridor confidence is established.
    /// </summary>
    public const double ApproachWindowMeters =
        7.5;

    /// <summary>
    /// Maximum horizontal travel from the current ground anchor before that
    /// anchor is retired and a nearby floor anchor is acquired.
    /// </summary>
    public const float GroundAnchorRetirementDistanceMeters =
        15.0f;

    /// <summary>
    /// The camera may be up to 20 m from its retiring anchor and the mapped
    /// path may independently be up to 20 m from GPS. Their vector sum is a
    /// placement offset, not a measure of anchor drift.
    /// </summary>
    public const float MaximumRouteOriginOffsetMeters =
        40.0f;

    public const float MaximumCameraToAnchorOffsetMeters = 20.0f;

    /// <summary>
    /// Lowest plausible upright-device camera height above a navigable floor.
    /// Hits closer than this are commonly tables, benches, or other raised
    /// horizontal surfaces rather than the pedestrian's floor.
    /// </summary>
    public const float MinimumPlausibleCameraHeightAboveGroundMeters =
        0.65f;

    /// <summary>
    /// Highest plausible upright-device camera height above a navigable floor.
    /// A larger separation usually indicates a stale or incorrectly resolved
    /// ARCore ground reference.
    /// </summary>
    public const float MaximumPlausibleCameraHeightAboveGroundMeters =
        2.40f;

    /// <summary>
    /// Stricter acquisition range used when deciding whether a newly observed
    /// horizontal surface is likely to be the pedestrian's floor. The wider
    /// plausible range above remains the renderer's continuity safety limit.
    /// </summary>
    public const float MinimumPreferredGroundCandidateCameraHeightMeters =
        0.85f;

    public const float MaximumPreferredGroundCandidateCameraHeightMeters =
        2.20f;

    public static float GetHorizontalDistanceMeters(
        float deltaX,
        float deltaZ)
    {
        if (!float.IsFinite(deltaX) ||
            !float.IsFinite(deltaZ))
        {
            return float.PositiveInfinity;
        }

        return MathF.Sqrt(
            deltaX * deltaX +
            deltaZ * deltaZ);
    }

    public static bool IsRouteOriginOffsetAcceptable(
        float offsetX,
        float offsetZ,
        out float horizontalDistanceMeters)
    {
        horizontalDistanceMeters =
            GetHorizontalDistanceMeters(
                offsetX,
                offsetZ);

        return float.IsFinite(
                   horizontalDistanceMeters) &&
            horizontalDistanceMeters <=
                MaximumRouteOriginOffsetMeters;
    }

    public static bool IsCameraHeightAboveGroundPlausible(
        float cameraWorldY,
        float groundWorldY,
        out float cameraHeightAboveGroundMeters)
    {
        cameraHeightAboveGroundMeters =
            cameraWorldY -
            groundWorldY;

        return float.IsFinite(
                   cameraHeightAboveGroundMeters) &&
            cameraHeightAboveGroundMeters >=
                MinimumPlausibleCameraHeightAboveGroundMeters &&
            cameraHeightAboveGroundMeters <=
                MaximumPlausibleCameraHeightAboveGroundMeters;
    }

    public static bool IsPreferredGroundCandidateHeight(
        float cameraWorldY,
        float candidateGroundWorldY,
        out float cameraHeightAboveCandidateMeters)
    {
        cameraHeightAboveCandidateMeters =
            cameraWorldY -
            candidateGroundWorldY;

        return float.IsFinite(
                   cameraHeightAboveCandidateMeters) &&
            cameraHeightAboveCandidateMeters >=
                MinimumPreferredGroundCandidateCameraHeightMeters &&
            cameraHeightAboveCandidateMeters <=
                MaximumPreferredGroundCandidateCameraHeightMeters;
    }
}
