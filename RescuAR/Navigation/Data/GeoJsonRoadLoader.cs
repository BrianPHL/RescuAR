using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Data;

/// <summary>
/// Loader for the exact ROADS.geojson / POINTS.geojson schema supplied for
/// RescuAR.
///
/// Supported geometries:
/// - roads: LineString
/// - points: Point
///
/// GeoJSON coordinate order is [longitude, latitude].
/// </summary>
public sealed class GeoJsonRoadLoader
{
    public async Task<IReadOnlyList<GeoJsonRoadFeature>> LoadRoadsAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            stream);

        using JsonDocument document =
            await JsonDocument.ParseAsync(
                stream,
                cancellationToken:
                    cancellationToken);

        JsonElement root =
            document.RootElement;

        ValidateFeatureCollection(
            root);

        List<GeoJsonRoadFeature> result =
            new();

        foreach (JsonElement feature in
                 root.GetProperty(
                     "features")
                     .EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            JsonElement geometry =
                feature.GetProperty(
                    "geometry");

            string? geometryType =
                geometry.GetProperty(
                    "type")
                    .GetString();

            if (!string.Equals(
                    geometryType,
                    "LineString",
                    StringComparison.Ordinal))
            {
                continue;
            }

            JsonElement properties =
                feature.GetProperty(
                    "properties");

            List<GeoCoordinate> coordinates =
                new();
            bool invalidCoordinates = false;

            foreach (JsonElement pair in
                     geometry.GetProperty(
                         "coordinates")
                         .EnumerateArray())
            {
                if (pair.GetArrayLength() <
                    2)
                {
                    invalidCoordinates = true;
                    continue;
                }

                double longitude =
                    pair[0]
                        .GetDouble();

                double latitude =
                    pair[1]
                        .GetDouble();

                GeoCoordinate coordinate =
                    new(
                        latitude,
                        longitude);

                if (coordinate.IsValid)
                {
                    coordinates.Add(
                        coordinate);
                }
                else invalidCoordinates = true;
            }

            if (invalidCoordinates || coordinates.Count <
                2)
            {
                continue;
            }

            result.Add(
                new GeoJsonRoadFeature
                {
                    SourceId =
                        TryGetInt32(
                            properties,
                            "id"),

                    OsmId =
                        TryGetString(
                            properties,
                            "osm_id"),

                    Name =
                        TryGetString(
                            properties,
                            "name"),

                    Highway =
                        TryGetString(
                            properties,
                            "highway"),

                    Tags =
                        ReadTags(properties),

                    Coordinates =
                        coordinates
                });
        }

        return result;
    }

    public async Task<IReadOnlyList<GeoJsonPointFeature>> LoadPointsAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            stream);

        using JsonDocument document =
            await JsonDocument.ParseAsync(
                stream,
                cancellationToken:
                    cancellationToken);

        JsonElement root =
            document.RootElement;

        ValidateFeatureCollection(
            root);

        List<GeoJsonPointFeature> result =
            new();

        foreach (JsonElement feature in
                 root.GetProperty(
                     "features")
                     .EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            JsonElement geometry =
                feature.GetProperty(
                    "geometry");

            string? geometryType =
                geometry.GetProperty(
                    "type")
                    .GetString();

            if (!string.Equals(
                    geometryType,
                    "Point",
                    StringComparison.Ordinal))
            {
                continue;
            }

            JsonElement pair =
                geometry.GetProperty(
                    "coordinates");

            if (pair.GetArrayLength() <
                2)
            {
                continue;
            }

            GeoCoordinate coordinate =
                new(
                    pair[1]
                        .GetDouble(),
                    pair[0]
                        .GetDouble());

            if (!coordinate.IsValid)
            {
                continue;
            }

            JsonElement properties =
                feature.GetProperty(
                    "properties");

            result.Add(
                new GeoJsonPointFeature
                {
                    SourceId =
                        TryGetInt32(
                            properties,
                            "id"),

                    OsmId =
                        TryGetString(
                            properties,
                            "osm_id"),

                    Name =
                        TryGetString(
                            properties,
                            "name"),

                    Highway =
                        TryGetString(
                            properties,
                            "highway"),

                    Barrier =
                        TryGetString(
                            properties,
                            "barrier"),

                    Coordinate =
                        coordinate,

                    Tags =
                        ReadTags(properties)
                });
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> ReadTags(JsonElement properties)
    {
        var tags = new Dictionary<string, string>(
            OsmOtherTagsParser.Parse(TryGetString(properties, "other_tags")),
            StringComparer.OrdinalIgnoreCase);
        foreach (var property in properties.EnumerateObject())
        {
            if (property.Name is "id" or "osm_id" or "name" or "highway" or "other_tags" ||
                property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Object or JsonValueKind.Array)
                continue;
            string value = property.Value.ToString();
            if (!string.IsNullOrWhiteSpace(value)) tags[property.Name] = value;
        }
        return tags;
    }

    private static void ValidateFeatureCollection(
        JsonElement root)
    {
        string? type =
            root.GetProperty(
                "type")
                .GetString();

        if (!string.Equals(
                type,
                "FeatureCollection",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "GeoJSON root is not a FeatureCollection.");
        }
    }

    private static string? TryGetString(
        JsonElement properties,
        string name)
    {
        if (!properties.TryGetProperty(
                name,
                out JsonElement value) ||
            value.ValueKind ==
                JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind ==
            JsonValueKind.String
                ? value.GetString()
                : value.ToString();
    }

    private static int TryGetInt32(
        JsonElement properties,
        string name)
    {
        if (!properties.TryGetProperty(
                name,
                out JsonElement value))
        {
            return 0;
        }

        return value.TryGetInt32(
            out int result)
                ? result
                : 0;
    }
}
