using RescuAR.AR;

namespace RescuAR.Tests;

public sealed class BridgeSafetyTests : IDisposable
{
    public BridgeSafetyTests()
    {
        ARRenderGenerationBridge.Invalidate();
        ARGroundStateBridge.Clear("test setup");
        ARCameraPoseBridge.Clear();
        ARTrackingStateBridge.Clear();
        ARFloodDepthBridge.Clear("test setup");
    }

    public void Dispose()
    {
        ARFloodDepthBridge.Clear("test cleanup");
        ARGroundStateBridge.Clear("test cleanup");
        ARCameraPoseBridge.Clear();
        ARTrackingStateBridge.Clear();
        ARRenderGenerationBridge.Invalidate();
    }

    [Fact]
    public void StaleGenerationIsRejected()
    {
        ARRenderGenerationToken active = new(2, 3, 4);
        ARRenderGenerationBridge.Activate(active);
        Assert.True(ARRenderGenerationBridge.TryAcceptCallback(active, "test"));
        Assert.False(ARRenderGenerationBridge.TryAcceptCallback(new(1, 3, 4), "test"));
        ARRenderGenerationBridge.RegisterGraphicsContext(5);
        Assert.False(ARRenderGenerationBridge.IsAcceptingFrames);
    }

    [Fact]
    public void PoseFreshnessExpiresWithoutDelay()
    {
        Assert.True(CreateSpatial(Environment.TickCount64).IsFresh);
        Assert.False(CreateSpatial(Environment.TickCount64 - ARCameraPoseBridge.MaximumPoseAgeMilliseconds - 1).IsFresh);
    }

    [Fact]
    public void GroundTrustRejectsStaleSession()
    {
        ARRenderGenerationToken active = new(7, 8, 9);
        ARRenderGenerationBridge.Activate(active);
        var provisional = ARGroundStateBridge.PublishProvisional(active, "test");
        Assert.True(provisional.HasGroundReference);
        Assert.Equal(provisional, ARGroundStateBridge.PublishVerified(new(6, 8, 9), "stale"));
        var suspended = ARGroundStateBridge.Suspend(active, "test");
        Assert.False(suspended.HasGroundReference);
        Assert.True(suspended.ReferenceGeneration > provisional.ReferenceGeneration);
    }

    [Fact]
    public void FloodFailsClosedWithoutTrackedGround()
    {
        ARRenderGenerationBridge.Activate(new(1, 1, 1));
        ARFloodDepthBridge.PublishLocalDepth(0.75, true, "unit test");
        Assert.False(ARFloodDepthBridge.Current.IsAvailable);
    }

    [Fact]
    public void TrackingSeparatesLifecyclePause()
    {
        ARTrackingStateBridge.PublishObservation(10, "Tracking", "", 1, false);
        var activePause = ARTrackingStateBridge.PublishObservation(10, "Paused", "InsufficientFeatures", 2, false);
        var lifecyclePause = ARTrackingStateBridge.PublishLifecycleTransition(10, "Paused", "Activity paused", false);
        Assert.Equal(1, activePause.ActivePauseTransitionCount);
        Assert.Equal(0, activePause.LifecyclePauseTransitionCount);
        Assert.Equal(1, lifecyclePause.LifecyclePauseTransitionCount);
        Assert.True(lifecyclePause.IsIntentionalLifecycleEvent);
    }

    [Fact]
    public void MetadataRejectsGeometryMismatch() =>
        Assert.False(new ARFrameMetadata(new(1, 1, 2), 100, new(3, 0, 1080, 2400)).IsValid);

    private static ARCameraPoseBridge.SpatialSnapshot CreateSpatial(long publishedAt) =>
        new(1, ARFrameMetadata.Invalid, publishedAt, true, "",
            ARCameraPoseBridge.PoseSnapshot.Unavailable,
            ARCameraPoseBridge.ProjectionSnapshot.Unavailable,
            ARCameraPoseBridge.AnchorSnapshot.Unavailable);
}
