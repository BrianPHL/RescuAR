using RescuAR.Diagnostics;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using System;

namespace RescuAR.Navigation.State;

/// <summary>
/// Cross-module handoff for the verified evacuation destination selected by
/// the MAUI application.
///
/// Besides the route target coordinate, the destination snapshot now carries
/// the facility-specific safe-zone radius. This lets arrival confirmation
/// treat an evacuation center as an area rather than requiring the user to
/// stand on one exact map pin.
/// </summary>
public static class NavigationDestinationBridge
{
    private const string LogTag =
        "RescuAR-MLD";

    private static readonly object sync =
        new();

    private static DestinationSnapshot current =
        DestinationSnapshot.Unavailable;

    public static event EventHandler? DestinationChanged;

    public static DestinationSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public static bool Set(
        EvacuationCenter center)
    {
        ArgumentNullException.ThrowIfNull(
            center);

        if (center.Coordinate is not GeoCoordinate coordinate ||
            !coordinate.IsValid)
        {
            AndroidLog.Warn(
                LogTag,
                $"Destination rejected: '{DiagnosticPrivacyPolicy.FormatRouteLabel(center.Name)}' has no verified coordinate.");

            return false;
        }

        Set(
            center.Name,
            coordinate);

        return true;
    }

    public static void Set(
        string name,
        GeoCoordinate coordinate)
    {
        double safeZoneRadiusMeters =
            SafeZoneFacilityCatalog.GetArrivalRadiusMeters(
                name);

        Set(
            name,
            coordinate,
            safeZoneRadiusMeters);
    }

    public static void Set(
        string name,
        GeoCoordinate coordinate,
        double safeZoneRadiusMeters)
    {
        if (!coordinate.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coordinate));
        }

        double effectiveSafeZoneRadiusMeters =
            double.IsFinite(
                safeZoneRadiusMeters) &&
            safeZoneRadiusMeters >
                0.0
                    ? Math.Clamp(
                        safeZoneRadiusMeters,
                        SafeZoneConfirmationService.MinimumSupportedArrivalRadiusMeters,
                        SafeZoneConfirmationService.MaximumSupportedArrivalRadiusMeters)
                    : SafeZoneFacilityCatalog.GetArrivalRadiusMeters(
                        name);

        DestinationSnapshot next =
            new(
                true,
                name ??
                    string.Empty,
                coordinate,
                effectiveSafeZoneRadiusMeters);

        lock (sync)
        {
            current =
                next;
        }

        AndroidLog.Debug(
            LogTag,
            "Navigation destination published: " +
            $"name='{DiagnosticPrivacyPolicy.FormatRouteLabel(next.Name)}', " +
            $"coordinate={DiagnosticPrivacyPolicy.FormatCoordinate(next.Coordinate.Latitude, next.Coordinate.Longitude)}, " +
            $"safeZoneRadius={next.SafeZoneRadiusMeters:F1} m");

        DestinationChanged?.Invoke(
            null,
            EventArgs.Empty);
    }

    public static void Clear()
    {
        lock (sync)
        {
            current =
                DestinationSnapshot.Unavailable;
        }

        AndroidLog.Debug(
            LogTag,
            "Navigation destination cleared.");

        DestinationChanged?.Invoke(
            null,
            EventArgs.Empty);
    }

    public readonly record struct DestinationSnapshot(
        bool IsAvailable,
        string Name,
        GeoCoordinate Coordinate,
        double SafeZoneRadiusMeters)
    {
        public static DestinationSnapshot Unavailable =>
            new(
                false,
                string.Empty,
                default,
                SafeZoneConfirmationService.ArrivalRadiusMeters);
    }
}
