namespace RescuAR.MAUI.Services.Navigation;

/// <summary>Geographic acquisition retries do not depend on the AR camera.</summary>
public static class RouteStartupRetryPolicy
{
    public static bool CanRetryInView(bool waitingForGps, bool mapView, bool arView, bool arReady) =>
        (mapView || arView) && (waitingForGps || (arView && arReady));
}
