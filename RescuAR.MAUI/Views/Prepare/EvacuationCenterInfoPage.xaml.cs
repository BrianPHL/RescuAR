using System;
using Microsoft.Maui.Controls;
using Mapsui;
using Mapsui.UI.Maui;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using RescuAR.App.ViewModels.Prepare;

namespace RescuAR.App.Views.Prepare
{
    public partial class EvacuationCenterInfoPage : ContentPage
    {
        public EvacuationCenterInfoViewModel ViewModel { get; }

        public EvacuationCenterInfoPage()
        {
            ViewModel = new EvacuationCenterInfoViewModel();
            InitializeComponent();
            BindingContext = ViewModel;

            if (BindingContext is EvacuationCenterInfoViewModel vm)
            {
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(EvacuationCenterInfoViewModel.SelectedCenter))
                    {
                        if (vm.SelectedCenter != null)
                        {
                            // Initialize both preview and enlarged maps with user and evacuation center pins
                            PopupMapControl.Map = CreateRouteMap(vm.SelectedCenter);
                            EnlargedMapControl.Map = CreateRouteMap(vm.SelectedCenter);
                        }
                    }
                    else if (e.PropertyName == nameof(EvacuationCenterInfoViewModel.IsEnlargedMapVisible))
                    {
                        if (vm.IsEnlargedMapVisible && vm.SelectedCenter != null)
                        {
                            EnlargedMapControl.Map = CreateRouteMap(vm.SelectedCenter);
                        }
                    }
                };
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            ViewModel.OnAppearing();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            ViewModel.OnDisappearing();
        }

        private Mapsui.Map CreateRouteMap(EvacuationCenterItem center)
        {
            var map = new Mapsui.Map
            {
                CRS = "EPSG:3857"
            };

            try
            {
                // 1. Add OpenStreetMap (OSM) Base Layer
                var osmLayer = Mapsui.Tiling.OpenStreetMap.CreateTileLayer("RescuAR-Mobile/1.0 (Marikina Emergency System)");
                map.Layers.Add(osmLayer);

                // 2. Project Evacuation Shelter Coordinates
                double shelterLat = center.Latitude != 0 ? center.Latitude : 14.6502;
                double shelterLng = center.Longitude != 0 ? center.Longitude : 121.0944;
                var (shelterX, shelterY) = Mapsui.Projections.SphericalMercator.FromLonLat(shelterLng, shelterLat);
                var shelterPoint = new MPoint(shelterX, shelterY);

                // 3. Project User Coordinates (fallback to Marikina center if GPS unavailable)
                double userLat = ViewModel.UserLatitude != 0 ? ViewModel.UserLatitude : 14.6612;
                double userLng = ViewModel.UserLongitude != 0 ? ViewModel.UserLongitude : 121.0963;
                var (userX, userY) = Mapsui.Projections.SphericalMercator.FromLonLat(userLng, userLat);
                var userPoint = new MPoint(userX, userY);

                // 4. User Location Point (Vibrant Blue Marker)
                var userFeature = new Mapsui.Layers.PointFeature(userPoint);
                userFeature["Name"] = "Your Location";

                var userLayer = new Mapsui.Layers.MemoryLayer
                {
                    Name = "UserLocationPin",
                    Features = new[] { userFeature },
                    Style = new SymbolStyle
                    {
                        Fill = new Mapsui.Styles.Brush(Mapsui.Styles.Color.FromString("#0284C7")),
                        Outline = new Pen(Mapsui.Styles.Color.White, 3),
                        SymbolScale = 0.85
                    }
                };
                map.Layers.Add(userLayer);

                // 5. Evacuation Center Point (RescuAR Teal Marker)
                var shelterFeature = new Mapsui.Layers.PointFeature(shelterPoint);
                shelterFeature["Name"] = center.Name;

                var shelterLayer = new Mapsui.Layers.MemoryLayer
                {
                    Name = "ShelterLocationPin",
                    Features = new[] { shelterFeature },
                    Style = new SymbolStyle
                    {
                        Fill = new Mapsui.Styles.Brush(Mapsui.Styles.Color.FromString("#0A8491")),
                        Outline = new Pen(Mapsui.Styles.Color.White, 3),
                        SymbolScale = 1.0
                    }
                };
                map.Layers.Add(shelterLayer);

                // 6. Center and Zoom to frame both location pin points
                CenterAndZoomToPins(map, userPoint, shelterPoint);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to build evacuation map: {ex.Message}");
            }

            return map;
        }

        private void CenterAndZoomToPins(Mapsui.Map map, MPoint userPoint, MPoint shelterPoint)
        {
            try
            {
                double midX = (userPoint.X + shelterPoint.X) / 2.0;
                double midY = (userPoint.Y + shelterPoint.Y) / 2.0;
                var midPoint = new MPoint(midX, midY);

                double dx = Math.Abs(userPoint.X - shelterPoint.X);
                double dy = Math.Abs(userPoint.Y - shelterPoint.Y);
                double span = Math.Max(dx, dy);

                // Ensure both user point and shelter point are cleanly framed
                double resolution = Math.Max(3.0, span / 180.0);

                map.Navigator.CenterOn(midPoint);
                map.Navigator.ZoomTo(resolution);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CenterAndZoomToPins error: {ex.Message}");
            }
        }

        private void OnZoomInEnlargedMapTapped(object sender, TappedEventArgs e)
        {
            EnlargedMapControl.Map?.Navigator.ZoomIn();
        }

        private void OnZoomOutEnlargedMapTapped(object sender, TappedEventArgs e)
        {
            EnlargedMapControl.Map?.Navigator.ZoomOut();
        }

        private void OnRecenterEnlargedMapTapped(object sender, TappedEventArgs e)
        {
            if (BindingContext is EvacuationCenterInfoViewModel vm && vm.SelectedCenter != null && EnlargedMapControl.Map != null)
            {
                double shelterLat = vm.SelectedCenter.Latitude != 0 ? vm.SelectedCenter.Latitude : 14.6502;
                double shelterLng = vm.SelectedCenter.Longitude != 0 ? vm.SelectedCenter.Longitude : 121.0944;
                var (sX, sY) = Mapsui.Projections.SphericalMercator.FromLonLat(shelterLng, shelterLat);

                double uLat = vm.UserLatitude != 0 ? vm.UserLatitude : 14.6612;
                double uLng = vm.UserLongitude != 0 ? vm.UserLongitude : 121.0963;
                var (uX, uY) = Mapsui.Projections.SphericalMercator.FromLonLat(uLng, uLat);

                CenterAndZoomToPins(EnlargedMapControl.Map, new MPoint(uX, uY), new MPoint(sX, sY));
            }
        }
    }
}
