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
    private WorkloadDemandSnapshot? lastWorkloadDemand;

    private void LogCurrentWorkloadDemand()
    {
        var flood = ARFloodDepthBridge.Current;
        var route = ARRouteBridge.Current;
        bool routeOcclusionRequired = ARRouteRenderer.DepthOcclusionRequested &&
            powerThermalDecision.RouteDepthAllowed && route.IsAvailable &&
            ARRouteVisualPolicy.HasNearbyRoute(route.Points);
        var demand = new WorkloadDemandSnapshot(planeFindingEnabled,
            depthModeEnabled, depthExperimentMode, groundDepthRequested,
            flood.IsAvailable, routeOcclusionRequired);
        if (lastWorkloadDemand == demand) return;
        lastWorkloadDemand = demand;
        string state =
            $"planeFinding={(planeFindingEnabled ? "ACTIVE" : "SUSPENDED")}, " +
            $"depth={(depthModeEnabled ? "ACTIVE" : "SUSPENDED")}, " +
            $"experimentMode={depthExperimentMode}, " +
            $"groundSearch={groundDepthRequested}, floodDepth={flood.IsAvailable}, " +
            $"routeOcclusion={routeOcclusionRequired}, configuration=SESSION_STABLE";
        if (!depthModeEnabled) ARDepthOcclusionBridge.Clear();
        Log.Info(Tag, "AR WORKLOAD STATE: " + state);
    }

    private readonly record struct WorkloadDemandSnapshot(bool PlaneFinding,
        bool Depth, string ExperimentMode, bool GroundSearch, bool FloodDepth,
        bool RouteOcclusion);
}
