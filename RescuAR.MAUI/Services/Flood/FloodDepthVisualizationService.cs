using RescuAR.App.Models;

namespace RescuAR.App.Services.Flood;

/// <summary>
/// Converts available flood information into a presentation-safe camera state.
///
/// IMPORTANT:
/// DisasterAdvisory.WaterLevel is currently used by the existing RescuAR UI as
/// a Marikina River / monitoring-station level (for example 16.5 m). It is not
/// assumed to be the depth of water at the phone's exact position.
///
/// A true local-depth visualization therefore uses LocalDepth mode only when a
/// trusted source explicitly supplies local flood depth. Simulation mode uses
/// a user-selected water height without claiming a measured flood level.
/// </summary>
public sealed class FloodDepthVisualizationService
{
    public enum FloodVisualizationMode
    {
        Unavailable = 0,
        FloodAdvisory = 1,
        RiverGauge = 2,
        LocalDepth = 3,
        Simulation = 4
    }

    public readonly record struct FloodVisualizationSnapshot(
        bool IsAvailable,
        FloodVisualizationMode Mode,
        string Title,
        string PrimaryText,
        string SecondaryText,
        string SourceText,
        double? LocalDepthMeters,
        double? ReportedRiverLevelMeters,
        string AdvisoryId)
    {
        public bool HasRenderableHeight =>
            IsAvailable &&
            (Mode == FloodVisualizationMode.LocalDepth || Mode == FloodVisualizationMode.Simulation) &&
            LocalDepthMeters is double height && double.IsFinite(height) && height > 0;

        public static FloodVisualizationSnapshot Unavailable =>
            new(
                false,
                FloodVisualizationMode.Unavailable,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                null,
                null,
                string.Empty);
    }

    public FloodVisualizationSnapshot FromAdvisory(
        DisasterAdvisory? advisory)
    {
        if (advisory is null ||
            !IsFloodAdvisory(advisory))
        {
            return FloodVisualizationSnapshot.Unavailable;
        }

        string area =
            string.IsNullOrWhiteSpace(advisory.DisplayAffectedArea)
                ? "Marikina City"
                : advisory.DisplayAffectedArea;

        string source =
            $"Verified advisory • {area}";

        if (double.IsFinite(advisory.WaterLevel) &&
            advisory.WaterLevel > 0.0)
        {
            return new FloodVisualizationSnapshot(
                true,
                FloodVisualizationMode.RiverGauge,
                "Flood conditions nearby",
                $"Reported river level: {advisory.WaterLevel:F1} m",
                "River gauge reading — not local street depth.",
                source,
                null,
                advisory.WaterLevel,
                advisory.Id ?? string.Empty);
        }

        return new FloodVisualizationSnapshot(
            true,
            FloodVisualizationMode.FloodAdvisory,
            "Flood conditions nearby",
            "Flood advisory active in your area",
            "Use AR guidance and avoid visibly flooded routes.",
            source,
            null,
            null,
            advisory.Id ?? string.Empty);
    }

    /// <summary>
    /// Future production entry point for a trusted, explicitly local flood
    /// depth source. This is intentionally separate from river gauge level.
    /// </summary>
    public FloodVisualizationSnapshot FromLocalDepth(
        double localDepthMeters,
        string sourceText,
        string areaText = "Current area")
    {
        if (!double.IsFinite(localDepthMeters) ||
            localDepthMeters < 0.0)
        {
            return FloodVisualizationSnapshot.Unavailable;
        }

        double clamped =
            Math.Clamp(localDepthMeters, 0.0, 3.0);

        return new FloodVisualizationSnapshot(
            true,
            FloodVisualizationMode.LocalDepth,
            "Flood depth visualization",
            $"Estimated local depth: {clamped:F2} m",
            GetLocalDepthSafetyText(clamped),
            string.IsNullOrWhiteSpace(sourceText)
                ? areaText
                : $"{sourceText} • {areaText}",
            clamped,
            null,
            string.Empty);
    }

    public FloodVisualizationSnapshot FromSimulation(double heightMeters)
    {
        var snapshot = FromLocalDepth(heightMeters, "User-selected simulation");
        if (!snapshot.IsAvailable) return snapshot;
        return snapshot with
        {
            Mode = FloodVisualizationMode.Simulation,
            Title = "Flood simulation",
            PrimaryText = $"Simulated water height: {snapshot.LocalDepthMeters:0.0#} m",
            SecondaryText = "Preview only — not a measured flood level.",
            SourceText = "User-selected height • ARCore floor reference"
        };
    }

    public static bool IsFloodAdvisory(
        DisasterAdvisory advisory)
    {
        string category =
            advisory.Category ?? string.Empty;

        string title =
            advisory.Title ?? string.Empty;

        return
            category.Contains(
                "flood",
                StringComparison.OrdinalIgnoreCase) ||
            category.Contains(
                "water",
                StringComparison.OrdinalIgnoreCase) ||
            title.Contains(
                "flood",
                StringComparison.OrdinalIgnoreCase) ||
            title.Contains(
                "river",
                StringComparison.OrdinalIgnoreCase) ||
            title.Contains(
                "water level",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLocalDepthSafetyText(
        double depthMeters)
    {
        if (depthMeters >= 1.0)
        {
            return "Dangerous flood depth. Do not enter the water; follow evacuation guidance.";
        }

        if (depthMeters >= 0.5)
        {
            return "Deep flooding indicated. Avoid this area and use the safer route.";
        }

        if (depthMeters >= 0.2)
        {
            return "Floodwater indicated ahead. Use caution and avoid unnecessary exposure.";
        }

        return "Shallow floodwater indicated. Conditions can change quickly.";
    }
}
