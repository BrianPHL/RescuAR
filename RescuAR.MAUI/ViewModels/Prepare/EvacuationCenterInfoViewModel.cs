using RescuAR.App.Services.Evacuation;
#if ANDROID
using Android.Util;
#endif

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using Microsoft.Extensions.DependencyInjection;
using RescuAR.App.Models;
using RescuAR.App.Services.Storage;
using RescuAR.Diagnostics;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.MAUI.Services.Location;
using GeoCoordinate = RescuAR.Navigation.Models.GeoCoordinate;
using RescuAR.Services;

namespace RescuAR.App.ViewModels.Prepare;

public class EmergencyHotlineItem
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string BadgeText { get; set; } = "EMS";
}

public partial class CategoryFilterItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    public string BackgroundColor => IsSelected ? "#007E8A" : "#FFFFFF";
    public string TextColor => IsSelected ? "#FFFFFF" : "#334155";
    public string BorderColor => IsSelected ? "#007E8A" : "#CBD5E1";
}

public partial class EvacuationCenterItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _barangay = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClassificationColor))]
    private string _classification = "Flood-Safe Major";

    [ObservableProperty]
    private string _distance = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWithin5Km))]
    private double _distanceKm;

    public bool IsWithin5Km => DistanceKm > 0 && DistanceKm <= 5.0;

    [ObservableProperty]
    private string _detailedDistanceString = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _verifiedBy = "Marikina LGU";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFacilityImage))]
    private string _facilityImageUrl = string.Empty;

    [ObservableProperty]
    private string _mapImageSource = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpsDisplay))]
    private double _latitude;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpsDisplay))]
    private double _longitude;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OccupancyDisplay))]
    private int _capacity = 500;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OccupancyDisplay))]
    private int _currentEvacuees = 0;

    [ObservableProperty]
    private string _headOfficer = "Unassigned";

    [ObservableProperty]
    private string _contact = "N/A";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FacilitiesDisplay))]
    private List<string> _facilities = new();

    public string FacilitiesDisplay => Facilities != null && Facilities.Count > 0 ? string.Join(", ", Facilities) : "Not recorded";
    public string OccupancyDisplay => Capacity > 0 ? $"{CurrentEvacuees} / {Capacity} evacuees" : "Capacity unavailable";
    public string GpsDisplay => Latitude != 0 && Longitude != 0 ? $"{Latitude:F4}, {Longitude:F4}" : "N/A";

    [ObservableProperty]
    private bool _isNearestShelter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BookmarkFill))]
    [NotifyPropertyChangedFor(nameof(BookmarkStroke))]
    private bool _isBookmarked;

    public string BookmarkFill => IsBookmarked ? "#007E8A" : "Transparent";
    public string BookmarkStroke => IsBookmarked ? "#007E8A" : "#0F172A";

    public bool HasFacilityImage => !string.IsNullOrWhiteSpace(FacilityImageUrl);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusPillBg))]
    [NotifyPropertyChangedFor(nameof(StatusPillText))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private string _status = "Standby";

    public string StatusPillBg => Status switch
    {
        "Open" => "#DCFCE7",
        "Full" => "#FEE2E2",
        _ => "#EFF6FF"
    };

    public string StatusPillText => Status switch
    {
        "Open" => "#16A34A",
        "Full" => "#DC2626",
        _ => "#2563EB"
    };

    public string StatusLabel => Status switch
    {
        "Open" => "Open / Operational",
        "Full" => "Full Capacity",
        _ => "Standby / Ready"
    };

    public string ClassificationColor => Classification switch
    {
        "Flood-Safe Major" => "#0284C7",
        "Flood-Safe Minor" => "#0369A1",
        "Dual-Purpose Major" => "#7C3AED",
        "Dual-Purpose Minor" => "#6D28D9",
        "Earthquake-Safe Minor" => "#D97706",
        _ => "#475569"
    };

    [RelayCommand]
    public void ToggleBookmark()
    {
        IsBookmarked = !IsBookmarked;
        try
        {
            Preferences.Default.Set($"bookmark_{Name}", IsBookmarked);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving bookmark: {ex.Message}");
        }
    }

    public void LoadBookmarkState()
    {
        try
        {
            IsBookmarked = Preferences.Default.Get($"bookmark_{Name}", false);
        }
        catch
        {
            IsBookmarked = false;
        }
    }
}

[QueryProperty(nameof(EmergencyType), "type")]
[QueryProperty(nameof(FilterCategory), "filter")]
public partial class EvacuationCenterInfoViewModel : ObservableObject
{
    private readonly EvacuationDirectory directory = EvacuationDirectoryRuntime.Current;
    private long directoryRevision = -1;
    private bool directorySubscribed;
    private const string MldLogTag = "RescuAR-MLD";
    private readonly List<EvacuationCenterItem> _masterCentersList = new();
    private readonly ILocationService _locationService;
    private LocationReading? _selectionLocation;
    private bool _locationWasCurrent;
    private bool _locationRefreshInProgress;
    private Microsoft.Maui.Dispatching.IDispatcherTimer? _locationExpiryTimer;
    private double _userLat = double.NaN;
    private double _userLng = double.NaN;

    public bool HasUsableUserLocation => LocationReadingQuality.CanDisplay(_selectionLocation, DateTimeOffset.UtcNow);
    public bool HasCurrentUserLocation => _locationWasCurrent &&
        LocationReadingQuality.IsFresh(_selectionLocation, DateTimeOffset.UtcNow);

    [ObservableProperty]
    private string _locationStatusDescription = "Finding your location — distances are unavailable.";

    public ObservableCollection<EmergencyHotlineItem> Hotlines { get; } = new();
    public ObservableCollection<EvacuationCenterItem> EvacuationCenters { get; } = new();
    public ObservableCollection<CategoryFilterItem> CategoryFilters { get; } = new();

    [ObservableProperty]
    private string _emergencyType = string.Empty;

    [ObservableProperty]
    private string _filterCategory = string.Empty;

    [ObservableProperty]
    private string _selectedFilter = "Within 5 km";

    [ObservableProperty]
    private bool _isDetailsPopupVisible;

    [ObservableProperty]
    private bool _isEnlargedMapVisible;

    public double UserLatitude => _userLat;
    public double UserLongitude => _userLng;

    [ObservableProperty]
    private EvacuationCenterItem? _selectedCenter;

    [ObservableProperty]
    private bool _isStartingNavigation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PassScoreText))]
    private int _passScore = Preferences.Default.Get("PASS_Score", 72);

    public string PassScoreText => $"{PassScore}% prepared";

    [ObservableProperty]
    private bool _isOfflineData;

    [ObservableProperty]
    private string _offlineStatusTitle = "Offline Mode — Locally Saved Data";

    [ObservableProperty]
    private string _offlineStatusDescription = string.Empty;

    [ObservableProperty]
    private string _shelterSectionSubtitle = "Nearest shelter is listed first";

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private DateTimeOffset? _lastSuccessfulUpdate;

    private bool _isConnectivitySubscribed;

    public EvacuationCenterInfoViewModel()
    {
        _locationService = RescuAR.MAUI.MauiProgram.Services.GetRequiredService<ILocationService>();
        InitializeCategoryFilters();
        LoadData();
    }

    partial void OnEmergencyTypeChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.ToLowerInvariant().Contains("flood"))
        {
            _selectedCategoryDropdown = "Flood-Safe Major";
            OnPropertyChanged(nameof(SelectedCategoryDropdown));
            SelectCategoryFilterInternal("Flood-Safe Major");
        }
    }

    partial void OnFilterCategoryChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _selectedCategoryDropdown = value;
            OnPropertyChanged(nameof(SelectedCategoryDropdown));
            SelectCategoryFilterInternal(value);
        }
    }

    private void InitializeCategoryFilters()
    {
        CategoryFilters.Clear();
        CategoryFilters.Add(new CategoryFilterItem { Name = "Within 5 km", IsSelected = true });
        CategoryFilters.Add(new CategoryFilterItem { Name = "All Shelters", IsSelected = false });
        CategoryFilters.Add(new CategoryFilterItem { Name = "Flood-Safe Major", IsSelected = false });
        CategoryFilters.Add(new CategoryFilterItem { Name = "Flood-Safe Minor", IsSelected = false });
        CategoryFilters.Add(new CategoryFilterItem { Name = "Dual-Purpose Major", IsSelected = false });
        CategoryFilters.Add(new CategoryFilterItem { Name = "Dual-Purpose Minor", IsSelected = false });
        CategoryFilters.Add(new CategoryFilterItem { Name = "Earthquake-Safe Minor", IsSelected = false });
    }

    [RelayCommand]
    public void SelectCategoryFilter(string categoryName)
    {
        SelectCategoryFilterInternal(categoryName);
    }

    private void SelectCategoryFilterInternal(string categoryName)
    {
        SelectedFilter = categoryName;
        foreach (var item in CategoryFilters)
        {
            item.IsSelected = string.Equals(item.Name, categoryName, StringComparison.OrdinalIgnoreCase);
        }
        ApplyFilterAndSorting();
    }

    public List<string> CategoryNames { get; } = new()
    {
        "Within 5 km",
        "All Shelters",
        "Flood-Safe Major",
        "Flood-Safe Minor",
        "Dual-Purpose Major",
        "Dual-Purpose Minor",
        "Earthquake-Safe Minor"
    };

    [ObservableProperty]
    private string _selectedCategoryDropdown = "Within 5 km";

    partial void OnSelectedCategoryDropdownChanged(string value)
    {
        SelectedFilter = value;
        ApplyFilterAndSorting();
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilterAndSorting();
    }

    private void LoadDefaultHotlines()
    {
        Hotlines.Clear();
        Hotlines.Add(new EmergencyHotlineItem { Name = "Marikina Rescue 161", Number = "(02) 161", Type = "24/7 Emergency Medical & Rescue", BadgeText = "EMS" });
        Hotlines.Add(new EmergencyHotlineItem { Name = "Marikina PNP Central", Number = "(02) 8405-0091", Type = "Police Emergency Hotline", BadgeText = "PNP" });
        Hotlines.Add(new EmergencyHotlineItem { Name = "Marikina BFP Fire Dept", Number = "(02) 8646-0427", Type = "Fire & Rescue Brigade", BadgeText = "BFP" });
        Hotlines.Add(new EmergencyHotlineItem { Name = "Red Cross Marikina", Number = "(02) 8681-3442", Type = "Disaster Relief & Blood Bank", BadgeText = "PRC" });
    }

    private static bool IsConnectedToInternet()
    {
        try
        {
            return Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateOfflineStatusInfo()
    {
        var snapshot = directory.Current;
        IsOfflineData = snapshot.Origin != DirectoryOrigin.Live;
        OfflineStatusTitle = snapshot.Origin == DirectoryOrigin.Historical ? "Historical directory" : IsOfflineData ? "Saved directory" : "Directory updated";
        OfflineStatusDescription = snapshot.Notice;
    }

    private static string FormatTimestamp(DateTimeOffset dt)
    {
        var now = DateTimeOffset.Now;
        var span = now - dt;

        if (span.TotalSeconds >= 0 && span.TotalMinutes < 1)
            return "Just now";
        if (span.TotalMinutes >= 1 && span.TotalMinutes < 60)
            return $"{(int)span.TotalMinutes}m ago";
        if (dt.Date == now.Date)
            return $"Today at {dt:h:mm tt}";
        if (dt.Date == now.Date.AddDays(-1))
            return $"Yesterday at {dt:h:mm tt}";

        return dt.ToString("MMM d, yyyy 'at' h:mm tt");
    }

    private static EvacuationCenterItem CreateItemFromCached(CachedEvacuationCenterItem cached)
    {
        var item = new EvacuationCenterItem
        {
            Id = cached.Id,
            Name = cached.Name,
            Barangay = cached.Barangay,
            Classification = string.IsNullOrWhiteSpace(cached.Classification) ? "Unspecified" : cached.Classification,
            Address = cached.Address,
            VerifiedBy = string.IsNullOrWhiteSpace(cached.VerifiedBy) ? "Not recorded" : cached.VerifiedBy,
            Latitude = cached.Latitude,
            Longitude = cached.Longitude,
            Capacity = Math.Max(0, cached.Capacity),
            CurrentEvacuees = cached.CurrentEvacuees,
            Status = string.IsNullOrWhiteSpace(cached.Status) ? "Unknown" : cached.Status,
            HeadOfficer = string.IsNullOrWhiteSpace(cached.HeadOfficer) ? "Unassigned" : cached.HeadOfficer,
            Contact = string.IsNullOrWhiteSpace(cached.Contact) ? "N/A" : cached.Contact,
            Facilities = cached.Facilities ?? new List<string>(),
            FacilityImageUrl = cached.FacilityImageUrl ?? string.Empty,
            MapImageSource = string.IsNullOrWhiteSpace(cached.MapImageSource)
                ? $"https://staticmap.openstreetmap.de/staticmap.php?center={cached.Latitude},{cached.Longitude}&zoom=16&size=600x300&markers={cached.Latitude},{cached.Longitude},red-pushpin"
                : cached.MapImageSource
        };

        item.LoadBookmarkState();

        return item;
    }

    private void RecalculateDistances()
    {
        bool usable = HasUsableUserLocation;
        bool current = usable && HasCurrentUserLocation;
        foreach (var center in _masterCentersList)
        {
            if (usable && new GeoCoordinate(center.Latitude, center.Longitude).IsValid &&
                (center.Latitude != 0 || center.Longitude != 0))
            {
                double distKm = _selectionLocation!.Coordinate.DistanceTo(
                    new GeoCoordinate(center.Latitude, center.Longitude)) / 1000.0;
                center.DistanceKm = distKm;
                string distance = distKm < 1 ? $"{distKm * 1000:F0} meters" : $"{distKm:F1} km";
                center.Distance = $"{(current ? "" : "About ")}{distance} away";
                center.DetailedDistanceString = $"{distance} in a straight line; walking distance may be longer";
            }
            else
            {
                center.DistanceKm = double.PositiveInfinity;
                center.Distance = "Distance unavailable";
                center.DetailedDistanceString = "A recent location is needed to estimate distance";
            }
        }
        LocationStatusDescription = usable
            ? $"{(current ? "Location" : "Last known location")} at {_selectionLocation!.Timestamp.ToLocalTime():h:mm:ss tt} " +
              $"(±{_selectionLocation.AccuracyMeters:F0} m). Distances are straight-line estimates."
            : "Location unavailable — keep Location enabled and refresh. Shelters are listed by name.";
        OnPropertyChanged(nameof(HasUsableUserLocation));
        OnPropertyChanged(nameof(HasCurrentUserLocation));
        OnPropertyChanged(nameof(UserLatitude));
        OnPropertyChanged(nameof(UserLongitude));
        UpdateOfflineStatusInfo();
        if (!usable) ShelterSectionSubtitle = "Location unavailable • Listed by name";
        else if (!current) ShelterSectionSubtitle = "Distances from last known position";
        ApplyFilterAndSorting();
    }

    private void LoadData()
    {
        var cached = CacheService.LoadEvacuationCenters();
        if (cached is not null && (cached.SourceKey.Length == 0 || cached.SourceKey == directory.Current.SourceKey) && cached.Hotlines.Count > 0)
        {
            Hotlines.Clear();
            foreach (var hotline in cached.Hotlines) Hotlines.Add(new() { Name = hotline.Name, Number = hotline.Number, Type = hotline.Type, BadgeText = hotline.BadgeText });
        }
        else LoadDefaultHotlines();
        PresentDirectory(directory.Current);
    }

    private void DirectoryChanged(EvacuationDirectorySnapshot snapshot) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (directorySubscribed && snapshot.SourceKey == directory.Current.SourceKey) PresentDirectory(snapshot);
    });

    private void PresentDirectory(EvacuationDirectorySnapshot snapshot)
    {
        if (directoryRevision == snapshot.Revision) return;
        directoryRevision = snapshot.Revision;
        string? selected = SelectedCenter?.Id;
        _masterCentersList.Clear();
        _masterCentersList.AddRange(snapshot.Centers.Select(CreateItemFromCached));
        if (selected is not null)
        {
            SelectedCenter = _masterCentersList.FirstOrDefault(center => center.Id == selected);
            if (SelectedCenter is null) IsDetailsPopupVisible = IsEnlargedMapVisible = false;
        }
        LastSuccessfulUpdate = snapshot.UpdatedAt;
        UpdateOfflineStatusInfo(); RecalculateDistances();
    }

    public void OnAppearing()
    {
        if (!directorySubscribed) { directory.Changed += DirectoryChanged; directorySubscribed = true; }
        PresentDirectory(directory.Current);
        if (_locationExpiryTimer is null && Application.Current?.Dispatcher is { } dispatcher)
        {
            _locationExpiryTimer = dispatcher.CreateTimer();
            _locationExpiryTimer.Interval = TimeSpan.FromSeconds(10);
            _locationExpiryTimer.Tick += (_, _) => RecalculateDistances();
        }
        _locationExpiryTimer?.Start();
        if (!_isConnectivitySubscribed)
        {
            try
            {
                Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
                _isConnectivitySubscribed = true;
            }
            catch { }
        }

        _ = RefreshDataAsync();
    }

    public void OnDisappearing()
    {
        if (directorySubscribed) { directory.Changed -= DirectoryChanged; directorySubscribed = false; }
        _locationExpiryTimer?.Stop();
        if (_isConnectivitySubscribed)
        {
            try
            {
                Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
                _isConnectivitySubscribed = false;
            }
            catch { }
        }
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        bool isConnected = e.NetworkAccess == NetworkAccess.Internet;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (isConnected)
            {
                await RefreshDataAsync();
            }
            else
            {
                await FetchEvacuationCentersFromSupabaseAsync();
                RecalculateDistances();
                await FilterEvacuationCentersByGpsAsync();
            }
        });
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        try
        {
            IsRefreshing = true;
            await RefreshDataAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public async Task RefreshDataAsync()
    {
        Task locationRefresh = FilterEvacuationCentersByGpsAsync();
        if (!IsConnectedToInternet())
        {
            await FetchEvacuationCentersFromSupabaseAsync();
            await locationRefresh;
            RecalculateDistances();
            return;
        }

        await Task.WhenAll(
            locationRefresh,
            FetchHotlinesFromSupabaseAsync(),
            FetchEvacuationCentersFromSupabaseAsync()
        );
    }

    public async Task FetchEvacuationCentersFromSupabaseAsync()
    {
        var snapshot = await directory.RefreshAsync(force: true);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (directorySubscribed) PresentDirectory(snapshot);
            CacheService.UpdateHotlines(Hotlines.Select(hotline => new CachedEmergencyHotlineItem
            { Name = hotline.Name, Number = hotline.Number, Type = hotline.Type, BadgeText = hotline.BadgeText }), snapshot.SourceKey);
        });
    }

    public async Task FetchHotlinesFromSupabaseAsync()
    {
        if (!IsConnectedToInternet()) return;

        try
        {
            var client = await SupabaseService.Instance.GetClientAsync();
            if (client != null)
            {
                var response = await client.From<SupabaseEmergencyHotline>()
                    .Order("created_at", Supabase.Postgrest.Constants.Ordering.Ascending)
                    .Get();

                if (response?.Models != null && response.Models.Count > 0)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        Hotlines.Clear();
                        foreach (var h in response.Models)
                        {
                            Hotlines.Add(new EmergencyHotlineItem
                            {
                                Name = h.Agency,
                                Number = h.PrimaryNumber,
                                Type = string.IsNullOrWhiteSpace(h.Coverage) ? h.Availability : $"{h.Availability} • {h.Coverage}",
                                BadgeText = string.IsNullOrWhiteSpace(h.Category) ? "EMS" : h.Category.ToUpper()
                            });
                        }
                        CacheService.UpdateHotlines(Hotlines.Select(hotline => new CachedEmergencyHotlineItem
                        { Name = hotline.Name, Number = hotline.Number, Type = hotline.Type, BadgeText = hotline.BadgeText }), directory.Current.SourceKey);
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to fetch live hotlines from Supabase: {ex.Message}");
        }
    }

    private async Task FilterEvacuationCentersByGpsAsync()
    {
        if (_locationRefreshInProgress) return;
        _locationRefreshInProgress = true;
        try
        {
            // The same singleton retains qualified Camera readings. Internet
            // connectivity controls catalog downloads, not device positioning.
            var cached = await _locationService.GetLastKnownLocationAsync();
            if (LocationReadingQuality.CanDisplay(cached, DateTimeOffset.UtcNow))
                await ApplySelectionLocationAsync(cached!, current: false);

            var fresh = await _locationService.GetCurrentLocationAsync();
            if (LocationReadingQuality.IsFresh(fresh, DateTimeOffset.UtcNow) &&
                LocationReadingQuality.CanDisplay(fresh, DateTimeOffset.UtcNow))
                await ApplySelectionLocationAsync(fresh!, current: true);
        }
        catch (Exception ex)
        {
            AndroidLog.Warn("RescuAR-LocationSelection",
                "Shelter location refresh unavailable; retaining only eligible last-known positioning: " +
                DiagnosticPrivacyPolicy.FormatException(ex));
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(RecalculateDistances);
            _locationRefreshInProgress = false;
        }
    }

    private Task ApplySelectionLocationAsync(LocationReading reading, bool current) =>
        MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_selectionLocation is null || reading.Timestamp > _selectionLocation.Timestamp ||
                (current && reading.Timestamp == _selectionLocation.Timestamp))
            {
                _selectionLocation = reading;
                _locationWasCurrent = current;
                _userLat = reading.Coordinate.Latitude;
                _userLng = reading.Coordinate.Longitude;
                AndroidLog.Info("RescuAR-LocationSelection", $"SHELTER_LOCATION source={(current ? "CURRENT" : "LAST_RELIABLE")}; " +
                    $"accuracy={reading.AccuracyMeters:F1}m; ageSec={(DateTimeOffset.UtcNow - reading.Timestamp).TotalSeconds:F1}; " +
                    $"coordinate={DiagnosticPrivacyPolicy.FormatCoordinate(_userLat, _userLng)}; " +
                    "distances=straight-line; route-start and arrival freshness gates remain separate.");
            }
            RecalculateDistances();
        });

    private void ApplyFilterAndSorting()
    {
        IEnumerable<EvacuationCenterItem> filtered = _masterCentersList;

        string activeFilter = !string.IsNullOrWhiteSpace(SelectedCategoryDropdown) ? SelectedCategoryDropdown : SelectedFilter;

        if (string.Equals(activeFilter, "Within 5 km", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(activeFilter, "Within 5 km radius", StringComparison.OrdinalIgnoreCase))
        {
            if (HasUsableUserLocation) filtered = filtered.Where(c => c.DistanceKm <= 5.0);
        }
        else if (string.Equals(activeFilter, "Flood-Safe All", StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(EmergencyType) && EmergencyType.ToLowerInvariant().Contains("flood")))
        {
            filtered = filtered.Where(c => 
                c.Classification.StartsWith("Flood-Safe", StringComparison.OrdinalIgnoreCase) ||
                c.Classification.StartsWith("Dual-Purpose", StringComparison.OrdinalIgnoreCase));
        }
        else if (!string.IsNullOrWhiteSpace(activeFilter) && 
                 !string.Equals(activeFilter, "All Shelters", StringComparison.OrdinalIgnoreCase) && 
                 !string.Equals(activeFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(c => 
                string.Equals(c.Classification, activeFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string query = SearchText.Trim().ToLowerInvariant();
            filtered = filtered.Where(c => 
                c.Name.ToLowerInvariant().Contains(query) ||
                c.Barangay.ToLowerInvariant().Contains(query) ||
                c.Classification.ToLowerInvariant().Contains(query) ||
                c.HeadOfficer.ToLowerInvariant().Contains(query));
        }

        var sortedList = HasUsableUserLocation
            ? filtered.OrderBy(c => c.DistanceKm).ToList()
            : filtered.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();

        // Mark first element as Nearest Shelter
        foreach (var center in _masterCentersList) center.IsNearestShelter = false;
        for (int i = 0; i < sortedList.Count; i++)
        {
            sortedList[i].IsNearestShelter = HasCurrentUserLocation && i == 0;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            EvacuationCenters.Clear();
            foreach (var item in sortedList)
            {
                EvacuationCenters.Add(item);
            }
        });
    }

    [RelayCommand]
    private void ToggleSelectedCenterBookmark()
    {
        SelectedCenter?.ToggleBookmark();
    }

    [RelayCommand]
    private async Task OpenPASSAssessmentAsync()
    {
        try
        {
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("Prepare/PASS");
            }
        }
        catch
        {
            var fallbackPage = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (fallbackPage != null)
            {
                await fallbackPage.Navigation.PushAsync(new Views.Prepare.PASSPage());
            }
        }
    }

    [RelayCommand]
    private async Task MakePhoneCall(string number)
    {
        if (string.IsNullOrWhiteSpace(number) || number == "N/A") return;

        try
        {
            string cleanDigits = System.Text.RegularExpressions.Regex.Replace(number, @"[^\d+]", "");
            if (!string.IsNullOrWhiteSpace(cleanDigits))
            {
                var uri = new Uri($"tel:{cleanDigits}");
                await Launcher.Default.OpenAsync(uri);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Phone call error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ViewMoreDetails(EvacuationCenterItem center)
    {
        SelectedCenter = center;
        IsDetailsPopupVisible = true;
    }

    [RelayCommand]
    private void CloseDetailsPopup()
    {
        IsDetailsPopupVisible = false;
        IsEnlargedMapVisible = false;
        SelectedCenter = null;
    }

    [RelayCommand]
    private void EnlargeMap()
    {
        IsEnlargedMapVisible = true;
    }

    [RelayCommand]
    private void CloseEnlargedMap()
    {
        IsEnlargedMapVisible = false;
    }

    [RelayCommand]
    private async Task NavigateToCameraAsync(EvacuationCenterItem? center = null)
    {
        if (center is not null)
        {
            await StartNavigationToCenterAsync(center, closeDetailsPopup: false);
            return;
        }

        if (Shell.Current is not null)
        {
            await Shell.Current.GoToAsync("//Camera");
        }
    }

    [RelayCommand]
    private async Task StartARNavigationAsync()
    {
        var center = SelectedCenter;
        if (center is null) return;
        await StartNavigationToCenterAsync(center, closeDetailsPopup: true);
    }

    private async Task StartNavigationToCenterAsync(EvacuationCenterItem center, bool closeDetailsPopup, bool map2D = false)
    {
        if (IsStartingNavigation) return;
        var current = directory.ResolveSelected(center.Id, center.Latitude, center.Longitude);
        if (current is null)
        {
            if (Shell.Current is not null) await Shell.Current.DisplayAlert("Directory changed", "Select this evacuation center again from the updated directory.", "OK");
            return;
        }
        center = CreateItemFromCached(current);
        IsStartingNavigation = true;

#if ANDROID
        Log.Debug(
            MldLogTag,
            "Evacuation-center AR navigation selected: " +
            $"name='{DiagnosticPrivacyPolicy.FormatRouteLabel(center.Name)}', " +
            $"coordinate={DiagnosticPrivacyPolicy.FormatCoordinate(center.Latitude, center.Longitude)}");
#endif

        try
        {
            if (closeDetailsPopup)
            {
                CloseDetailsPopup();
            }

            bool opened = map2D
                ? await CameraNavigationLauncher.Open2DAsync(center.Name, center.Latitude, center.Longitude)
                : await CameraNavigationLauncher.OpenAsync(center.Name, center.Latitude, center.Longitude);

            if (!opened && Shell.Current is not null)
            {
                await Shell.Current.DisplayAlert(
                    map2D ? "2D Navigation" : "AR Navigation",
                    "This evacuation center could not be used as a navigation destination.",
                    "OK");
            }
        }
        catch (Exception exception)
        {
#if ANDROID
            Log.Error(MldLogTag, $"Starting AR navigation failed: {exception}");
#endif

            if (Shell.Current is not null)
            {
                await Shell.Current.DisplayAlert(
                    map2D ? "2D Navigation" : "AR Navigation",
                    "Unable to start AR navigation.",
                    "OK");
            }
        }
        finally
        {
            IsStartingNavigation = false;
        }
    }

    [RelayCommand]
    private async Task NavigateTo2DMapAsync(EvacuationCenterItem? center)
    {
        if (center is not null) await StartNavigationToCenterAsync(center, closeDetailsPopup: false, map2D: true);
    }

    [RelayCommand]
    private async Task Start2DNavigationAsync()
    {
        if (SelectedCenter is { } center) await StartNavigationToCenterAsync(center, closeDetailsPopup: true, map2D: true);
    }

    [RelayCommand]
    private async Task NavigateToMapAsync()
    {
        if (!await CameraNavigationLauncher.OpenMapAsync() && Shell.Current is { } shell)
            await shell.DisplayAlert("2D Map", "The map could not be opened. Please try again.", "OK");
    }
}
