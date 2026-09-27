using System;
using System.Collections.Generic;

namespace RescuAR.Navigation.Data;

/// <summary>
/// Pedestrian-first filter for the supplied Marikina OSM road export.
///
/// Motor-road centerlines are not pedestrian sidewalks or verified crossings.
/// It does not treat motor-vehicle oneway restrictions as pedestrian oneway
/// restrictions.
/// </summary>
public sealed class PedestrianRoadFilter
{
    private static readonly HashSet<string> AllowedHighwayTypes =
        new(
            StringComparer.OrdinalIgnoreCase)
        {
            "footway",
            "path",
            "pedestrian",
            "steps",
            "residential",
            "service",
            "living_street",
            "corridor"
        };

    private static readonly HashSet<string> ExplicitlyDeniedValues =
        new(
            StringComparer.OrdinalIgnoreCase)
        {
            "no",
            "private"
        };

    public bool IsWalkable(
        GeoJsonRoadFeature feature)
    {
        ArgumentNullException.ThrowIfNull(
            feature);

        if (string.IsNullOrWhiteSpace(
                feature.Highway) ||
            !AllowedHighwayTypes.Contains(
                feature.Highway))
        {
            return false;
        }

        if (HasDeniedTag(
                feature.Tags,
                "foot"))
        {
            return false;
        }

        if (HasDeniedTag(
                feature.Tags,
                "access"))
        {
            return false;
        }

        // A vehicle service way mapped beneath a building is not proof of a
        // pedestrian passage. Explicit footway passages remain graph edges.
        if (string.Equals(feature.Highway, "service",
                StringComparison.OrdinalIgnoreCase) &&
            feature.Tags.TryGetValue("tunnel", out string? tunnel) &&
            string.Equals(tunnel, "building_passage",
                StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool HasDeniedTag(
        IReadOnlyDictionary<string, string> tags,
        string key)
    {
        return tags.TryGetValue(
                key,
                out string? value) &&
            ExplicitlyDeniedValues.Contains(
                value);
    }
}
