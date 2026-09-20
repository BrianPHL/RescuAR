using RescuAR.AR;
using RescuAR.Navigation.Progress;

namespace RescuAR.Tests;

public sealed class GuidanceAndWorkloadPolicyTests
{
    [Fact]
    public void TrustedInputsAllowRoute()
    {
        var result = Evaluate(ARCameraSpatialController.SpatialContinuityState.Live, true, true);
        Assert.Equal(ARGuidanceConfidencePolicy.GuidanceConfidenceState.Full, result.State);
        Assert.True(result.AllowsRouteGeometry);
    }

    [Theory]
    [InlineData(ARCameraSpatialController.SpatialContinuityState.Untrusted, true, true)]
    [InlineData(ARCameraSpatialController.SpatialContinuityState.Live, false, true)]
    [InlineData(ARCameraSpatialController.SpatialContinuityState.Live, true, false)]
    public void UnsafeInputsHideRoute(ARCameraSpatialController.SpatialContinuityState state, bool heading, bool gps) =>
        Assert.False(Evaluate(state, heading, gps).AllowsRouteGeometry);

    [Fact]
    public void CriticalTierShedsWork()
    {
        var normal = ARPowerThermalPolicy.Evaluate(35, false, ARPowerThermalPolicy.AndroidThermalSeverity.None, ARPowerThermalPolicy.WorkloadMode.Normal);
        var critical = ARPowerThermalPolicy.Evaluate(35, false, ARPowerThermalPolicy.AndroidThermalSeverity.Critical, normal.Mode);
        Assert.False(critical.RouteDepthAllowed);
        Assert.True(critical.MinimumFrameIntervalMilliseconds > normal.MinimumFrameIntervalMilliseconds);
        Assert.True(critical.MaximumGroundProbesPerSweep < normal.MaximumGroundProbesPerSweep);
    }

    [Fact]
    public void HysteresisPreventsFlapping()
    {
        Assert.Equal(ARPowerThermalPolicy.WorkloadMode.Warm,
            ARPowerThermalPolicy.Evaluate(38, false, ARPowerThermalPolicy.AndroidThermalSeverity.None, ARPowerThermalPolicy.WorkloadMode.Warm).Mode);
        Assert.Equal(ARPowerThermalPolicy.WorkloadMode.Normal,
            ARPowerThermalPolicy.Evaluate(37, false, ARPowerThermalPolicy.AndroidThermalSeverity.None, ARPowerThermalPolicy.WorkloadMode.Warm).Mode);
    }

    private static ARGuidanceConfidencePolicy.GuidanceConfidenceSnapshot Evaluate(ARCameraSpatialController.SpatialContinuityState state, bool heading, bool gps) =>
        ARGuidanceConfidencePolicy.Evaluate(true, true, RouteVisualKind.RouteWindow, true, gps,
            GpsPdrFusionPolicy.GpsConfidence.High, RouteMatchConfidence.High, false, heading,
            new(state, 0, 100, "unit test"), false, false);
}
