using System;
using System.Collections.Generic;

namespace RescuAR.Navigation.Data;

/// <summary>
/// Pedestrian-first filter for the supplied Marikina OSM road export.
///
/// The filter is intentionally conservative about explicit access bans.
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
            "unclassified",
            "tertiary",
            "tertiary_link",
            "secondary",
            "secondary_link",
            "primary",
            "primary_link",
            "living_street",
            "track",
            "cycleway",
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

        /*
         * OSM cycleways are not automatically pedestrian routes everywhere.
         * Admit them only when the export does not explicitly deny walking.
         * A later policy layer can tighten this further if field testing
         * identifies unsuitable bicycle-only facilities.
         */
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
