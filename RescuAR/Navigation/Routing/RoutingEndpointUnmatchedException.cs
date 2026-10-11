using System;

namespace RescuAR.Navigation.Routing;

/// <summary>The provider cannot match an endpoint to its bounded pedestrian network.</summary>
public sealed class RoutingEndpointUnmatchedException : Exception
{
    public RoutingEndpointUnmatchedException()
        : base("OSRM NoSegment: an endpoint has no eligible segment within the requested radius.") { }
}
