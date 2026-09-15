using System;
using System.Collections.Generic;

namespace RescuAR.Navigation.Guidance;

/// <summary>
/// Facility-specific safe-zone coverage for evacuation destinations.
///
/// RescuAR routes to one representative coordinate for each evacuation
/// facility, but arrival should not require the user to stand on that exact
/// point. Schools, covered courts, sports complexes, and government compounds
/// occupy an area, so each known facility receives a conservative circular
/// geofence large enough to cover the usable evacuation vicinity plus normal
/// pedestrian GPS uncertainty.
///
/// This is intentionally data-driven. If a future evacuation-center dataset
/// provides an authoritative polygon/footprint, that polygon should supersede
/// these circular approximations without changing SafeZoneConfirmationService.
/// </summary>
public static class SafeZoneFacilityCatalog
{
    /// <summary>
    /// Legacy/default radius for destinations that are not yet explicitly
    /// profiled. This preserves the previous Stage 5 behavior.
    /// </summary>
    public const double DefaultSafeZoneRadiusMeters =
        SafeZoneConfirmationService.ArrivalRadiusMeters;

    /*
     * Current evacuation destinations exposed by the application.
     *
     * The values represent the facility vicinity rather than a single doorway.
     * They intentionally include a modest margin for GPS uncertainty and for
     * evacuation use of yards/courts immediately surrounding the primary
     * building.
     */
    private static readonly Dictionary<string, double> radiusByName =
        new(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Malanday Elementary School"] = 70.0,
            ["San Roque High School"] = 65.0,
            ["San Roque National High School"] = 65.0,
            ["Concepcion Uno Covered Court"] = 45.0,
            ["Concepcion Subdivision Covered Court"] = 45.0,
            ["Concepcion Integrated School"] = 65.0,
            ["Marikina Elementary School"] = 60.0,

            // Additional evacuation-center models already present elsewhere
            // in the MAUI project.
            ["Marikina Sports Center"] = 90.0,
            ["Marikina City Hall"] = 65.0,
            ["Marikina City Hall Gym"] = 65.0,
            ["Riverbanks Center"] = 100.0
        };

    public static double GetArrivalRadiusMeters(
        string? facilityName)
    {
        string cleanName =
            facilityName?.Trim() ??
            string.Empty;

        if (cleanName.Length > 0 &&
            radiusByName.TryGetValue(
                cleanName,
                out double radiusMeters))
        {
            return radiusMeters;
        }

        return DefaultSafeZoneRadiusMeters;
    }
}
