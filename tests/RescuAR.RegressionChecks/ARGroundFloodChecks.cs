using System.Numerics;
using RescuAR.AR;

internal static class ARGroundFloodChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var severity in new[]
        {
            ARPowerThermalPolicy.AndroidThermalSeverity.None,
            ARPowerThermalPolicy.AndroidThermalSeverity.Light,
            ARPowerThermalPolicy.AndroidThermalSeverity.Moderate,
            ARPowerThermalPolicy.AndroidThermalSeverity.Severe,
            ARPowerThermalPolicy.AndroidThermalSeverity.Critical
        })
        {
            var workload = ARPowerThermalPolicy.Evaluate(float.NaN, false, severity,
                ARPowerThermalPolicy.WorkloadMode.Normal);
            var full = ARGroundProbePlan.Create(workload.MaximumGroundProbesPerSweep, false);
            var reduced = ARGroundProbePlan.Create(workload.MaximumGroundProbesPerSweep, true);
            check(full.ScreenProbes >= 2 && full.ScreenProbes <= 4 &&
                full.WorldDownProbes <= 3 &&
                full.ScreenProbes + full.WorldDownProbes <= workload.MaximumGroundProbesPerSweep,
                $"{workload.Mode} floor search reserves same-frame screen support within its budget");
            check(reduced.ScreenProbes >= 2 && reduced.WorldDownProbes == 0 &&
                reduced.ScreenProbes <= workload.MaximumGroundProbesPerSweep,
                $"{workload.Mode} reduced floor search can still verify Plane or Depth evidence");
        }

        var generation = new ARRenderGenerationToken(950, 951, 952);
        ARRenderGenerationBridge.Activate(generation);
        ARGroundStateBridge.BeginSearch(generation, false, "floor fixture");
        var initial = ARGroundStateBridge.PublishProvisional(generation,
            "camera-relative provisional ground");
        ARGroundStateBridge.Suspend(generation, "temporary tracking pause");
        check(!ARGroundStateBridge.Current.HasGroundReference,
            "a tracking pause withholds provisional floor placement");
        ARGroundStateBridge.BeginSearch(generation, false, "tracked camera resumed");
        var restored = ARGroundStateBridge.PublishProvisional(generation,
            "camera-relative provisional ground");
        check(restored.HasGroundReference && restored.Trust == ARGroundTrust.Provisional &&
            restored.ReferenceGeneration > initial.ReferenceGeneration,
            "resuming a retained floor estimate restores provisional trust with a new reference generation");
        check(ARGroundStateBridge.PublishProvisional(generation,
            "camera-relative provisional ground") == restored,
            "successive tracked provisional frames do not churn floor state or reference identity");
        ARGroundStateBridge.PublishProvisional(generation with { SessionGeneration = 949 },
            "old camera callback");
        check(ARGroundStateBridge.Current == restored,
            "an old session cannot restore provisional floor trust");
        var verified = ARGroundStateBridge.PublishVerified(generation, "real floor acquired");
        check(verified.Trust == ARGroundTrust.Verified &&
            verified.ReferenceGeneration > restored.ReferenceGeneration,
            "a real floor anchor still replaces the restored estimate");
        ARGroundStateBridge.Clear("floor fixture complete");
        ARRenderGenerationBridge.Activate(ARRenderGenerationToken.Invalid);

        var projection = new ARCameraPoseBridge.ProjectionSnapshot(true,
            1,0,0,0, 0,1,0,0, 0,0,-1.002002f,-0.2002002f, 0,0,-1,0, 0.1f,100);
        var camera = new Vector3(0, 1.35f, 0);
        var start = new Vector3(-1, 0.6f, -2);
        var end = new Vector3(1, 0.6f, -2);
        bool Project(Vector3 observer, out Vector2 a, out Vector2 b) =>
            FloodSimulationProjection.TryProjectSegment(start, end, observer,
                Quaternion.Identity, projection, out a, out b);
        check(Project(camera, out var a, out var b) &&
            Math.Abs(a.X - 0.25f) < 0.001f && Math.Abs(b.X - 0.75f) < 0.001f &&
            a.Y > 0.5f && a.Y < 1,
            "a 0.6 m water outline projects below a normally held camera");
        check(Project(camera + Vector3.UnitX, out var moved, out _) && moved.X < a.X,
            "a fixed floor outline shifts in view as the observer moves");
        check(!FloodSimulationProjection.TryProjectSegment(new(-1, 0.6f, 2),
            new(1, 0.6f, 2), camera, Quaternion.Identity, projection, out _, out _),
            "water outlines behind the observer remain clipped");
    }
}
