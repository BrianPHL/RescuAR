using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using Mapsui;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Maui;
using NetTopologySuite.IO;
using Mapsui.Nts;
using Mapsui.Styles;
using System.Linq;
using NetTopologySuite.Geometries;

namespace RescuAR.App.ViewModels.Map;

public class SphericalMercatorProjector : NetTopologySuite.Geometries.ICoordinateFilter
{
    public void Filter(Coordinate coord)
    {
        var projected = Mapsui.Projections.SphericalMercator.FromLonLat(coord.X, coord.Y);
        coord.X = projected.x;
        coord.Y = projected.y;
    }
}

public partial class MapViewModel : ObservableObject
{
    private bool _realtimeAdvisorySubscribed;

    [ObservableProperty]
    private Mapsui.Map _map = new();

    public MapViewModel()
    {
    }

    public async Task InitializeMapAsync(MapControl mapControl)
    {
        try
        {
            var map = new Mapsui.Map
            {
                CRS = "EPSG:3857"
            };

            // Add Google Maps Base Layer
            var tileSource = new BruTile.Web.HttpTileSource(new BruTile.Predefined.GlobalSphericalMercator(0, 18), "https://mt1.google.com/vt/lyrs=m&x={x}&y={y}&z={z}", name: "Google Maps");
            var googleMapsLayer = new Mapsui.Tiling.Layers.TileLayer(tileSource) { Name = "BaseMap" };
            map.Layers.Add(googleMapsLayer);

            // We will skip loading 2D_MAP.mbtiles because it contains vector tiles (PBF)
            // Mapsui 4 TileLayer only supports raster image tiles (PNG/JPG).

            // Load GeoJSON Roads
            await LoadGeoJsonLayerAsync(map, "ROADS.geojson", "Roads", new VectorStyle { Line = new Pen(Mapsui.Styles.Color.Gray, 1.5) });

            // Load GeoJSON Points
            await LoadGeoJsonLayerAsync(map, "POINTS.geojson", "Points", new SymbolStyle { Fill = new Mapsui.Styles.Brush(Mapsui.Styles.Color.Teal), SymbolScale = 0.5 });

            // Center and Zoom to Map Data (Marikina)
            var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(121.1029, 14.6507);
            map.Navigator.CenterOn(new MPoint(x, y));
            map.Navigator.ZoomTo(9.5546); // ~Zoom Level 14

            Map = map;
            mapControl.Map = map;
        }
        catch (Exception ex)
        {
            if (Shell.Current != null)
                await Shell.Current.DisplayAlert("Map Error", $"Base Map failed: {ex.Message}", "OK");
            Console.WriteLine($"Error loading map: {ex.Message}");
        }
    }

    private async Task LoadGeoJsonLayerAsync(Mapsui.Map map, string fileName, string layerName, IStyle style)
    {
        try
        {
            string localPath = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, fileName);
            if (!File.Exists(localPath))
            {
                using var stream = await Microsoft.Maui.Storage.FileSystem.OpenAppPackageFileAsync(fileName);
                using var newStream = File.Create(localPath);
                await stream.CopyToAsync(newStream);
            }

            string geoJson = await File.ReadAllTextAsync(localPath);
            
            var features = await Task.Run(() => 
            {
                var reader = new GeoJsonReader();
                var featureCollection = reader.Read<NetTopologySuite.Features.FeatureCollection>(geoJson);
                if (featureCollection == null) return new List<GeometryFeature>();

                var projector = new SphericalMercatorProjector();
                return featureCollection.Select(f => 
                {
                    var geom = f.Geometry.Copy();
                    geom.Apply(projector);
                    return new GeometryFeature(geom);
                }).ToList();
            });

            var layer = new Mapsui.Layers.MemoryLayer
            {
                Name = layerName,
                Features = features,
                Style = style
            };

            map.Layers.Add(layer);
        }
        catch (Exception ex)
        {
            if (Shell.Current != null)
                await Shell.Current.DisplayAlert("Map Error", $"Failed to load {fileName}: {ex.Message}", "OK");
            Console.WriteLine($"Error loading {fileName}: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OpenChatAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("SafetyCirclePage");
        }
    }

    // --- Advisory Popup ---
    [ObservableProperty]
    private RescuAR.App.Models.DisasterAdvisory? _selectedAdvisory;

    [ObservableProperty]
    private bool _isPopupVisible;

    [RelayCommand]
    private void ClosePopup()
    {
        IsPopupVisible = false;
        SelectedAdvisory = null;
        RescuAR.App.Services.Reports.RealtimeAdvisoryManager.StopAlarmAudio();
    }

    [RelayCommand]
    private async Task GoToAdvisoriesFeedAsync()
    {
        ClosePopup();
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("AdvisoryFeedPage");
        }
    }

    partial void OnMapChanged(Mapsui.Map value)
    {
        if (_realtimeAdvisorySubscribed)
            return;

        _realtimeAdvisorySubscribed = true;

        RescuAR.App.Services.Reports.RealtimeAdvisoryManager.OnNewAdvisoryPushed += (newAdvisory) =>
        {
            SelectedAdvisory = newAdvisory;
            IsPopupVisible = true;
        };

        RescuAR.App.Services.Reports.RealtimeAdvisoryManager.StartRealtimeListener();
    }
}
