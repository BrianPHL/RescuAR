using System;
using Microsoft.Maui.Controls;
using Mapsui;
using Mapsui.UI.Maui;
using RescuAR.App.ViewModels.Prepare;

namespace RescuAR.App.Views.Prepare
{
    public partial class EvacuationCenterInfoPage : ContentPage
    {
        public EvacuationCenterInfoPage()
        {
            InitializeComponent();

            if (BindingContext is EvacuationCenterInfoViewModel vm)
            {
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(EvacuationCenterInfoViewModel.SelectedCenter))
                    {
                        if (vm.SelectedCenter != null)
                        {
                            // Initialize the mapsui MapControl in code-behind
                            InitializePopupMap(vm.SelectedCenter);
                        }
                    }
                };
            }
        }

        private void InitializePopupMap(EvacuationCenterItem center)
        {
            try
            {
                var map = new Mapsui.Map
                {
                    CRS = "EPSG:3857"
                };

                // Add OpenStreetMap (OSM) Base Layer
                var osmLayer = Mapsui.Tiling.OpenStreetMap.CreateTileLayer();
                map.Layers.Add(osmLayer);

                // Project shelter center coordinates (SphericalMercator)
                var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(center.Longitude, center.Latitude);
                var markerPoint = new MPoint(x, y);

                // Add pin marker feature for the evacuation shelter
                var feature = new Mapsui.Layers.PointFeature(markerPoint);
                feature["Name"] = center.Name;

                var memoryLayer = new Mapsui.Layers.MemoryLayer
                {
                    Name = "ShelterLocationPin",
                    Features = new[] { feature },
                    Style = new Mapsui.Styles.SymbolStyle
                    {
                        Fill = new Mapsui.Styles.Brush(Mapsui.Styles.Color.FromString("#007E8A")),
                        SymbolScale = 0.8,
                        Outline = new Mapsui.Styles.Pen(Mapsui.Styles.Color.White, 2)
                    }
                };
                map.Layers.Add(memoryLayer);

                // Center map and set appropriate zoom level
                map.Navigator.CenterOn(markerPoint);
                map.Navigator.ZoomTo(4.0); // Medium close zoom

                PopupMapControl.Map = map;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load OSM popup map: {ex.Message}");
            }
        }
    }
}
