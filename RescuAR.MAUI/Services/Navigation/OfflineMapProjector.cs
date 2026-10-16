using Mapsui.Projections;
using NetTopologySuite.Geometries;

namespace RescuAR.MAUI.Services.Navigation;

public sealed class OfflineMapProjector : ICoordinateFilter
{
    public void Filter(Coordinate coordinate)
    {
        var projected = SphericalMercator.FromLonLat(coordinate.X, coordinate.Y);
        coordinate.X = projected.x;
        coordinate.Y = projected.y;
    }
}
