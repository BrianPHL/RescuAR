using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RescuAR.Diagnostics;
using RescuAR.Navigation.Hazards;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Routing;

/// <summary>
/// Online routing adapter for the Railway-hosted RescuAR OSRM service.
///
/// Ordinary routing preserves the existing OSRM behavior. Stage 10 adds a
/// hazard-aware path that first asks MLD for multiple alternatives and rejects
/// any route intersecting active RescuAR hazard exclusion zones. If all direct
/// alternatives are unsafe, MLD is asked to route through bounded bypass
/// waypoints around the first blocking hazard. The returned geometry is always
/// revalidated client-side before it can reach AR navigation.
/// </summary>
public sealed class MLDRoutingService : IHazardAwareRoutingService
{
    private const string LogTag =
        "RescuAR-MLD";

    private const string HazardAwareAlgorithmName =
        "MLD (Railway OSRM, Hazard-Aware)";

    private const int HazardDirectAlternativeCount =
        3;

    private const int HazardDetourAlternativeCount =
        2;

    private const int MaximumHazardDetourPasses =
        3;

    private const double BypassNearMarginMeters =
        20.0;

    private const double BypassFarMarginMeters =
        40.0;

    private const double BypassWaypointExtraClearanceMeters =
        4.0;

    private const double EarthRadiusMeters =
        6371008.8;

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
        ValidateCoordinates(
            origin,
            destination);

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
            try
            {
                IReadOnlyList<RouteResult> routes =
                    await RequestRoutesFromEndpointAsync(
                        baseUrls[i],
                        new[]
                        {
                            origin,
                            destination
                        },
                        0,
                        AlgorithmName,
                        cancellationToken);

                RouteResult? route =
                    routes.Count > 0
                        ? routes[0]
                        : null;

                if (route is null)
                {
                    AndroidLog.Warn(
                        LogTag,
                        "MLD returned no usable route.");
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

    /// <summary>
    /// Online MLD hazard-aware route calculation.
    ///
    /// OSRM does not consume RescuAR's hazard circles directly. Instead, this
    /// method uses two client-side safeguards while keeping path computation on
    /// MLD:
    /// 1) request multiple MLD alternatives and select the first safe one;
    /// 2) if every direct alternative is unsafe, force MLD through candidate
    ///    bypass waypoints around the blocking hazard and revalidate the full
    ///    returned geometry.
    ///
    /// If MLD cannot produce a verified-safe route, null is returned so the
    /// HybridRoutingService can fall back to hazard-aware A*.
    /// </summary>
    public async Task<RouteResult?> FindRouteAvoidingHazardsAsync(
        GeoCoordinate origin,
        GeoCoordinate destination,
        IReadOnlyList<RouteHazard> hazards,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            hazards);

        ValidateCoordinates(
            origin,
            destination);

        cancellationToken.ThrowIfCancellationRequested();

        RouteHazard[] validHazards =
            hazards
                .Where(
                    hazard =>
                        hazard is not null &&
                        hazard.Coordinate.IsValid &&
                        double.IsFinite(hazard.RadiusMeters) &&
                        hazard.RadiusMeters > 0.0)
                .ToArray();

        if (validHazards.Length == 0)
        {
            return await FindRouteAsync(
                origin,
                destination,
                cancellationToken);
        }

        if (validHazards.Any(
                hazard =>
                    destination.DistanceTo(hazard.Coordinate) <=
                    hazard.RadiusMeters))
        {
            AndroidLog.Warn(
                LogTag,
                "MLD HAZARD-AWARE request rejected because the destination " +
                "lies inside an active hazard exclusion zone.");

            return null;
        }

        AndroidLog.Warn(
            LogTag,
            "MLD HAZARD-AWARE route request started: " +
            $"origin=({origin.Latitude:F7},{origin.Longitude:F7}), " +
            $"destination=({destination.Latitude:F7},{destination.Longitude:F7}), " +
            $"hazards={validHazards.Length}.");

        Exception? lastFailure =
            null;

        for (int endpointIndex = 0;
             endpointIndex < baseUrls.Length;
             endpointIndex++)
        {
            string baseUrl =
                baseUrls[endpointIndex];

            try
            {
                IReadOnlyList<RouteResult> directCandidates =
                    await RequestRoutesFromEndpointAsync(
                        baseUrl,
                        new[]
                        {
                            origin,
                            destination
                        },
                        HazardDirectAlternativeCount,
                        HazardAwareAlgorithmName,
                        cancellationToken);

                if (directCandidates.Count == 0)
                {
                    AndroidLog.Warn(
                        LogTag,
                        "MLD HAZARD-AWARE endpoint returned no direct route. " +
                        "Hybrid routing may use hazard-aware A*.");

                    return null;
                }

                for (int candidateIndex = 0;
                     candidateIndex < directCandidates.Count;
                     candidateIndex++)
                {
                    RouteResult candidate =
                        directCandidates[candidateIndex];

                    if (RouteHazardGeometry.RouteAvoidsHazardsFromOrigin(
                            candidate,
                            origin,
                            validHazards))
                    {
                        AndroidLog.Warn(
                            LogTag,
                            "MLD HAZARD-AWARE SAFE direct route selected: " +
                            $"candidate={candidateIndex + 1}/{directCandidates.Count}, " +
                            $"points={candidate.Points.Count}, " +
                            $"distance={candidate.TotalDistanceMeters:F1} m, " +
                            $"hazards={validHazards.Length}.");

                        return candidate;
                    }
                }

                RouteHazardGeometry.RouteHazardIntersection firstUnsafe =
                    RouteHazardGeometry.FindFirstUnsafeIntersectionFromOrigin(
                        directCandidates[0],
                        origin,
                        validHazards);

                AndroidLog.Warn(
                    LogTag,
                    "Every direct MLD alternative intersects an active hazard. " +
                    $"Trying bounded MLD bypass waypoints; " +
                    $"firstHazard='{firstUnsafe.Hazard?.Id ?? "<unknown>"}', " +
                    $"distanceAhead={firstUnsafe.DistanceAheadMeters:F1} m.");

                RouteResult? detourRoute =
                    await TryFindMldDetourRouteAsync(
                        baseUrl,
                        origin,
                        destination,
                        directCandidates[0],
                        validHazards,
                        cancellationToken);

                if (detourRoute is not null)
                {
                    AndroidLog.Warn(
                        LogTag,
                        "MLD HAZARD-AWARE SAFE detour selected: " +
                        $"points={detourRoute.Points.Count}, " +
                        $"distance={detourRoute.TotalDistanceMeters:F1} m, " +
                        $"hazards={validHazards.Length}.");

                    return detourRoute;
                }

                /*
                 * A healthy endpoint answered but could not produce a route
                 * that survives client-side hazard validation. The configured
                 * mirror normally uses the same graph, so repeating the full
                 * detour search there only adds latency. Let HybridRoutingService
                 * move directly to hazard-aware A*.
                 */
                AndroidLog.Warn(
                    LogTag,
                    "MLD HAZARD-AWARE exhausted safe alternatives and detours. " +
                    "Returning no safe MLD route so hybrid routing can use A*.");

                return null;
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                AndroidLog.Warn(
                    LogTag,
                    $"MLD hazard-aware endpoint '{baseUrl}' timed out; " +
                    "trying fallback endpoint if available.");

                lastFailure =
                    new TimeoutException(
                        $"OSRM endpoint '{baseUrl}' timed out.");
            }
            catch (HttpRequestException ex)
            {
                AndroidLog.Warn(
                    LogTag,
                    $"MLD hazard-aware endpoint '{baseUrl}' transport failure: " +
                    $"{ex.Message}");

                lastFailure =
                    ex;
            }
        }

        throw new HttpRequestException(
            "All configured Railway OSRM endpoints failed during hazard-aware routing.",
            lastFailure);
    }

    private async Task<RouteResult?> TryFindMldDetourRouteAsync(
        string baseUrl,
        GeoCoordinate origin,
        GeoCoordinate destination,
        RouteResult baselineRoute,
        IReadOnlyList<RouteHazard> hazards,
        CancellationToken cancellationToken)
    {
        RouteResult currentRoute =
            baselineRoute;

        List<GeoCoordinate> committedBypassWaypoints =
            new();

        HashSet<string> hazardsAlreadyBypassed =
            new(StringComparer.Ordinal);

        for (int pass = 0;
             pass < MaximumHazardDetourPasses;
             pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RouteHazardGeometry.RouteHazardIntersection unsafeIntersection =
                RouteHazardGeometry.FindFirstUnsafeIntersectionFromOrigin(
                    currentRoute,
                    origin,
                    hazards);

            if (!unsafeIntersection.IsAffected ||
                unsafeIntersection.Hazard is null)
            {
                return currentRoute;
            }

            RouteHazard blockingHazard =
                unsafeIntersection.Hazard;

            IReadOnlyList<GeoCoordinate> bypassCandidates =
                BuildBypassCandidates(
                    currentRoute,
                    unsafeIntersection,
                    blockingHazard);

            RouteResult? bestProgressiveRoute =
                null;

            GeoCoordinate bestBypassWaypoint =
                default;

            double bestNextHazardDistance =
                double.NegativeInfinity;

            for (int bypassIndex = 0;
                 bypassIndex < bypassCandidates.Count;
                 bypassIndex++)
            {
                GeoCoordinate waypoint =
                    bypassCandidates[bypassIndex];

                if (IsWaypointInsideAnyHazard(
                        waypoint,
                        hazards))
                {
                    continue;
                }

                List<GeoCoordinate> coordinates =
                    new(
                        2 +
                        committedBypassWaypoints.Count +
                        1)
                    {
                        origin
                    };

                coordinates.AddRange(
                    committedBypassWaypoints);

                coordinates.Add(
                    waypoint);

                coordinates.Add(
                    destination);

                IReadOnlyList<RouteResult> candidates =
                    await RequestRoutesFromEndpointAsync(
                        baseUrl,
                        coordinates,
                        HazardDetourAlternativeCount,
                        HazardAwareAlgorithmName,
                        cancellationToken);

                for (int candidateIndex = 0;
                     candidateIndex < candidates.Count;
                     candidateIndex++)
                {
                    RouteResult candidate =
                        candidates[candidateIndex];

                    List<RouteHazard> requiredAvoidance =
                        hazards
                            .Where(
                                hazard =>
                                    hazardsAlreadyBypassed.Contains(hazard.Id) ||
                                    string.Equals(
                                        hazard.Id,
                                        blockingHazard.Id,
                                        StringComparison.Ordinal))
                            .ToList();

                    if (!RouteHazardGeometry.RouteAvoidsHazardsFromOrigin(
                            candidate,
                            origin,
                            requiredAvoidance))
                    {
                        continue;
                    }

                    RouteHazardGeometry.RouteHazardIntersection nextUnsafe =
                        RouteHazardGeometry.FindFirstUnsafeIntersectionFromOrigin(
                            candidate,
                            origin,
                            hazards);

                    if (!nextUnsafe.IsAffected)
                    {
                        AndroidLog.Debug(
                            LogTag,
                            "MLD hazard detour candidate validated SAFE: " +
                            $"pass={pass + 1}, bypass={bypassIndex + 1}, " +
                            $"waypoint=({waypoint.Latitude:F7},{waypoint.Longitude:F7}), " +
                            $"distance={candidate.TotalDistanceMeters:F1} m.");

                        return candidate;
                    }

                    double nextDistance =
                        nextUnsafe.DistanceAheadMeters;

                    if (bestProgressiveRoute is null ||
                        nextDistance >
                            bestNextHazardDistance + 1.0 ||
                        (Math.Abs(
                             nextDistance -
                             bestNextHazardDistance) <= 1.0 &&
                         candidate.TotalDistanceMeters <
                             bestProgressiveRoute.TotalDistanceMeters))
                    {
                        bestProgressiveRoute =
                            candidate;

                        bestBypassWaypoint =
                            waypoint;

                        bestNextHazardDistance =
                            nextDistance;
                    }
                }
            }

            if (bestProgressiveRoute is null)
            {
                AndroidLog.Warn(
                    LogTag,
                    "MLD could not construct a validated bypass around hazard " +
                    $"'{blockingHazard.Id}' during pass {pass + 1}.");

                return null;
            }

            committedBypassWaypoints.Add(
                bestBypassWaypoint);

            hazardsAlreadyBypassed.Add(
                blockingHazard.Id);

            currentRoute =
                bestProgressiveRoute;

            AndroidLog.Debug(
                LogTag,
                "MLD progressive hazard bypass accepted: " +
                $"pass={pass + 1}, " +
                $"hazard='{blockingHazard.Id}', " +
                $"committedWaypoints={committedBypassWaypoints.Count}, " +
                $"nextUnsafeDistance={bestNextHazardDistance:F1} m.");
        }

        return RouteHazardGeometry.RouteAvoidsHazardsFromOrigin(
                currentRoute,
                origin,
                hazards)
            ? currentRoute
            : null;
    }

    private async Task<IReadOnlyList<RouteResult>> RequestRoutesFromEndpointAsync(
        string baseUrl,
        IReadOnlyList<GeoCoordinate> coordinates,
        int alternatives,
        string algorithmName,
        CancellationToken cancellationToken)
    {
        string requestUrl =
            BuildRouteUrl(
                baseUrl,
                coordinates,
                alternatives);

        AndroidLog.Debug(
            LogTag,
            $"MLD endpoint request: {requestUrl}");

        using HttpResponseMessage response =
            await httpClient.GetAsync(
                requestUrl,
                cancellationToken);

        string json =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return ParseRoutes(
                json,
                algorithmName);
        }

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
                return Array.Empty<RouteResult>();
            }

            throw new InvalidDataException(
                $"OSRM request failed with HTTP {(int)response.StatusCode} " +
                $"({response.StatusCode}). Response: {TrimForDiagnostic(json)}");
        }

        throw new HttpRequestException(
            $"OSRM endpoint '{baseUrl}' returned HTTP {(int)response.StatusCode} " +
            $"({response.StatusCode}).",
            null,
            response.StatusCode);
    }

    private static IReadOnlyList<RouteResult> ParseRoutes(
        string json,
        string algorithmName)
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
            return Array.Empty<RouteResult>();
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
            return Array.Empty<RouteResult>();
        }

        List<RouteResult> routes =
            new();

        foreach (JsonElement routeElement in
                 routesElement.EnumerateArray())
        {
            RouteResult? route =
                ParseRouteElement(
                    routeElement,
                    algorithmName);

            if (route is not null)
            {
                routes.Add(
                    route);
            }
        }

        return routes;
    }

    private static RouteResult? ParseRouteElement(
        JsonElement route,
        string algorithmName)
    {
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

        double totalDistanceMeters =
            apiDistanceMeters >
            0.0
                ? apiDistanceMeters
                : cumulativeDistanceMeters;

        return new RouteResult(
            points,
            totalDistanceMeters,
            algorithmName);
    }

    private static IReadOnlyList<GeoCoordinate> BuildBypassCandidates(
        RouteResult route,
        RouteHazardGeometry.RouteHazardIntersection intersection,
        RouteHazard hazard)
    {
        double forwardEast =
            1.0;

        double forwardNorth =
            0.0;

        int segmentIndex =
            intersection.SegmentIndex;

        if (segmentIndex >= 0 &&
            segmentIndex + 1 < route.Points.Count)
        {
            GeoCoordinate from =
                route.Points[segmentIndex]
                    .Coordinate;

            GeoCoordinate to =
                route.Points[segmentIndex + 1]
                    .Coordinate;

            (double East, double North) delta =
                ToLocalMeters(
                    to,
                    from);

            double length =
                Math.Sqrt(
                    (delta.East * delta.East) +
                    (delta.North * delta.North));

            if (length >
                0.5)
            {
                forwardEast =
                    delta.East /
                    length;

                forwardNorth =
                    delta.North /
                    length;
            }
        }

        double leftEast =
            -forwardNorth;

        double leftNorth =
            forwardEast;

        double nearClearance =
            hazard.RadiusMeters +
            BypassNearMarginMeters;

        double farClearance =
            hazard.RadiusMeters +
            BypassFarMarginMeters;

        List<(double East, double North)> offsets =
            new()
            {
                (leftEast * nearClearance,
                 leftNorth * nearClearance),

                (-leftEast * nearClearance,
                 -leftNorth * nearClearance),

                (leftEast * farClearance,
                 leftNorth * farClearance),

                (-leftEast * farClearance,
                 -leftNorth * farClearance),

                (((leftEast * 0.82) + (forwardEast * 0.57)) * farClearance,
                 ((leftNorth * 0.82) + (forwardNorth * 0.57)) * farClearance),

                (((-leftEast * 0.82) + (forwardEast * 0.57)) * farClearance,
                 ((-leftNorth * 0.82) + (forwardNorth * 0.57)) * farClearance)
            };

        List<GeoCoordinate> candidates =
            new();

        for (int i = 0;
             i < offsets.Count;
             i++)
        {
            GeoCoordinate coordinate =
                OffsetCoordinate(
                    hazard.Coordinate,
                    offsets[i].East,
                    offsets[i].North);

            if (!coordinate.IsValid)
            {
                continue;
            }

            bool duplicate =
                candidates.Any(
                    existing =>
                        existing.DistanceTo(coordinate) <
                        3.0);

            if (!duplicate)
            {
                candidates.Add(
                    coordinate);
            }
        }

        return candidates;
    }

    private static bool IsWaypointInsideAnyHazard(
        GeoCoordinate waypoint,
        IReadOnlyList<RouteHazard> hazards)
    {
        for (int i = 0;
             i < hazards.Count;
             i++)
        {
            RouteHazard hazard =
                hazards[i];

            if (waypoint.DistanceTo(hazard.Coordinate) <=
                hazard.RadiusMeters +
                BypassWaypointExtraClearanceMeters)
            {
                return true;
            }
        }

        return false;
    }

    private static (double East, double North) ToLocalMeters(
        GeoCoordinate coordinate,
        GeoCoordinate origin)
    {
        double originLatitudeRadians =
            DegreesToRadians(
                origin.Latitude);

        double longitudeDeltaRadians =
            DegreesToRadians(
                coordinate.Longitude -
                origin.Longitude);

        double latitudeDeltaRadians =
            DegreesToRadians(
                coordinate.Latitude -
                origin.Latitude);

        return (
            longitudeDeltaRadians *
                Math.Cos(originLatitudeRadians) *
                EarthRadiusMeters,
            latitudeDeltaRadians *
                EarthRadiusMeters);
    }

    private static GeoCoordinate OffsetCoordinate(
        GeoCoordinate origin,
        double eastMeters,
        double northMeters)
    {
        double latitudeRadians =
            DegreesToRadians(
                origin.Latitude);

        double latitudeDeltaDegrees =
            RadiansToDegrees(
                northMeters /
                EarthRadiusMeters);

        double longitudeScale =
            Math.Cos(
                latitudeRadians);

        if (Math.Abs(longitudeScale) <
            0.000001)
        {
            longitudeScale =
                longitudeScale < 0.0
                    ? -0.000001
                    : 0.000001;
        }

        double longitudeDeltaDegrees =
            RadiansToDegrees(
                eastMeters /
                (EarthRadiusMeters * longitudeScale));

        return new GeoCoordinate(
            origin.Latitude +
                latitudeDeltaDegrees,
            origin.Longitude +
                longitudeDeltaDegrees);
    }

    private static string BuildRouteUrl(
        string baseUrl,
        IReadOnlyList<GeoCoordinate> coordinates,
        int alternatives)
    {
        string coordinatePath =
            string.Join(
                ";",
                coordinates.Select(
                    coordinate =>
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "{0:F7},{1:F7}",
                            coordinate.Longitude,
                            coordinate.Latitude)));

        string alternativesQuery =
            alternatives > 0
                ? $"&alternatives={alternatives}"
                : string.Empty;

        return
            $"{baseUrl}/route/v1/foot/{coordinatePath}" +
            "?overview=full&geometries=geojson&steps=true" +
            alternativesQuery;
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

    private static void ValidateCoordinates(
        GeoCoordinate origin,
        GeoCoordinate destination)
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
    }

    private static double DegreesToRadians(
        double degrees)
    {
        return degrees *
            (Math.PI / 180.0);
    }

    private static double RadiansToDegrees(
        double radians)
    {
        return radians *
            (180.0 / Math.PI);
    }
}
