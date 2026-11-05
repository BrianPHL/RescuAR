namespace RescuAR.AR;

/// <summary>Never present scene geometry without its live camera background.
/// In-flight conversion still owns the single RGBA target; do not reuse it.</summary>
public static class ARCameraPresentationPolicy
{
    private static readonly object sync = new();
    private static ARRenderGenerationToken presentedGeneration;
    private static long presentedAt = long.MinValue;

    public static bool CanPresent(long graphicsGeneration, ARFrameMetadata metadata,
        bool hasTexture, long monotonicNow) => graphicsGeneration <= 0 ||
        (hasTexture && metadata.Generation.GraphicsGeneration == graphicsGeneration &&
         metadata.IsFresh(ARCameraTextureBridge.MaximumTextureAgeMilliseconds, monotonicNow) &&
         ARRenderGenerationBridge.IsCurrent(metadata.Generation));

    public static void RecordPresented(ARFrameMetadata metadata, long monotonicNow)
    {
        if (!CanPresent(metadata.Generation.GraphicsGeneration, metadata, true, monotonicNow) ||
            !metadata.IsValid) return;
        lock (sync) { presentedGeneration = metadata.Generation; presentedAt = monotonicNow; }
    }

    // UI polling can coincide with an in-flight conversion even while frames
    // are being presented between polls. Monitor completed draws, not samples
    // of the temporarily empty texture slot. This never authorizes a draw.
    public static bool HasRecentPresentation(ARRenderGenerationToken generation, long monotonicNow)
    {
        long at;
        lock (sync)
        {
            if (presentedGeneration != generation) return false;
            at = presentedAt;
        }
        return ARRenderGenerationBridge.IsCurrent(generation) && monotonicNow >= at &&
            monotonicNow - at <= ARCameraTextureBridge.MaximumTextureAgeMilliseconds;
    }
}
