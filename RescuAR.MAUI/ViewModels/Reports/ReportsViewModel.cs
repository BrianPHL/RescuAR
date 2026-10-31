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
        private ResidentReportScope? _reportOwner;
        private IncidentPoint? _reportPoint;
        private System.Threading.CancellationTokenSource? _reportCancellation;
        private readonly ResidentReportMediaDraft _reportMedia = new(ResidentReportRuntime.Context);
        private string _reportId = "";
        private string _reportSelectedMediaKey = "";
        private DateTime _reportCreatedAt;
        private long _reportDraftVersion, _reportLocationVersion;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEditReport))] private bool isReportSubmitting;
        [ObservableProperty] private string reportSubmissionStatus = "";
        [ObservableProperty] private string reportLocationStatus = "Choose a GPS or map point for the incident.";
        public bool CanEditReport => !IsReportSubmitting;

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

        // Vicinity Hazard Monitoring & Alerts
        [ObservableProperty]
        private bool isVicinityMonitoringActive = true;

        [ObservableProperty]
        private bool hasVicinityAlerts = true;

        [ObservableProperty]
        private string vicinityStatusTitle = "Vicinity Monitoring Active";

        [ObservableProperty]
        private string vicinityStatusSubtitle = "Scanning active disaster reports & affected areas...";

        [ObservableProperty]
        private string vicinityAffectedAreas = "Affected Areas: Barangay Malanday • Concepcion Uno";

        [ObservableProperty]
        private string vicinityAlertSeverity = "Medium";

        [ObservableProperty]
        private string vicinityAlertBadgeColor = "#FFFBEB";

        [ObservableProperty]
        private string vicinityAlertTextColor = "#92400E";

        [ObservableProperty]
        private string vicinityAlertBorderColor = "#FDE68A";

        [ObservableProperty]
        private string vicinityAlertIcon = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z";

        [ObservableProperty]
        private string vicinityActiveCountText = "Scanning...";

        [ObservableProperty]
        private double userLatitude = 14.6585;

        [ObservableProperty]
        private double userLongitude = 121.0955;

        [ObservableProperty]
        private bool isUserLocationLoaded;

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
        private string newReportAddress = string.Empty;

        [ObservableProperty]
        private double newReportLatitude = double.NaN;

        [ObservableProperty]
        private double newReportLongitude = double.NaN;

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

            RescuAR.App.Services.Reports.RealtimeAdvisoryManager.OnNewAdvisoryPushed -= HandleNewAdvisoryPushed;
            RescuAR.App.Services.Reports.RealtimeAdvisoryManager.OnNewAdvisoryPushed += HandleNewAdvisoryPushed;
            RescuAR.MAUI.Services.Settings.AppPreferences.ObserveWeak(this, static (vm, setting) =>
            {
                if (setting == RescuAR.MAUI.Services.Settings.AppSetting.ForegroundAdvisories &&
                    !RescuAR.MAUI.Services.Settings.AppPreferences.IsEnabled(setting))
                    MainThread.BeginInvokeOnMainThread(() => vm.IsPopupVisible = false);
            });
        }

        private void HandleNewAdvisoryPushed(DisasterAdvisory newAdvisory)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (RescuAR.MAUI.Services.Settings.AppPreferences.IsEnabled(RescuAR.MAUI.Services.Settings.AppSetting.ForegroundAdvisories))
                {
                    SelectedAdvisory = newAdvisory;
                    IsPopupVisible = true;
                }
            });
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
                // Attempt to fetch current user location for vicinity distance monitoring
                try
                {
                    var location = await Geolocation.Default.GetLastKnownLocationAsync();
                    if (location == null)
                    {
                        location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Low, TimeSpan.FromSeconds(2)));
                    }

                    if (location != null)
                    {
                        UserLatitude = location.Latitude;
                        UserLongitude = location.Longitude;
                        IsUserLocationLoaded = true;
                    }
                }
                catch (Exception locEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Vicinity location fetch warning: {locEx.Message}");
                }

                var list = await _reportService.GetReportsAsync(SearchQuery, SelectedFilter, UserLatitude, UserLongitude);

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

                // Update real-time Vicinity Hazard Monitoring Alerts
                UpdateVicinityHazardAlerts(list);
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

        private void UpdateVicinityHazardAlerts(List<CommunityReport> reports)
        {
            if (reports == null || reports.Count == 0)
            {
                HasVicinityAlerts = true;
                VicinityAlertSeverity = "Clear";
                VicinityStatusTitle = "Vicinity Clear • Safe Area";
                VicinityStatusSubtitle = "No active disaster reports within your 5 km vicinity.";
                VicinityAffectedAreas = "Your approximate location is currently clear of reported hazards.";
                VicinityAlertBadgeColor = "#ECFDF5";
                VicinityAlertTextColor = "#065F46";
                VicinityAlertBorderColor = "#A7F3D0";
                VicinityActiveCountText = "0 Hazards Nearby";
                VicinityAlertIcon = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z";
                return;
            }

            // Filter hazards within user vicinity
            var nearbyHazards = reports.OrderBy(r => r.DistanceKm).ToList();
            var nearest = nearbyHazards.FirstOrDefault();
            int count = nearbyHazards.Count;
            VicinityActiveCountText = $"{count} Hazard{(count > 1 ? "s" : "")} Nearby";

            // Extract unique affected areas from reports address/description
            var areaList = new List<string>();
            foreach (var r in nearbyHazards)
            {
                if (!string.IsNullOrWhiteSpace(r.Address))
                {
                    var parts = r.Address.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        var area = parts[0].Trim();
                        if (!areaList.Contains(area)) areaList.Add(area);
                    }
                }
            }

            VicinityAffectedAreas = areaList.Count > 0
                ? $"Affected Areas: {string.Join(" • ", areaList.Take(3))}"
                : "Affected Areas: Barangay Malanday • Concepcion Uno";

            // Determine highest severity
            bool hasRescueOrFlood = nearbyHazards.Any(r =>
                (r.Category != null && r.Category.Equals("Rescue Request", StringComparison.OrdinalIgnoreCase)) ||
                (r.Category != null && r.Category.Equals("Flood Warning", StringComparison.OrdinalIgnoreCase)));

            if (hasRescueOrFlood || (nearest != null && nearest.DistanceKm <= 1.0))
            {
                VicinityAlertSeverity = "High";
                VicinityStatusTitle = $"⚠️ Vicinity Hazard Alert ({nearest?.DistanceText ?? "Nearby"})";
                VicinityStatusSubtitle = nearest != null
                    ? $"Nearest: {nearest.Category} - {nearest.Title}"
                    : $"{count} disaster hazard reports within your vicinity.";
                VicinityAlertBadgeColor = "#FEF2F2";
                VicinityAlertTextColor = "#991B1B";
                VicinityAlertBorderColor = "#FECACA";
                VicinityAlertIcon = "M12,2L1,21H23L12,2M12,6L19.8,20H4.2L12,6M11,10V14H13V10H11M11,16V18H13V16H11Z";
            }
            else
            {
                VicinityAlertSeverity = "Medium";
                VicinityStatusTitle = $"⚡ Vicinity Caution ({nearest?.DistanceText ?? "Nearby"})";
                VicinityStatusSubtitle = nearest != null
                    ? $"Nearest: {nearest.Category} - {nearest.Title}"
                    : $"{count} active reports nearby.";
                VicinityAlertBadgeColor = "#FFFBEB";
                VicinityAlertTextColor = "#92400E";
                VicinityAlertBorderColor = "#FDE68A";
                VicinityAlertIcon = "M11,15H13V17H11V15M11,7H13V13H11V7M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z";
            }
        }

        [RelayCommand]
        private async Task RefreshVicinityLocationAsync()
        {
            await FetchUserLocationAsync();
            await LoadReportsAsync();
        }

        private void RequireReportDraft(ResidentReportScope owner, long request, System.Threading.CancellationToken token = default)
        {
            ResidentReportRuntime.Context.Require(owner, token);
            if (_reportOwner != owner || request != _reportDraftVersion || !IsCreateModalVisible)
                throw new OperationCanceledException("The report form changed.", token);
        }

        public void PauseReportWork()
        {
            _reportCancellation?.Cancel();
            _reportDraftVersion++; _reportLocationVersion++;
            IsReportSubmitting = false; IsFetchingLocation = false; IsSuccessModalVisible = false;
        }

        public void ResumeReportWork()
        {
            if (_reportOwner is not { } owner) return;
            try
            {
                ResidentReportRuntime.Context.Require(owner);
                if (_reportCancellation?.IsCancellationRequested != false)
                { _reportCancellation?.Dispose(); _reportCancellation = new(); }
            }
            catch
            {
                PauseReportWork(); IsCreateModalVisible = IsMapPickerVisible = false;
                NewReportTitle = NewReportDescription = NewReportAddress = NewReportMediaUrl = "";
                NewReportHasMedia = false; _selectedMediaFile = null; _reportOwner = null; _reportPoint = null; _reportId = ""; _reportMedia.Reset();
            }
        }

        [RelayCommand]
        private async Task OpenCreateModalAsync()
        {
            PauseReportWork();
            _reportCancellation?.Dispose(); _reportCancellation = new();
            _reportOwner = null; _reportPoint = null; _reportId = Guid.NewGuid().ToString(); _reportCreatedAt = default;
            _reportMedia.Reset(); _reportSelectedMediaKey = ""; _selectedMediaFile = null;
            NewReportTitle = NewReportDescription = NewReportAddress = NewReportMediaUrl = "";
            NewReportLatitude = NewReportLongitude = double.NaN;
            NewReportCategory = "Flood Warning"; NewReportHasMedia = false; NewReportAllowComments = true;
            ReportSubmissionStatus = ""; ReportLocationStatus = "Choose a GPS or map point for the incident.";
            IsCreateModalVisible = true;
            try
            {
                _reportOwner = ResidentReportRuntime.Context.Capture();
                await FetchUserLocationAsync();
            }
            catch { ReportSubmissionStatus = "Sign in with a verified account before submitting a report."; }
        }

        [RelayCommand]
        private void CloseCreateModal()
        {
            PauseReportWork();
            IsCreateModalVisible = false;
        }

        private void SetIncidentPoint(IncidentPoint point)
        {
            _reportPoint = point;
            NewReportLatitude = point.Latitude; NewReportLongitude = point.Longitude;
            ReportLocationStatus = $"{(point.Source == IncidentPointSource.Gps ? "GPS" : "Map")} point selected: {point.Latitude:F5}, {point.Longitude:F5}. Review the incident location before submitting.";
        }

        [RelayCommand]
        private async Task FetchUserLocationAsync()
        {
            if (!IsCreateModalVisible || _reportOwner is not { } owner || _reportCancellation is null) return;
            long request = _reportDraftVersion, locationRequest = ++_reportLocationVersion;
            var token = _reportCancellation.Token;
            IsFetchingLocation = true;
            try
            {
                RequireReportDraft(owner, request, token);
                var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)), token);
                RequireReportDraft(owner, request, token);
                if (locationRequest != _reportLocationVersion) return;
                if (location is null) throw new ResidentReportInputException("GPS is unavailable. Choose the incident point on the map.");
                var point = IncidentPointPolicy.FromGps(location.Latitude, location.Longitude, location.Accuracy, location.Timestamp, location.IsFromMockProvider, DateTimeOffset.UtcNow);
                SetIncidentPoint(point); NewReportAddress = $"{point.Latitude:F5}, {point.Longitude:F5}";
                try
                {
                    var placemarks = await Geocoding.Default.GetPlacemarksAsync(location).WaitAsync(TimeSpan.FromSeconds(4), token);
                    RequireReportDraft(owner, request, token);
                    if (locationRequest != _reportLocationVersion) return;
                    var place = placemarks?.FirstOrDefault();
                    if (place is not null)
                    {
                        var parts = new[] { place.FeatureName, place.Thoroughfare, place.SubLocality, place.Locality }.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
                        if (parts.Count > 0) NewReportAddress = string.Join(", ", parts);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* The selected coordinates remain valid when an optional street label is unavailable. */ }
            }
            catch (Exception error)
            {
                if (request == _reportDraftVersion && locationRequest == _reportLocationVersion)
                    ReportLocationStatus = _reportPoint is null ? error is ResidentReportInputException ? error.Message : "GPS is unavailable. Choose the incident point on the map."
                        : "The previous incident point is retained. GPS could not confirm a replacement.";
            }
            finally { if (request == _reportDraftVersion && locationRequest == _reportLocationVersion) IsFetchingLocation = false; }
        }

        [RelayCommand]
        private async Task BackAsync()
        {
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("//Home");
            }
        }

        private FileResult? _selectedMediaFile;

        [RelayCommand]
        private async Task PickMediaAsync()
        {
            if (!CanEditReport || _reportOwner is not { } owner) return;
            long request = _reportDraftVersion;
            try
            {
                RequireReportDraft(owner, request);
                var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
                if (status != PermissionStatus.Granted) status = await Permissions.RequestAsync<Permissions.Camera>();
                RequireReportDraft(owner, request);
                if (status != PermissionStatus.Granted) { ReportSubmissionStatus = "Camera permission was not granted. The report draft is retained."; return; }
                if (!MediaPicker.Default.IsCaptureSupported) { ReportSubmissionStatus = "Camera capture is unavailable on this device."; return; }
                var photo = await MediaPicker.Default.CapturePhotoAsync(); RequireReportDraft(owner, request);
                if (photo is null) return;
                _selectedMediaFile = photo; _reportSelectedMediaKey = Guid.NewGuid().ToString(); _reportMedia.Reset();
                NewReportMediaUrl = photo.FullPath; NewReportMediaType = "Image"; NewReportHasMedia = true;
            }
            catch { if (request == _reportDraftVersion) ReportSubmissionStatus = "The photo could not be selected. No report was submitted."; }
        }

        [RelayCommand]
        private void RemoveMedia()
        {
            if (!CanEditReport) return;
            _selectedMediaFile = null; _reportSelectedMediaKey = ""; _reportMedia.Reset();
            NewReportMediaUrl = ""; NewReportHasMedia = false;
        }

        [RelayCommand]
        private void OpenMapPicker()
        {
            if (!CanEditReport) return;
            MapSearchQuery = ""; OsmSearchResults.Clear(); IsMapPickerVisible = true;
        }

        [RelayCommand] private void CloseMapPicker() => IsMapPickerVisible = false;

        [RelayCommand]
        private void SelectOsmLocation(OsmSearchResult item)
        {
            if (!CanEditReport || item is null || _reportOwner is not { } owner) return;
            try
            {
                RequireReportDraft(owner, _reportDraftVersion);
                var point = IncidentPointPolicy.FromMap(item.Latitude, item.Longitude, DateTimeOffset.UtcNow);
                _reportLocationVersion++; IsFetchingLocation = false; SetIncidentPoint(point);
                NewReportAddress = item.DisplayName; IsMapPickerVisible = false;
            }
            catch (Exception error) { ReportLocationStatus = error is ResidentReportInputException ? error.Message : "The incident point could not be selected. Reopen the report form."; }
        }

        [RelayCommand]
        private void SelectPresetLocation(string location)
        {
            if (!CanEditReport || string.IsNullOrWhiteSpace(location)) return;
            _reportLocationVersion++; IsFetchingLocation = false; _reportPoint = null;
            NewReportLatitude = NewReportLongitude = double.NaN;
            NewReportAddress = location; MapSearchQuery = location;
            ReportLocationStatus = "Choose the matching search result to confirm this incident point.";
        }

        [RelayCommand]
        private async Task ConfirmCustomMapLocationAsync()
        {
            if (!CanEditReport || _reportOwner is not { } owner || string.IsNullOrWhiteSpace(MapSearchQuery)) return;
            long request = _reportDraftVersion; string query = MapSearchQuery.Trim();
            IsSearchingOsm = true;
            try
            {
                RequireReportDraft(owner, request);
                var results = await _osmService.SearchLocationsAsync(query);
                RequireReportDraft(owner, request);
                if (query != MapSearchQuery.Trim()) return;
                OsmSearchResults = new ObservableCollection<OsmSearchResult>(results);
                ReportLocationStatus = results.Count > 0 ? "Choose the matching search result to confirm the incident point."
                    : "No mapped point was found. Try a different search or use GPS; a typed address alone does not select coordinates.";
            }
            catch { if (request == _reportDraftVersion) ReportLocationStatus = "Location search could not complete. Try again or use GPS."; }
            finally { if (request == _reportDraftVersion) IsSearchingOsm = false; }
        }

        [RelayCommand]
        private async Task SubmitReportAsync()
        {
            if (!CanEditReport || _reportOwner is not { } owner || _reportCancellation is null) return;
            long request = _reportDraftVersion;
            var token = _reportCancellation.Token;
            IsReportSubmitting = true; ReportSubmissionStatus = "Submitting report…";
            try
            {
                RequireReportDraft(owner, request, token);
                if (string.IsNullOrWhiteSpace(NewReportTitle) || string.IsNullOrWhiteSpace(NewReportDescription))
                    throw new ResidentReportInputException("Enter a title and description for the incident.");
                if (!IncidentPointPolicy.Chosen(_reportPoint) || NewReportLatitude != _reportPoint!.Latitude || NewReportLongitude != _reportPoint.Longitude)
                    throw new ResidentReportInputException("Choose a GPS or map point before submitting the report.");
                var point = _reportPoint!;
                string title = NewReportTitle.Trim(), description = NewReportDescription.Trim(), category = NewReportCategory, address = NewReportAddress.Trim();
                string mediaType = NewReportMediaType; bool allowComments = NewReportAllowComments;
                _reportLocationVersion++; IsFetchingLocation = false;
                if (_reportCreatedAt == default) _reportCreatedAt = DateTime.UtcNow;
                var selectedPhoto = _selectedMediaFile;
                string media = await _reportMedia.PrepareAsync(owner, selectedPhoto is null ? null : _reportSelectedMediaKey,
                    cancellation => ResidentReportRuntime.UploadPhotoAsync(owner, selectedPhoto!, cancellation), token);
                RequireReportDraft(owner, request, token);
                var report = new CommunityReport
                {
                    Id = _reportId, Title = title, Description = description, Category = category,
                    Address = address, Latitude = point.Latitude, Longitude = point.Longitude,
                    PostedBy = ResidentReportRuntime.Sender(owner), CreatedAt = _reportCreatedAt, MediaUrl = media,
                    MediaType = mediaType, AllowComments = allowComments, Status = "Pending"
                };
                await _reportService.AddReportAsync(report, owner, point, token);
                RequireReportDraft(owner, request, token);
                IsCreateModalVisible = false; IsSuccessModalVisible = true;
                ReportSubmissionStatus = "Report accepted by the server for review. Public visibility requires administrator approval.";
                await LoadReportsAsync();
                await Task.Delay(2000, token);
                ResidentReportRuntime.Context.Require(owner, token);
                if (request == _reportDraftVersion) IsSuccessModalVisible = false;
            }
            catch (Exception error)
            {
                if (request == _reportDraftVersion)
                {
                    bool sameAccount = true;
                    try { ResidentReportRuntime.Context.Require(owner); } catch { sameAccount = false; }
                    if (!sameAccount) { ResumeReportWork(); ReportSubmissionStatus = "Your account changed. Start a new report after signing in."; }
                    else ReportSubmissionStatus = error is ResidentReportInputException or ResidentReportUnconfirmedException ? error.Message
                        : "Submission was not confirmed. Your form and selected photo are retained. Submit again to retry the same report.";
                }
            }
            finally { if (request == _reportDraftVersion) IsReportSubmitting = false; }
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
