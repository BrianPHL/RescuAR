using System;

namespace RescuAR.AR;

/// <summary>Preserves same-frame floor support when reducing probe work.</summary>
public readonly record struct ARGroundProbePlan(int WorldDownProbes, int ScreenProbes)
{
    public const int MinimumSpatialSamples = 2;

    public static ARGroundProbePlan Create(int maximumProbesPerSweep, bool reduced)
    {
        int limit = Math.Max(MinimumSpatialSamples, maximumProbesPerSweep);
        int worldDown = reduced ? 0 : Math.Min(3, limit - MinimumSpatialSamples);
        int screen = reduced ? MinimumSpatialSamples : Math.Min(4, limit - worldDown);
        return new(worldDown, screen);
    }
}
