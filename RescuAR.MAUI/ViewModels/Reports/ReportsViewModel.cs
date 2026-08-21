using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;
using Microsoft.Maui.Controls;
using RescuAR.App.Models;
using RescuAR.App.Services.Reports;

namespace RescuAR.App.ViewModels.Reports
{
    public partial class ReportsViewModel : ObservableObject
    {
        private readonly CommunityReportService _reportService;
        private readonly IOsmGeocodingService _osmService;

        [ObservableProperty]
        private ObservableCollection<CommunityReport> reports = new();

        [ObservableProperty]
        private string searchQuery = string.Empty;

        [ObservableProperty]
        private string selectedFilter = "Newest first";

        [ObservableProperty]
        private List<string> filterOptions = new() { "Newest first", "Oldest first", "Nearest to me" };

        [ObservableProperty]
        private bool isRefreshing;

        // Modal Visibility
        [ObservableProperty]
        private bool isCreateModalVisible;

        [ObservableProperty]
        private bool isSuccessModalVisible;

        [ObservableProperty]
        private bool isMapPickerVisible;

        // Create Report Form Fields
        [ObservableProperty]
        private string newReportTitle = string.Empty;

        [ObservableProperty]
        private string newReportDescription = string.Empty;

        [ObservableProperty]
        private string newReportCategory = "Flood Warning";

        [ObservableProperty]
        private List<string> categoryOptions = new()
        {
            "Flood Warning",
            "Rescue Request",
            "Road Hazard",
            "Power Outage",
            "General Alert"
        };

        [ObservableProperty]
        private string newReportAddress = "41 C. Benitez St., MBLA Court, Malanday, Marikina City";

        [ObservableProperty]
        private double newReportLatitude = 14.6585;

        [ObservableProperty]
        private double newReportLongitude = 121.0955;

        [ObservableProperty]
        private string newReportMediaUrl = string.Empty;

        [ObservableProperty]
        private string newReportMediaType = "Image"; // Image or Video

        [ObservableProperty]
        private bool newReportHasMedia;

        [ObservableProperty]
        private bool newReportAllowComments = true;

        [ObservableProperty]
        private bool isFetchingLocation;

        // Map Picker Search Query & OSM Results
        [ObservableProperty]
        private string mapSearchQuery = string.Empty;

        [ObservableProperty]
        private bool isSearchingOsm;

        [ObservableProperty]
        private ObservableCollection<OsmSearchResult> osmSearchResults = new();

        [ObservableProperty]
        private List<string> presetLocations = new()
        {
            "41 C. Benitez St., MBLA Court, Malanday, Marikina City",
            "J.P. Rizal St. cor. Malaya St., Malanday, Marikina City",
            "Malaya Street, Barangay Malanday, Marikina City",
            "H. Bautista Elementary School, Concepcion Uno, Marikina City",
            "Marikina Sports Center, Sta. Elena, Marikina City",
            "Nangka Elementary School, Nangka, Marikina City",
            "Sto. Niño National High School, Sto. Niño, Marikina City"
        };

        public ReportsViewModel() : this(new CommunityReportService(), new OsmGeocodingService())
        {
        }

        public ReportsViewModel(CommunityReportService reportService, IOsmGeocodingService osmService)
        {
            _reportService = reportService;
            _osmService = osmService;
            _ = LoadReportsAsync();
        }

        partial void OnSearchQueryChanged(string value)
        {
            _ = LoadReportsAsync();
        }

        partial void OnSelectedFilterChanged(string value)
        {
            _ = LoadReportsAsync();
        }

        partial void OnMapSearchQueryChanged(string value)
        {
            _ = SearchOsmLocationsAsync(value);
        }

        private async Task SearchOsmLocationsAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            {
                OsmSearchResults.Clear();
                IsSearchingOsm = false;
                return;
            }

            IsSearchingOsm = true;
            try
            {
                var list = await _osmService.SearchLocationsAsync(query);
                OsmSearchResults = new ObservableCollection<OsmSearchResult>(list);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OSM Search Exception: {ex.Message}");
            }
            finally
            {
                IsSearchingOsm = false;
            }
        }

        [RelayCommand]
        public async Task LoadReportsAsync()
        {
            IsRefreshing = true;
            try
            {
                var list = await _reportService.GetReportsAsync(SearchQuery, SelectedFilter);
                Reports = new ObservableCollection<CommunityReport>(list);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading reports: {ex.Message}");
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        [RelayCommand]
        private async Task OpenCreateModalAsync()
        {
            // Reset fields
            NewReportTitle = string.Empty;
            NewReportDescription = string.Empty;
            NewReportCategory = "Flood Warning";
            NewReportMediaUrl = string.Empty;
            NewReportHasMedia = false;
            NewReportAllowComments = true;
            IsCreateModalVisible = true;

            // Automatically attempt to fetch current GPS location
            await FetchUserLocationAsync();
        }

        [RelayCommand]
        private void CloseCreateModal()
        {
            IsCreateModalVisible = false;
        }

        [RelayCommand]
        private async Task FetchUserLocationAsync()
        {
            IsFetchingLocation = true;
            try
            {
                var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)));
                if (location != null)
                {
                    NewReportLatitude = location.Latitude;
                    NewReportLongitude = location.Longitude;

                    var placemarks = await Geocoding.Default.GetPlacemarksAsync(location);
                    var placemark = placemarks?.FirstOrDefault();
                    if (placemark != null)
                    {
                        var parts = new List<string>();
                        if (!string.IsNullOrWhiteSpace(placemark.FeatureName)) parts.Add(placemark.FeatureName);
                        if (!string.IsNullOrWhiteSpace(placemark.Thoroughfare)) parts.Add(placemark.Thoroughfare);
                        if (!string.IsNullOrWhiteSpace(placemark.SubLocality)) parts.Add(placemark.SubLocality);
                        if (!string.IsNullOrWhiteSpace(placemark.Locality)) parts.Add(placemark.Locality);

                        if (parts.Count > 0)
                        {
                            NewReportAddress = string.Join(", ", parts);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Location fetch error: {ex.Message}");
                if (string.IsNullOrWhiteSpace(NewReportAddress))
                {
                    NewReportAddress = "41 C. Benitez St., MBLA Court, Malanday, Marikina City";
                }
            }
            finally
            {
                IsFetchingLocation = false;
            }
        }

        [RelayCommand]
        private async Task PickMediaAsync()
        {
            try
            {
                var action = await Shell.Current.DisplayActionSheetAsync("Upload Media", "Cancel", null, "Take Photo", "Choose Photo from Gallery", "Pick Video");

                if (action == "Take Photo")
                {
                    if (MediaPicker.Default.IsCaptureSupported)
                    {
                        var photo = await MediaPicker.Default.CapturePhotoAsync();
                        if (photo != null)
                        {
                            NewReportMediaUrl = photo.FullPath;
                            NewReportMediaType = "Image";
                            NewReportHasMedia = true;
                        }
                    }
                }
                else if (action == "Choose Photo from Gallery")
                {
                    var photo = await MediaPicker.Default.PickPhotoAsync();
                    if (photo != null)
                    {
                        NewReportMediaUrl = photo.FullPath;
                        NewReportMediaType = "Image";
                        NewReportHasMedia = true;
                    }
                }
                else if (action == "Pick Video")
                {
                    var video = await MediaPicker.Default.PickVideoAsync();
                    if (video != null)
                    {
                        NewReportMediaUrl = video.FullPath;
                        NewReportMediaType = "Video";
                        NewReportHasMedia = true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Media pick error: {ex.Message}");
            }
        }

        [RelayCommand]
        private void RemoveMedia()
        {
            NewReportMediaUrl = string.Empty;
            NewReportHasMedia = false;
        }

        [RelayCommand]
        private void OpenMapPicker()
        {
            MapSearchQuery = string.Empty;
            OsmSearchResults.Clear();
            IsMapPickerVisible = true;
        }

        [RelayCommand]
        private void CloseMapPicker()
        {
            IsMapPickerVisible = false;
        }

        [RelayCommand]
        private void SelectOsmLocation(OsmSearchResult item)
        {
            if (item != null)
            {
                NewReportAddress = item.DisplayName;
                NewReportLatitude = item.Latitude;
                NewReportLongitude = item.Longitude;
                IsMapPickerVisible = false;
            }
        }

        [RelayCommand]
        private void SelectPresetLocation(string location)
        {
            if (!string.IsNullOrWhiteSpace(location))
            {
                NewReportAddress = location;
                IsMapPickerVisible = false;
            }
        }

        [RelayCommand]
        private void ConfirmCustomMapLocation()
        {
            if (!string.IsNullOrWhiteSpace(MapSearchQuery))
            {
                NewReportAddress = MapSearchQuery.Trim();
            }
            IsMapPickerVisible = false;
        }

        [RelayCommand]
        private async Task SubmitReportAsync()
        {
            if (string.IsNullOrWhiteSpace(NewReportTitle))
            {
                await Shell.Current.DisplayAlertAsync("Required Field", "Please enter a title for your community report.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(NewReportDescription))
            {
                await Shell.Current.DisplayAlertAsync("Required Field", "Please enter a description of the incident.", "OK");
                return;
            }

            var report = new CommunityReport
            {
                Title = NewReportTitle.Trim(),
                Description = NewReportDescription.Trim(),
                Category = NewReportCategory,
                Address = string.IsNullOrWhiteSpace(NewReportAddress) ? "Marikina City" : NewReportAddress.Trim(),
                Latitude = NewReportLatitude,
                Longitude = NewReportLongitude,
                DistanceText = "50 meters away",
                PostedBy = "Aubrey T.",
                PostedAt = DateTime.Now,
                MediaUrl = NewReportMediaUrl,
                MediaType = NewReportMediaType,
                HasMedia = NewReportHasMedia,
                AllowComments = NewReportAllowComments
            };

            await _reportService.AddReportAsync(report);

            // Hide create modal and show success modal
            IsCreateModalVisible = false;
            IsSuccessModalVisible = true;

            await LoadReportsAsync();

            // Auto dismiss success modal after 2 seconds
            await Task.Delay(2000);
            IsSuccessModalVisible = false;
        }

        [RelayCommand]
        private void CloseSuccessModal()
        {
            IsSuccessModalVisible = false;
        }

        [RelayCommand]
        private async Task ViewReportDetailsAsync(CommunityReport report)
        {
            if (report == null) return;
            await Shell.Current.GoToAsync($"ReportDetails?ReportId={report.Id}");
        }

        [RelayCommand]
        private async Task ToggleLikeAsync(CommunityReport report)
        {
            if (report == null) return;
            await _reportService.ToggleLikeAsync(report.Id);
        }
    }
}
