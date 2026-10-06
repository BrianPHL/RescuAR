using RescuAR.Navigation.Data;
using RescuAR.Navigation.Models;

namespace RescuAR.MAUI.Services.Navigation;

/// <summary>Offline context from the same embedded data used by dev's router.</summary>
public static class CameraOfflineMapDataService
{
    private static readonly Lazy<Task<IReadOnlyList<IReadOnlyList<GeoCoordinate>>>> roads = new(LoadCoreAsync);

    public static Task<IReadOnlyList<IReadOnlyList<GeoCoordinate>>> LoadAsync(CancellationToken token = default) =>
        roads.Value.WaitAsync(token);

    private static async Task<IReadOnlyList<IReadOnlyList<GeoCoordinate>>> LoadCoreAsync()
    {
        var features = await NavigationDataBootstrap.GetRoadFeaturesAsync().ConfigureAwait(false);
        // Invalid lines are excluded as a whole: never connect surviving vertices
        // across a missing/invalid location. Context lines are not routing promises.
        return features.Where(feature => feature.Coordinates.Count >= 2 &&
                feature.Coordinates.All(coordinate => coordinate.IsValid))
            .Select(feature => feature.Coordinates).ToArray();
    }
}
