using Android.Util;
using Google.AR.Core;
using RescuAR.AR;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// Enables costly ARCore features only while their outputs are required.
/// This method runs while the service's update gate is held, so session
/// reconfiguration cannot race camera controls or lifecycle transitions.
/// </summary>
public sealed partial class ArCoreService
{
    private const long WorkloadTransitionRetryIntervalMilliseconds =
        1_000;

    private bool planeFindingEnabled =
        true;

    private long lastWorkloadTransitionAttemptTimestamp =
        long.MinValue;

    private void UpdateDepthModeForCurrentDemand(
        Session currentSession)
    {
        ARFloodDepthBridge.FloodDepthSnapshot flood =
            ARFloodDepthBridge.Current;

        ARRouteBridge.RouteSnapshot route =
            ARRouteBridge.Current;

        bool routeOcclusionRequired =
            ARRouteRenderer.DepthOcclusionRequested &&
            powerThermalDecision.RouteDepthAllowed &&
            route.IsAvailable &&
            ARRouteVisualPolicy.HasNearbyRoute(
                route.Points);

        bool floodDepthRequired =
            flood.IsAvailable;

        bool desiredPlaneFinding =
            groundDepthRequested;

        bool desiredDepth =
            depthModeSupported &&
            !forceDepthDisabledForExperiment &&
            (groundDepthRequested ||
             floodDepthRequired ||
             routeOcclusionRequired);

        if (desiredPlaneFinding ==
                planeFindingEnabled &&
            desiredDepth ==
                depthModeEnabled)
        {
            return;
        }

        long now =
            System.Environment.TickCount64;

        if (lastWorkloadTransitionAttemptTimestamp !=
                long.MinValue &&
            now -
                lastWorkloadTransitionAttemptTimestamp <
                    WorkloadTransitionRetryIntervalMilliseconds)
        {
            return;
        }

        lastWorkloadTransitionAttemptTimestamp =
            now;

        try
        {
            using Google.AR.Core.Config config =
                currentSession.Config;

            config.SetPlaneFindingMode(
                desiredPlaneFinding
                    ? Google.AR.Core.Config
                        .PlaneFindingMode
                        .Horizontal
                    : Google.AR.Core.Config
                        .PlaneFindingMode
                        .Disabled);

            config.SetDepthMode(
                desiredDepth
                    ? Google.AR.Core.Config
                        .DepthMode
                        .Automatic!
                    : Google.AR.Core.Config
                        .DepthMode
                        .Disabled);

            currentSession.Configure(
                config);

            planeFindingEnabled =
                desiredPlaneFinding;

            depthModeEnabled =
                desiredDepth;

            if (!depthModeEnabled)
            {
                ARDepthOcclusionBridge.Clear();

                lastDepthOcclusionPublishTimestamp =
                    long.MinValue;
            }

            Log.Info(
                Tag,
                "AR WORKLOAD STATE: " +
                $"planeFinding={(planeFindingEnabled ? "ACTIVE" : "SUSPENDED")}, " +
                $"depth={(depthModeEnabled ? "ACTIVE" : "SUSPENDED")}, " +
                $"experimentMode={depthExperimentMode}, " +
                $"groundSearch={groundDepthRequested}, " +
                $"floodDepth={floodDepthRequired}, " +
                $"routeOcclusion={routeOcclusionRequired}.");
        }
        catch (Exception exception)
        {
            Log.Warn(
                Tag,
                "ARCore workload-state transition deferred: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }
}
