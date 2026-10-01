using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RescuAR.Navigation.Data;

/// <summary>Shared evacuation rules. Docker converts the same embedded JSON to Lua.</summary>
public static class EvacuationRoutingPolicy
{
    private static readonly JsonElement Policy = Load();
    public static double MaximumOriginSnapMeters => Number("maximumOriginSnapMeters");
    public static double MaximumDestinationSnapMeters => Number("maximumDestinationSnapMeters");
    public static double EscapeProgressMeters => Number("escapeProgressMeters");

    private static JsonElement Load()
    {
        using Stream stream = typeof(EvacuationRoutingPolicy).Assembly.GetManifestResourceStream(
            "RescuAR.Navigation.Data.evacuation-policy.json") ??
            throw new InvalidOperationException("Evacuation routing policy was not embedded.");
        using JsonDocument document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    private static double Number(string key) => Policy.GetProperty(key).GetDouble();
    private static bool Contains(string key, string value)
    {
        foreach (JsonElement item in Policy.GetProperty(key).EnumerateArray())
            if (string.Equals(item.GetString(), value, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static string Tag(IReadOnlyDictionary<string, string> tags, string key) =>
        tags.TryGetValue(key, out string? value) ? value.Trim().ToLowerInvariant() : string.Empty;

    public static bool AccessDenied(IReadOnlyDictionary<string, string> tags, string? direction = null)
    {
        string foot = direction is null ? Tag(tags, "foot") : Tag(tags, "foot:" + direction);
        if (foot.Length == 0) foot = Tag(tags, "foot");
        if (Contains("deniedFoot", foot)) return true;
        if (Contains("allowedFoot", foot)) return false;
        string access = direction is null ? Tag(tags, "access") : Tag(tags, "access:" + direction);
        if (access.Length == 0) access = Tag(tags, "access");
        return Contains("deniedAccess", access);
    }

    public static bool IsBlockedPoint(string? barrier, IReadOnlyDictionary<string, string> tags)
    {
        string kind = (string.IsNullOrWhiteSpace(barrier) ? Tag(tags, "barrier") : barrier).Trim().ToLowerInvariant();
        return AccessDenied(tags) || Tag(tags, "impassable") == "yes" ||
            Tag(tags, "status") == "impassable" || Contains("hardBarriers", kind) ||
            (Contains("gates", kind) && Tag(tags, "locked") == "yes") ||
            Tag(tags, "crossing") == "no";
    }

    public static bool IsDirectionAllowed(GeoJsonRoadFeature feature, bool forward)
    {
        string highway = (feature.Highway ?? string.Empty).Trim().ToLowerInvariant();
        var tags = feature.Tags;
        string foot = Tag(tags, forward ? "foot:forward" : "foot:backward");
        if (foot.Length == 0) foot = Tag(tags, "foot");
        bool explicitFoot = Contains("allowedFoot", foot);
        if ((!Contains("allowedHighways", highway) &&
             !(Contains("explicitFootHighways", highway) && explicitFoot)) ||
            AccessDenied(tags, forward ? "forward" : "backward") || IsPhysicalWayBlocked(tags) ||
            (Tag(tags, "motorroad") == "yes" && !explicitFoot) ||
            (highway == "service" && Tag(tags, "tunnel") == "building_passage" && !explicitFoot))
            return false;
        string oneway = Tag(tags, "oneway:foot");
        return forward ? oneway != "-1" : oneway is not ("yes" or "1" or "true");
    }

    private static bool IsPhysicalWayBlocked(IReadOnlyDictionary<string, string> tags) =>
        Tag(tags, "impassable") == "yes" || Tag(tags, "status") == "impassable" ||
        Tag(tags, "area") == "yes" || Contains("hardBarriers", Tag(tags, "barrier")) ||
        (Contains("gates", Tag(tags, "barrier")) && Tag(tags, "locked") == "yes") ||
        HasActiveTag(tags, "construction") || HasActiveTag(tags, "proposed") ||
        (Tag(tags, "footway") == "crossing" && Tag(tags, "crossing") == "no");

    private static bool HasActiveTag(IReadOnlyDictionary<string, string> tags, string key) =>
        Tag(tags, key) is not ("" or "no" or "false");

    public static double CostFactor(string highway, IReadOnlyDictionary<string, string> tags)
    {
        highway = highway.Trim().ToLowerInvariant();
        double factor = 1.0;
        if (Tag(tags, "access") == "private") factor *= Number("privateFactor");
        if (Contains("majorHighways", highway)) factor *= Number("majorRoadFactor");
        if (Tag(tags, "footway") == "crossing" &&
            !Contains("markedCrossings", Tag(tags, "crossing")) &&
            Tag(tags, "crossing:markings") is not ("yes" or "zebra"))
            factor *= Number("unmarkedCrossingFactor");
        if (Policy.GetProperty("surfaceFactors").TryGetProperty(Tag(tags, "surface"), out var surface))
            factor *= surface.GetDouble();
        return factor;
    }
}
