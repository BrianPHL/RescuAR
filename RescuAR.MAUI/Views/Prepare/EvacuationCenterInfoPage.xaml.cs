using System;
using Microsoft.Maui.Controls;
using Mapsui;
using Mapsui.UI.Maui;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using RescuAR.App.ViewModels.Prepare;
using RescuAR.Navigation.Models;

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
                    if (e.PropertyName == nameof(EvacuationCenterInfoViewModel.SelectedCenter) ||
                        e.PropertyName == nameof(EvacuationCenterInfoViewModel.LocationStatusDescription))
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
                double shelterLat = center.Latitude;
                double shelterLng = center.Longitude;
                if (!new GeoCoordinate(shelterLat, shelterLng).IsValid ||
                    (shelterLat == 0 && shelterLng == 0)) return map;
                var (shelterX, shelterY) = Mapsui.Projections.SphericalMercator.FromLonLat(shelterLng, shelterLat);
                var shelterPoint = new MPoint(shelterX, shelterY);

                MPoint? userPoint = null;
                if (ViewModel.HasUsableUserLocation)
                {
                    var (userX, userY) = Mapsui.Projections.SphericalMercator.FromLonLat(
                        ViewModel.UserLongitude, ViewModel.UserLatitude);
                    userPoint = new MPoint(userX, userY);
                    var userFeature = new Mapsui.Layers.PointFeature(userPoint);
                    userFeature["Name"] = ViewModel.HasCurrentUserLocation ? "Your Location" : "Last known location";

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
                }

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
                CenterAndZoomToPins(map, userPoint ?? shelterPoint, shelterPoint);
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
                double shelterLat = vm.SelectedCenter.Latitude;
                double shelterLng = vm.SelectedCenter.Longitude;
                if (!new GeoCoordinate(shelterLat, shelterLng).IsValid ||
                    (shelterLat == 0 && shelterLng == 0)) return;
                var (sX, sY) = Mapsui.Projections.SphericalMercator.FromLonLat(shelterLng, shelterLat);

                double uLat = vm.HasUsableUserLocation ? vm.UserLatitude : shelterLat;
                double uLng = vm.HasUsableUserLocation ? vm.UserLongitude : shelterLng;
                var (uX, uY) = Mapsui.Projections.SphericalMercator.FromLonLat(uLng, uLat);

                CenterAndZoomToPins(EnlargedMapControl.Map, new MPoint(uX, uY), new MPoint(sX, sY));
            }
        }
    }
}
