using Android.Util;
using RescuAR.AR;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// Keeps plane/depth configuration fixed for the retained session. Consumers
/// still acquire ground/depth outputs on demand, but UI demand changes must
/// not repeatedly reconfigure ARCore's native depth worker.
/// </summary>
public sealed partial class ArCoreService
{
    private bool planeFindingEnabled = true;
    private string? lastWorkloadStateLog;

    private void LogCurrentWorkloadDemand()
    {
        var flood = ARFloodDepthBridge.Current;
        var route = ARRouteBridge.Current;
        bool routeOcclusionRequired = ARRouteRenderer.DepthOcclusionRequested &&
            powerThermalDecision.RouteDepthAllowed && route.IsAvailable &&
            ARRouteVisualPolicy.HasNearbyRoute(route.Points);
        string state =
            $"planeFinding={(planeFindingEnabled ? "ACTIVE" : "SUSPENDED")}, " +
            $"depth={(depthModeEnabled ? "ACTIVE" : "SUSPENDED")}, " +
            $"experimentMode={depthExperimentMode}, " +
            $"groundSearch={groundDepthRequested}, floodDepth={flood.IsAvailable}, " +
            $"routeOcclusion={routeOcclusionRequired}, configuration=SESSION_STABLE";
        if (state == lastWorkloadStateLog) return;
        lastWorkloadStateLog = state;
        if (!depthModeEnabled) ARDepthOcclusionBridge.Clear();
        Log.Info(Tag, "AR WORKLOAD STATE: " + state);
    }
}
