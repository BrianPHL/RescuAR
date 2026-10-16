using System;

namespace RescuAR.AR;

/// <summary>Bounds passthrough conversion work without changing display geometry or camera UVs.</summary>
public static class ARCameraConversionPolicy
{
    public const uint MaximumTargetEdge = 1280;
    public const ulong InitialFenceWaitNanoseconds = 4_000_000;
    public const long PendingFrameTimeoutMilliseconds = 2000;

    public static (uint Width, uint Height) GetTargetSize(uint width, uint height)
    {
        if (width == 0 || height == 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Camera dimensions must be nonzero.");

        double scale = Math.Min(1.0, MaximumTargetEdge / (double)Math.Max(width, height));
        return (Math.Max(1u, (uint)Math.Round(width * scale)),
            Math.Max(1u, (uint)Math.Round(height * scale)));
    }
}
