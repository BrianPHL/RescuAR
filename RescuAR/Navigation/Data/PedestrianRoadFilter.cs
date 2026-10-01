using System;
using System.Collections.Generic;

namespace RescuAR.Navigation.Data;

/// <summary>Uses the evacuation policy shared with Docker's MLD profile.</summary>
public sealed class PedestrianRoadFilter
{
    public bool IsWalkable(GeoJsonRoadFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return EvacuationRoutingPolicy.IsDirectionAllowed(feature, true) ||
            EvacuationRoutingPolicy.IsDirectionAllowed(feature, false);
    }

    public static bool HasExplicitlyDeniedAccess(IReadOnlyDictionary<string, string> tags) =>
        EvacuationRoutingPolicy.AccessDenied(tags);
}
