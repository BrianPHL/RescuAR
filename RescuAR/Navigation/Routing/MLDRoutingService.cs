using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RescuAR.Diagnostics;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Routing;

/// <summary>
/// Online routing adapter for the Railway-hosted RescuAR OSRM service.
///
/// The Railway deployment is preprocessed with OSRM's MLD pipeline.
/// This class converts the OSRM GeoJSON response into RescuAR's canonical
/// RouteResult so the AR/navigation layers do not depend on OSRM-specific
/// response models.
/// </summary>
public sealed class MLDRoutingService : IRoutingService
{
    private const string LogTag =
        "RescuAR-MLD";
    public const string PrimaryBaseUrl =
        "https://rescuar-production.up.railway.app";

    public const string FallbackBaseUrl =
        "https://rescuar-production-2c22.up.railway.app";

    private static readonly HttpClient httpClient =
        new()
        {
            Timeout =
                TimeSpan.FromSeconds(
                    15)
        };

    private readonly string[] baseUrls;

    public string AlgorithmName =>
        "MLD (Railway OSRM)";

    public MLDRoutingService()
        : this(
            PrimaryBaseUrl,
            FallbackBaseUrl)
    {
    }

    public MLDRoutingService(
        params string[] baseUrls)
    {
        if (baseUrls is null ||
            baseUrls.Length == 0)
        {
            throw new ArgumentException(
                "At least one OSRM base URL is required.",
                nameof(baseUrls));
        }

        this.baseUrls =
            new string[
                baseUrls.Length];

        for (int i = 0;
             i < baseUrls.Length;
             i++)
        {
            string value =
                baseUrls[i]
                    ?.Trim()
                    .TrimEnd('/')
                ?? string.Empty;

            if (!Uri.TryCreate(
                    value,
                    UriKind.Absolute,
                    out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttps &&
                 uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new ArgumentException(
                    $"Invalid OSRM base URL: '{baseUrls[i]}'.",
                    nameof(baseUrls));
            }

            this.baseUrls[i] =
                value;
        }
    }

    public async Task<RouteResult?> FindRouteAsync(
        GeoCoordinate origin,
        GeoCoordinate destination,
        CancellationToken cancellationToken = default)
    {
        if (!origin.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(origin));
        }

        if (!destination.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination));
        }

        if (origin ==
            destination)
        {
            return new RouteResult(
                [
                    new RoutePoint(
                        origin,
                        0.0)
                ],
                0.0,
                AlgorithmName);
        }

        AndroidLog.Debug(
            LogTag,
            "MLD route request started: " +
            $"origin=({origin.Latitude:F7},{origin.Longitude:F7}), " +
            $"destination=({destination.Latitude:F7},{destination.Longitude:F7})");

        Exception? lastFailure =
            null;

        for (int i = 0;
             i < baseUrls.Length;
             i++)
        {
            string requestUrl =
                BuildRouteUrl(
                    baseUrls[i],
                    origin,
                    destination);

            AndroidLog.Debug(
                LogTag,
                $"MLD endpoint attempt {i + 1}/{baseUrls.Length}: {requestUrl}");

            try
            {
                using HttpResponseMessage response =
                    await httpClient.GetAsync(
                        requestUrl,
                        cancellationToken);

                string json =
                    await response.Content.ReadAsStringAsync(
                        cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    RouteResult? route =
                        ParseRoute(
                            json);

                    if (route is null)
                    {
                        AndroidLog.Warn(
                            LogTag,
                            "MLD returned HTTP success but no usable route.");
                    }
                    else
                    {
                        AndroidLog.Debug(
                            LogTag,
                            "MLD route parsed successfully: " +
                            $"algorithm='{route.Algorithm}', " +
                            $"points={route.Points.Count}, " +
                            $"distance={route.TotalDistanceMeters:F1} m");
                    }

                    return route;
                }

                /*
                 * A normal OSRM client error is not a reason to try another
                 * mirror: the same invalid coordinates/request would fail
                 * there as well.
                 */
                if ((int)response.StatusCode <
                    500)
                {
                    if (TryReadOsrmCode(
                            json,
                            out string code) &&
                        string.Equals(
                            code,
                            "NoRoute",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    throw new InvalidDataException(
                        $"OSRM request failed with HTTP {(int)response.StatusCode} " +
                        $"({response.StatusCode}). Response: {TrimForDiagnostic(json)}");
                }

                AndroidLog.Warn(
                    LogTag,
                    $"MLD endpoint '{baseUrls[i]}' returned " +
                    $"HTTP {(int)response.StatusCode} ({response.StatusCode}); " +
                    "trying fallback if available.");

                lastFailure =
                    new HttpRequestException(
                        $"OSRM endpoint '{baseUrls[i]}' returned " +
                        $"HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                        null,
                        response.StatusCode);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                AndroidLog.Warn(
                    LogTag,
                    $"MLD endpoint '{baseUrls[i]}' timed out.");

                lastFailure =
                    new TimeoutException(
                        $"OSRM endpoint '{baseUrls[i]}' timed out.");
            }
            catch (HttpRequestException ex)
            {
                AndroidLog.Warn(
                    LogTag,
                    $"MLD endpoint '{baseUrls[i]}' HTTP failure: " +
                    $"{ex.GetType().Name}: {ex.Message}");

                lastFailure =
                    ex;
            }
        }

        AndroidLog.Error(
            LogTag,
            "All configured Railway MLD endpoints failed. " +
            $"{lastFailure?.GetType().Name}: {lastFailure?.Message}");

        throw new HttpRequestException(
            "All configured Railway OSRM endpoints failed.",
            lastFailure);
    }

    private RouteResult? ParseRoute(
        string json)
    {
        using JsonDocument document =
            JsonDocument.Parse(
                json);

        JsonElement root =
            document.RootElement;

        string code =
            root.TryGetProperty(
                "code",
                out JsonElement codeElement)
                ? codeElement.GetString() ??
                  string.Empty
                : string.Empty;

        if (string.Equals(
                code,
                "NoRoute",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!string.Equals(
                code,
                "Ok",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"OSRM returned code '{code}'.");
        }

        if (!root.TryGetProperty(
                "routes",
                out JsonElement routesElement) ||
            routesElement.ValueKind !=
                JsonValueKind.Array ||
            routesElement.GetArrayLength() ==
                0)
        {
            return null;
        }

        JsonElement route =
            routesElement[0];

        double apiDistanceMeters =
            route.TryGetProperty(
                "distance",
                out JsonElement distanceElement) &&
            distanceElement.TryGetDouble(
                out double parsedDistance)
                ? parsedDistance
                : 0.0;

        if (!route.TryGetProperty(
                "geometry",
                out JsonElement geometry) ||
            !geometry.TryGetProperty(
                "coordinates",
                out JsonElement coordinateArray) ||
            coordinateArray.ValueKind !=
                JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "OSRM route does not contain GeoJSON geometry coordinates.");
        }

        List<RoutePoint> points =
            new();

        GeoCoordinate? previous =
            null;

        double cumulativeDistanceMeters =
            0.0;

        foreach (JsonElement pair in
                 coordinateArray.EnumerateArray())
        {
            if (pair.ValueKind !=
                    JsonValueKind.Array ||
                pair.GetArrayLength() <
                    2)
            {
                continue;
            }

            /*
             * GeoJSON coordinate order is [longitude, latitude].
             */
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

            if (previous is GeoCoordinate prior)
            {
                if (prior ==
                    coordinate)
                {
                    continue;
                }

                cumulativeDistanceMeters +=
                    prior.DistanceTo(
                        coordinate);
            }

            points.Add(
                new RoutePoint(
                    coordinate,
                    cumulativeDistanceMeters));

            previous =
                coordinate;
        }

        if (points.Count ==
            0)
        {
            return null;
        }

        /*
         * Prefer OSRM's route distance when supplied because it represents
         * the server's routed path. The per-point cumulative distance remains
         * geometry-derived and is used for route-window extraction.
         */
        double totalDistanceMeters =
            apiDistanceMeters >
            0.0
                ? apiDistanceMeters
                : cumulativeDistanceMeters;

        return new RouteResult(
            points,
            totalDistanceMeters,
            AlgorithmName);
    }

    private static string BuildRouteUrl(
        string baseUrl,
        GeoCoordinate origin,
        GeoCoordinate destination)
    {
        string coordinatePair =
            string.Format(
                CultureInfo.InvariantCulture,
                "{0:F7},{1:F7};{2:F7},{3:F7}",
                origin.Longitude,
                origin.Latitude,
                destination.Longitude,
                destination.Latitude);

        return
            $"{baseUrl}/route/v1/driving/{coordinatePair}" +
            "?overview=full&geometries=geojson&steps=true";
    }

    private static bool TryReadOsrmCode(
        string json,
        out string code)
    {
        code =
            string.Empty;

        try
        {
            using JsonDocument document =
                JsonDocument.Parse(
                    json);

            if (!document.RootElement.TryGetProperty(
                    "code",
                    out JsonElement codeElement))
            {
                return false;
            }

            code =
                codeElement.GetString() ??
                string.Empty;

            return code.Length >
                0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string TrimForDiagnostic(
        string value)
    {
        const int maxLength =
            500;

        if (string.IsNullOrWhiteSpace(
                value))
        {
            return "<empty>";
        }

        return value.Length <=
            maxLength
                ? value
                : value[..maxLength] +
                  "...";
    }
}
