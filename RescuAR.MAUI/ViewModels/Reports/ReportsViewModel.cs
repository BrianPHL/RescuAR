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

        private readonly HashSet<string> _seenReportIds = new();
        [ObservableProperty]
        private ObservableCollection<ReportNotification> notifications = new();

        [ObservableProperty]
        private int unreadNotificationsCount;

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

            // Fetch reports initially when VM is created
            _ = LoadReportsAsync();

            RescuAR.App.Services.Reports.RealtimeAdvisoryManager.OnNewAdvisoryPushed += (newAdvisory) =>
            {
                SelectedAdvisory = newAdvisory;
                IsPopupVisible = true;
            };
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
                
                foreach (var report in list)
                {
                    if (!_seenReportIds.Contains(report.Id))
                    {
                        var notification = new ReportNotification
                        {
                            Title = report.PostedBy,
                            Message = $"reports all about {report.Title}",
                            Timestamp = report.CreatedAt
                        };
                        Notifications.Insert(0, notification);
                        UnreadNotificationsCount++;
                    }
                }
                
                foreach (var report in list)
                {
                    _seenReportIds.Add(report.Id);
                }
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

        private FileResult? _selectedMediaFile;

        [RelayCommand]
        private async Task PickMediaAsync()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.Camera>();
                }

                if (status == PermissionStatus.Granted)
                {
                    if (MediaPicker.Default.IsCaptureSupported)
                    {
                        var photo = await MediaPicker.Default.CapturePhotoAsync();
                        if (photo != null)
                        {
                            _selectedMediaFile = photo;
                            NewReportMediaUrl = photo.FullPath;
                            NewReportMediaType = "Image";
                            NewReportHasMedia = true;
                        }
                    }
                    else
                    {
                        await Shell.Current.DisplayAlert("Camera Unavailable", "Camera capture is not supported on this device.", "OK");
                    }
                }
                else
                {
                    await Shell.Current.DisplayAlert("Permission Denied", "Camera permission is required to take photos.", "OK");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Media pick error: {ex.Message}");
                await Shell.Current.DisplayAlert("Camera Error", ex.Message, "OK");
            }
        }

        [RelayCommand]
        private void RemoveMedia()
        {
            _selectedMediaFile = null;
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
                await Shell.Current.DisplayAlert("Required Field", "Please enter a title for your community report.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(NewReportDescription))
            {
                await Shell.Current.DisplayAlert("Required Field", "Please enter a description of the incident.", "OK");
                return;
            }

            string publicMediaUrl = string.Empty;

            if (_selectedMediaFile != null)
            {
                try
                {
                    using var stream = await _selectedMediaFile.OpenReadAsync();
                    var uploadedUrl = await RescuAR.App.Services.Cloud.CloudinaryService.UploadImageStreamAsync(stream, _selectedMediaFile.FileName);
                    if (!string.IsNullOrWhiteSpace(uploadedUrl))
                    {
                        publicMediaUrl = uploadedUrl;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Stream upload error: {ex.Message}");
                }
            }

            if (string.IsNullOrWhiteSpace(publicMediaUrl) && !string.IsNullOrWhiteSpace(NewReportMediaUrl))
            {
                var uploadedUrl = await RescuAR.App.Services.Cloud.CloudinaryService.UploadImageAsync(NewReportMediaUrl);
                if (!string.IsNullOrWhiteSpace(uploadedUrl))
                {
                    publicMediaUrl = uploadedUrl;
                }
            }

            string userFullName = await GetActiveUserFullNameAsync();

            var report = new CommunityReport
            {
                Title = NewReportTitle.Trim(),
                Description = NewReportDescription.Trim(),
                Category = NewReportCategory,
                Address = string.IsNullOrWhiteSpace(NewReportAddress) ? "Marikina City" : NewReportAddress.Trim(),
                Latitude = NewReportLatitude,
                Longitude = NewReportLongitude,
                DistanceText = "50 meters away",
                PostedBy = userFullName,
                CreatedAt = DateTime.UtcNow,
                MediaUrl = publicMediaUrl,
                MediaType = NewReportMediaType,
                HasMedia = !string.IsNullOrWhiteSpace(publicMediaUrl),
                AllowComments = NewReportAllowComments,
                Status = "Pending"
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

            var updatedReport = _reportService.Reports.FirstOrDefault(r => r.Id == report.Id);
            if (updatedReport != null && updatedReport != report)
            {
                report.IsLikedByCurrentUser = updatedReport.IsLikedByCurrentUser;
                report.LikeCount = updatedReport.LikeCount;
            }

            var index = Reports.IndexOf(report);
            if (index >= 0)
            {
                Reports[index] = null!;
                Reports[index] = report;
            }
        }

        [RelayCommand]
        private async Task OpenNotificationsAsync()
        {
            foreach (var notif in Notifications)
            {
                notif.IsRead = true;
            }
            UnreadNotificationsCount = 0;
            
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("NotificationsPage");
            }
        }

        [RelayCommand]
        private void ClearNotifications()
        {
            Notifications.Clear();
            UnreadNotificationsCount = 0;
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

        private async Task<string> GetActiveUserFullNameAsync()
        {
            string fname = Microsoft.Maui.Storage.Preferences.Default.Get("UserFirstName", "").Trim();
            string lname = Microsoft.Maui.Storage.Preferences.Default.Get("UserLastName", "").Trim();
            string storedFullName = $"{fname} {lname}".Trim();
            if (!string.IsNullOrWhiteSpace(storedFullName)) return storedFullName;

            string username = Microsoft.Maui.Storage.Preferences.Default.Get("UserName", "").Trim();
            if (!string.IsNullOrWhiteSpace(username)) return username;

            try
            {
                var client = await RescuAR.Services.SupabaseService.Instance.GetClientAsync();
                if (client?.Auth?.CurrentUser != null)
                {
                    var user = client.Auth.CurrentUser;
                    if (user.UserMetadata != null)
                    {
                        if (user.UserMetadata.TryGetValue("full_name", out var fnObj) && fnObj != null && !string.IsNullOrWhiteSpace(fnObj.ToString()))
                            return fnObj.ToString()!.Trim();
                        if (user.UserMetadata.TryGetValue("name", out var nameObj) && nameObj != null && !string.IsNullOrWhiteSpace(nameObj.ToString()))
                            return nameObj.ToString()!.Trim();
                    }

                    var profileRes = await client.From<RescuAR.App.Models.SupabaseProfile>().Filter("id", Supabase.Postgrest.Constants.Operator.Equals, user.Id).Get();
                    if (profileRes?.Models != null && profileRes.Models.Count > 0)
                    {
                        var prof = profileRes.Models.First();
                        string profName = $"{prof.FirstName} {prof.LastName}".Trim();
                        if (!string.IsNullOrWhiteSpace(profName)) return profName;
                    }

                    if (!string.IsNullOrWhiteSpace(user.Email) && user.Email.Contains("@"))
                    {
                        string emailPrefix = user.Email.Split('@')[0];
                        if (!string.IsNullOrWhiteSpace(emailPrefix))
                        {
                            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(emailPrefix.Replace('.', ' ').Replace('_', ' '));
                        }
                    }
                }
            }
            catch { }

            return "Anonymous Resident";
        }
    }

    public class ReportNotification
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsRead { get; set; } = false;
        public string TimestampText => Timestamp.ToString("MMM dd, yyyy - hh:mm tt");
  }
}
