using System;

namespace RescuAR.Navigation.Hazards;

public enum RouteHazardCoverage
{
    Unavailable,
    Live,
    Saved,
    Expired
}

public static class RouteHazardCoveragePolicy
{
    private static readonly TimeSpan LiveAge = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaximumCoverageAge = TimeSpan.FromHours(2);

    /// <summary>Coverage can expire while known exclusions still require avoidance.</summary>
    public static RouteHazardCoverage Evaluate(DateTimeOffset? refreshedAt, DateTimeOffset now,
        bool liveResponseReceived, bool internetAvailable)
    {
        if (refreshedAt is not { } timestamp || timestamp > now || timestamp == default)
            return RouteHazardCoverage.Unavailable;

        TimeSpan age = now - timestamp;
        if (age > MaximumCoverageAge) return RouteHazardCoverage.Expired;
        return liveResponseReceived && internetAvailable && age < LiveAge
            ? RouteHazardCoverage.Live
            : RouteHazardCoverage.Saved;
    }
}
