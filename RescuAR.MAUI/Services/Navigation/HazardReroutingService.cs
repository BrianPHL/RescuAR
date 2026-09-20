using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using RescuAR.App.Models;
using RescuAR.App.Services.Reports;
using RescuAR.Diagnostics;
using RescuAR.Navigation.Hazards;
using RescuAR.Navigation.Models;

#if ANDROID
using Android.Util;
#endif

namespace RescuAR.MAUI.Services.Navigation;

/// <summary>
/// Stage 10 route-safety monitor.
///
/// Production hazards currently come from geolocated, Approved community
/// reports. Only Flood Warning and Road Hazard categories are treated as
/// routing exclusions. Resolved/Pending reports never invalidate a route.
///
/// Remote hazard snapshots are retained in memory when connectivity is lost,
/// allowing the navigation stack to keep avoiding the last verified hazards
/// while offline. New hazard information still requires network access.
/// </summary>
public sealed class HazardReroutingService
{
    private const string LogTag =
        "RescuAR-HazardReroute";

    private static readonly TimeSpan RemoteRefreshInterval =
        TimeSpan.FromSeconds(8);

    private static readonly TimeSpan PerHazardRerouteCooldown =
        TimeSpan.FromSeconds(20);

    private static readonly TimeSpan MaximumOfflineCacheAge =
        TimeSpan.FromHours(2);

    private const string HazardCachePreferenceKey =
        "RescuAR.Navigation.VerifiedHazards.V1";

    private readonly CommunityReportService communityReportService;

    private readonly object sync =
        new();

    private RouteHazard[] verifiedRemoteHazards =
        Array.Empty<RouteHazard>();

#if RESCUAR_DIAGNOSTICS
    private RouteHazard[] developerHazards =
        Array.Empty<RouteHazard>();
#endif

    private readonly Dictionary<string, DateTimeOffset> lastRerouteAttemptUtcByHazard =
        new(StringComparer.Ordinal);

    private DateTimeOffset lastRemoteRefreshAttemptUtc =
        DateTimeOffset.MinValue;

    private DateTimeOffset? lastRemoteRefreshSuccessUtc;

    private int remoteRefreshInProgress;

    private string? lastLoggedHazardId;

    private int lastLoggedDistanceBucket =
        -1;

    public HazardReroutingService()
        : this(new CommunityReportService())
    {
    }

    public HazardReroutingService(
        CommunityReportService communityReportService)
    {
        this.communityReportService =
            communityReportService ??
            throw new ArgumentNullException(
                nameof(communityReportService));

        LoadVerifiedHazardCache();
    }

    public readonly record struct HazardRouteAssessment(
        bool IsUnsafe,
        RouteHazard? PrimaryHazard,
        double DistanceAheadMeters,
        double RouteClearanceMeters,
        IReadOnlyList<RouteHazard> ActiveHazards,
        DateTimeOffset? LastRemoteRefreshSuccessUtc)
    {
        public static HazardRouteAssessment Safe(
            IReadOnlyList<RouteHazard> hazards,
            DateTimeOffset? lastRemoteRefreshSuccessUtc) =>
            new(
                false,
                null,
                double.PositiveInfinity,
                double.PositiveInfinity,
                hazards,
                lastRemoteRefreshSuccessUtc);
    }

    /// <summary>
    /// Returns a synchronous assessment from the latest verified hazard
    /// snapshot and opportunistically schedules a non-blocking remote refresh.
    /// This keeps the 2-second GPS/PDR loop responsive even when Supabase is
    /// slow or unavailable.
    /// </summary>
    public HazardRouteAssessment AssessRoute(
        RouteResult route,
        double progressMeters,
        bool internetAvailable)
    {
        ArgumentNullException.ThrowIfNull(
            route);

        ScheduleRemoteRefreshIfDue(
            internetAvailable);

        RouteHazard[] hazards;
        DateTimeOffset? lastSuccess;

        lock (sync)
        {
            hazards =
                MergeHazardsLocked();

            lastSuccess =
                lastRemoteRefreshSuccessUtc;
        }

        RouteHazardGeometry.RouteHazardIntersection intersection =
            RouteHazardGeometry.FindFirstIntersection(
                route,
                progressMeters,
                hazards);

        if (!intersection.IsAffected ||
            intersection.Hazard is null)
        {
            lock (sync)
            {
                lastLoggedHazardId =
                    null;

                lastLoggedDistanceBucket =
                    -1;
            }

            return HazardRouteAssessment.Safe(
                hazards,
                lastSuccess);
        }

        RouteHazard hazard =
            intersection.Hazard;

        int distanceBucket =
            (int)Math.Floor(
                Math.Max(0.0, intersection.DistanceAheadMeters) /
                10.0);

        bool shouldLog;

        lock (sync)
        {
            shouldLog =
                !string.Equals(
                    lastLoggedHazardId,
                    hazard.Id,
                    StringComparison.Ordinal) ||
                lastLoggedDistanceBucket !=
                    distanceBucket;

            if (shouldLog)
            {
                lastLoggedHazardId =
                    hazard.Id;

                lastLoggedDistanceBucket =
                    distanceBucket;
            }
        }

        if (shouldLog)
        {
            WriteWarning(
                "ACTIVE ROUTE HAZARD DETECTED: " +
                $"id='{DiagnosticPrivacyPolicy.FormatRouteLabel(hazard.Id)}', " +
                $"category='{hazard.Category}', " +
                $"severity='{hazard.Severity}', " +
                $"title='{DiagnosticPrivacyPolicy.FormatRouteLabel(hazard.Title)}', " +
                $"distanceAhead={intersection.DistanceAheadMeters:F1} m, " +
                $"routeClearance={intersection.MinimumDistanceMeters:F1} m, " +
                $"exclusionRadius={hazard.RadiusMeters:F1} m, " +
                $"activeHazards={hazards.Length}.");
        }

        return new HazardRouteAssessment(
            true,
            hazard,
            intersection.DistanceAheadMeters,
            intersection.MinimumDistanceMeters,
            hazards,
            lastSuccess);
    }

    /// <summary>
    /// Reserves one reroute attempt for a hazard so repeated GPS polls cannot
    /// trigger overlapping or rapid retry loops when no alternative exists.
    /// </summary>
    public bool TryReserveReroute(
        RouteHazard hazard,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(
            hazard);

        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        lock (sync)
        {
            if (lastRerouteAttemptUtcByHazard.TryGetValue(
                    hazard.Id,
                    out DateTimeOffset lastAttempt))
            {
                TimeSpan elapsed =
                    now -
                    lastAttempt;

                if (elapsed <
                    PerHazardRerouteCooldown)
                {
                    double remaining =
                        Math.Max(
                            0.0,
                            (PerHazardRerouteCooldown - elapsed)
                                .TotalSeconds);

                    reason =
                        $"hazard reroute cooldown active for another {remaining:F0} s";

                    return false;
                }
            }

            lastRerouteAttemptUtcByHazard[hazard.Id] =
                now;
        }

        reason =
            "hazard reroute reserved";

        return true;
    }

#if RESCUAR_DIAGNOSTICS
    /// <summary>
    /// Adds/replaces a controlled test hazard without touching production
    /// report data. Used only by the Stage 10 developer field-test harness.
    /// </summary>
    public void SetDeveloperHazard(
        RouteHazard? hazard)
    {
        lock (sync)
        {
            developerHazards =
                hazard is null
                    ? Array.Empty<RouteHazard>()
                    : new[] { hazard };

            lastLoggedHazardId =
                null;

            lastLoggedDistanceBucket =
                -1;

            if (hazard is not null)
            {
                lastRerouteAttemptUtcByHazard.Remove(
                    hazard.Id);
            }
        }

        WriteWarning(
            hazard is null
                ? "Developer route hazard cleared."
                : "DEVELOPER ROUTE HAZARD ARMED: " +
                  $"id='{DiagnosticPrivacyPolicy.FormatRouteLabel(hazard.Id)}', " +
                  $"coordinate={DiagnosticPrivacyPolicy.FormatCoordinate(hazard.Coordinate.Latitude, hazard.Coordinate.Longitude)}, " +
                  $"radius={hazard.RadiusMeters:F1} m.");
    }
#endif

    public void ResetSessionState()
    {
        lock (sync)
        {
#if RESCUAR_DIAGNOSTICS
            developerHazards =
                Array.Empty<RouteHazard>();
#endif

            lastRerouteAttemptUtcByHazard.Clear();

            lastLoggedHazardId =
                null;

            lastLoggedDistanceBucket =
                -1;
        }
    }

    public void ScheduleRemoteRefreshIfDue(
        bool internetAvailable)
    {
        if (!internetAvailable)
        {
            return;
        }

        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        lock (sync)
        {
            if (now -
                    lastRemoteRefreshAttemptUtc <
                RemoteRefreshInterval)
            {
                return;
            }

            lastRemoteRefreshAttemptUtc =
                now;
        }

        if (Interlocked.CompareExchange(
                ref remoteRefreshInProgress,
                1,
                0) != 0)
        {
            return;
        }

        _ =
            RefreshRemoteHazardsAsync();
    }

    private async Task RefreshRemoteHazardsAsync()
    {
        try
        {
            List<CommunityReport>? reports =
                await communityReportService.TryGetRemoteReportsAsync();

            if (reports is null)
            {
                WriteDebug(
                    "Verified hazard refresh unavailable. Retaining the last successful hazard snapshot.");

                return;
            }

            RouteHazard[] hazards =
                reports
                    .Where(IsApprovedRoutingHazard)
                    .Select(ConvertToRouteHazard)
                    .Where(hazard => hazard is not null)
                    .Cast<RouteHazard>()
                    .GroupBy(
                        hazard =>
                            hazard.Id,
                        StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToArray();

            DateTimeOffset refreshedAtUtc =
                DateTimeOffset.UtcNow;

            lock (sync)
            {
                verifiedRemoteHazards =
                    hazards;

                lastRemoteRefreshSuccessUtc =
                    refreshedAtUtc;
            }

            SaveVerifiedHazardCache(
                hazards,
                refreshedAtUtc);

            WriteDebug(
                "Verified route-hazard snapshot refreshed: " +
                $"remoteReports={reports.Count}, " +
                $"activeRoutingHazards={hazards.Length}.");
        }
        catch (Exception exception)
        {
            WriteWarning(
                "Verified hazard refresh failed; retaining previous snapshot: " +
                DiagnosticPrivacyPolicy.FormatException(exception));
        }
        finally
        {
            Volatile.Write(
                ref remoteRefreshInProgress,
                0);
        }
    }

    private void LoadVerifiedHazardCache()
    {
        try
        {
            string json =
                Microsoft.Maui.Storage.Preferences.Default.Get(
                    HazardCachePreferenceKey,
                    string.Empty);

            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            CachedHazardSnapshot? snapshot =
                JsonSerializer.Deserialize<CachedHazardSnapshot>(
                    json);

            if (snapshot is null ||
                snapshot.Hazards is null ||
                DateTimeOffset.UtcNow -
                    snapshot.RefreshedAtUtc >
                    MaximumOfflineCacheAge)
            {
                Microsoft.Maui.Storage.Preferences.Default.Remove(
                    HazardCachePreferenceKey);

                return;
            }

            RouteHazard[] hazards =
                snapshot.Hazards
                    .Where(row =>
                        row is not null &&
                        new GeoCoordinate(row.Latitude, row.Longitude).IsValid &&
                        double.IsFinite(row.RadiusMeters) &&
                        row.RadiusMeters > 0.0)
                    .Select(row =>
                        new RouteHazard(
                            row.Id,
                            new GeoCoordinate(row.Latitude, row.Longitude),
                            row.RadiusMeters,
                            row.Category,
                            row.Severity,
                            row.Title,
                            row.Source,
                            row.ObservedAtUtc))
                    .ToArray();

            lock (sync)
            {
                verifiedRemoteHazards =
                    hazards;

                lastRemoteRefreshSuccessUtc =
                    snapshot.RefreshedAtUtc;
            }

            WriteDebug(
                "Loaded last verified route-hazard snapshot for offline continuity: " +
                $"hazards={hazards.Length}, " +
                $"snapshotAge={(DateTimeOffset.UtcNow - snapshot.RefreshedAtUtc).TotalMinutes:F1} min.");
        }
        catch (Exception exception)
        {
            WriteWarning(
                "Unable to load cached verified hazards; continuing without " +
                "offline hazard cache: " +
                DiagnosticPrivacyPolicy.FormatException(exception));
        }
    }

    private static void SaveVerifiedHazardCache(
        IReadOnlyList<RouteHazard> hazards,
        DateTimeOffset refreshedAtUtc)
    {
        try
        {
            CachedHazardSnapshot snapshot =
                new()
                {
                    RefreshedAtUtc =
                        refreshedAtUtc,
                    Hazards =
                        hazards
                            .Select(hazard =>
                                new CachedHazardRow
                                {
                                    Id = hazard.Id,
                                    Latitude = hazard.Coordinate.Latitude,
                                    Longitude = hazard.Coordinate.Longitude,
                                    RadiusMeters = hazard.RadiusMeters,
                                    Category = hazard.Category,
                                    Severity = hazard.Severity,
                                    Title = hazard.Title,
                                    Source = hazard.Source,
                                    ObservedAtUtc = hazard.ObservedAtUtc
                                })
                            .ToArray()
                };

            Microsoft.Maui.Storage.Preferences.Default.Set(
                HazardCachePreferenceKey,
                JsonSerializer.Serialize(snapshot));
        }
        catch (Exception exception)
        {
            WriteWarning(
                "Unable to persist verified hazard snapshot: " +
                DiagnosticPrivacyPolicy.FormatException(exception));
        }
    }

    private sealed class CachedHazardSnapshot
    {
        public DateTimeOffset RefreshedAtUtc { get; set; }

        public CachedHazardRow[] Hazards { get; set; } =
            Array.Empty<CachedHazardRow>();
    }

    private sealed class CachedHazardRow
    {
        public string Id { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double RadiusMeters { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public DateTimeOffset ObservedAtUtc { get; set; }
    }

    private RouteHazard[] MergeHazardsLocked()
    {
#if RESCUAR_DIAGNOSTICS
        if (developerHazards.Length == 0)
        {
            return verifiedRemoteHazards.ToArray();
        }

        return verifiedRemoteHazards
            .Concat(developerHazards)
            .GroupBy(
                hazard =>
                    hazard.Id,
                StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToArray();
#else
        return verifiedRemoteHazards.ToArray();
#endif
    }

    private static bool IsApprovedRoutingHazard(
        CommunityReport report)
    {
        if (report is null ||
            !string.Equals(
                report.Status,
                "Approved",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(
                report.Category,
                "Flood Warning",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                report.Category,
                "Road Hazard",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        GeoCoordinate coordinate =
            new(
                report.Latitude,
                report.Longitude);

        return coordinate.IsValid &&
               !(Math.Abs(coordinate.Latitude) < 0.000001 &&
                 Math.Abs(coordinate.Longitude) < 0.000001);
    }

    private static RouteHazard? ConvertToRouteHazard(
        CommunityReport report)
    {
        GeoCoordinate coordinate =
            new(
                report.Latitude,
                report.Longitude);

        if (!coordinate.IsValid)
        {
            return null;
        }

        double radiusMeters =
            GetExclusionRadiusMeters(
                report.Category,
                report.Severity);

        DateTime createdAt =
            report.CreatedAt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(
                    report.CreatedAt,
                    DateTimeKind.Utc)
                : report.CreatedAt.ToUniversalTime();

        return new RouteHazard(
            report.Id,
            coordinate,
            radiusMeters,
            report.Category,
            report.Severity,
            report.Title,
            "Approved Community Report",
            new DateTimeOffset(createdAt));
    }

    private static double GetExclusionRadiusMeters(
        string? category,
        string? severity)
    {
        bool flood =
            string.Equals(
                category,
                "Flood Warning",
                StringComparison.OrdinalIgnoreCase);

        string normalizedSeverity =
            severity?.Trim() ?? string.Empty;

        if (flood)
        {
            if (normalizedSeverity.Equals("High", StringComparison.OrdinalIgnoreCase) ||
                normalizedSeverity.Equals("Severe", StringComparison.OrdinalIgnoreCase) ||
                normalizedSeverity.Equals("Critical", StringComparison.OrdinalIgnoreCase))
            {
                return 60.0;
            }

            if (normalizedSeverity.Equals("Low", StringComparison.OrdinalIgnoreCase))
            {
                return 25.0;
            }

            return 40.0;
        }

        if (normalizedSeverity.Equals("High", StringComparison.OrdinalIgnoreCase) ||
            normalizedSeverity.Equals("Severe", StringComparison.OrdinalIgnoreCase) ||
            normalizedSeverity.Equals("Critical", StringComparison.OrdinalIgnoreCase))
        {
            return 40.0;
        }

        if (normalizedSeverity.Equals("Low", StringComparison.OrdinalIgnoreCase))
        {
            return 15.0;
        }

        return 25.0;
    }

    private static void WriteDebug(
        string message)
    {
#if ANDROID
        Log.Debug(
            LogTag,
            message);
#else
        System.Diagnostics.Debug.WriteLine(
            $"{LogTag}: {message}");
#endif
    }

    private static void WriteWarning(
        string message)
    {
#if ANDROID
        Log.Warn(
            LogTag,
            message);
#else
        System.Diagnostics.Debug.WriteLine(
            $"{LogTag}: {message}");
#endif
    }
}
