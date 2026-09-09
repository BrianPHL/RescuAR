namespace RescuAR.Navigation.Models;

/// <summary>
/// Clean application representation of an evacuation center.
///
/// Coordinates are nullable because several centers in the source screenshots
/// currently have no verified coordinates. Unverified locations should remain
/// unavailable for routing rather than receiving guessed coordinates.
/// </summary>
public sealed record EvacuationCenter(
    string Name,
    EvacuationCenterCategory Category,
    GeoCoordinate? Coordinate);

public enum EvacuationCenterCategory
{
    FloodSafeMajor,
    FloodSafeMinor,
    DualPurposeMajor,
    DualPurposeMinor,
    EarthquakeSafeMinor
}
