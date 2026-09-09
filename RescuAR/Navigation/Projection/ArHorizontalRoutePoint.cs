namespace RescuAR.Navigation.Projection;

/// <summary>
/// Horizontal AR-space route point after map-to-AR yaw alignment.
///
/// Y is intentionally omitted here. The current AR ground anchor supplies the
/// route's vertical placement.
/// </summary>
public readonly record struct ArHorizontalRoutePoint(
    float X,
    float Z,
    double DistanceFromWindowStartMeters);
