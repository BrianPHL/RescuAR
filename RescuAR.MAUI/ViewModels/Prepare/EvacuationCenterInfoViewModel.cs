#if ANDROID
using Android.Util;
#endif

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using RescuAR.App.Models;
using RescuAR.App.Services.Storage;
using RescuAR.Diagnostics;
using RescuAR.MAUI.Services.Navigation;
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

    public string FacilitiesDisplay => Facilities != null && Facilities.Count > 0 ? string.Join(", ", Facilities) : "Clean Water, Restrooms";
    public string OccupancyDisplay => Capacity > 0 ? $"{CurrentEvacuees} / {Capacity} evacuees" : $"{CurrentEvacuees} evacuees";
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
    private const string MldLogTag = "RescuAR-MLD";
    private readonly List<EvacuationCenterItem> _masterCentersList = new();
    private double _userLat = 14.6612;
    private double _userLng = 121.0963;

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
        InitializeCategoryFilters();
        LoadData();
        _ = FetchHotlinesFromSupabaseAsync();
        _ = FetchEvacuationCentersFromSupabaseAsync();
        _ = FilterEvacuationCentersByGpsAsync();
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

    private static List<EvacuationCenterItem> GetOfficialMarikinaCenters()
    {
        return new List<EvacuationCenterItem>
        {
            new EvacuationCenterItem
            {
                Name = "Malanday Elementary School",
                Barangay = "Malanday",
                Classification = "Flood-Safe Major",
                Address = "48 Visayas St., Malanday, 1805 Marikina City",
                Latitude = 14.650283,
                Longitude = 121.094409,
                Capacity = 1200,
                CurrentEvacuees = 0,
                Status = "Standby",
                HeadOfficer = "Capt. Roberto Santos",
                Contact = "0917-555-0192",
                Facilities = new List<string> { "Medical Station", "Clean Water", "Generator", "Modular Tents" },
                FacilityImageUrl = string.Empty
            },
            new EvacuationCenterItem
            {
                Name = "H. Bautista Elementary School",
                Barangay = "Concepcion Uno",
                Classification = "Flood-Safe Major",
                Address = "Liwasang Kalayaan, Concepcion Uno, 1807 Marikina City",
                Latitude = 14.657914,
                Longitude = 121.104240,
                Capacity = 1000,
                CurrentEvacuees = 0,
                Status = "Standby",
                HeadOfficer = "Kagawad Arnel Cruz",
                Contact = "0918-444-9120",
                Facilities = new List<string> { "Medical Station", "Clean Water", "Restrooms" },
                FacilityImageUrl = string.Empty
            },
            new EvacuationCenterItem
            {
                Name = "Nangka Elementary School",
                Barangay = "Nangka",
                Classification = "Flood-Safe Major",
                Address = "Nangka, Marikina City",
                Latitude = 14.672991,
                Longitude = 121.108440,
                Capacity = 1500,
                CurrentEvacuees = 0,
                Status = "Standby",
                HeadOfficer = "Kagawad Manuel Reyes",
                Contact = "0920-333-8101",
                Facilities = new List<string> { "Clean Water", "Generator", "Kitchen Area", "Child-Friendly Space" },
                FacilityImageUrl = string.Empty
            },
            new EvacuationCenterItem
            {
                Name = "Concepcion Elementary School",
                Barangay = "Concepcion Uno",
                Classification = "Flood-Safe Major",
                Address = "Concepcion Uno, Marikina City",
                Latitude = 14.647648,
                Longitude = 121.103974,
                Capacity = 1100,
                CurrentEvacuees = 0,
                Status = "Standby",
                HeadOfficer = "Officer Gabriel Fernandez",
                Contact = "0917-222-3456",
                Facilities = new List<string> { "Generator", "Restrooms", "Clean Water" },
                FacilityImageUrl = string.Empty
            },
            new EvacuationCenterItem
            {
                Name = "Sto. Niño Elementary School",
                Barangay = "Sto. Niño",
                Classification = "Flood-Safe Major",
                Address = "Sto. Niño, Marikina City",
                Latitude = 14.638324,
                Longitude = 121.098368,
                Capacity = 1300,
                CurrentEvacuees = 0,
                Status = "Standby",
                HeadOfficer = "Maria Gonzales (MCDRRMO)",
                Contact = "0915-222-7711",
                Facilities = new List<string> { "Generator", "Restrooms", "Parking", "Medical Hub" },
                FacilityImageUrl = string.Empty
            },
            new EvacuationCenterItem
            {
                Name = "St. Mary Elem. School",
                Barangay = "Parang",
                Classification = "Flood-Safe Minor",
                Address = "Parang, Marikina City",
                Latitude = 14.668643,
                Longitude = 121.113418,
                Capacity = 600,
                CurrentEvacuees = 0,
                Status = "Standby",
                HeadOfficer = "Officer Laura Reyes",
                Contact = "0918-222-1100",
                Facilities = new List<string> { "Clean Water", "Restrooms" },
                FacilityImageUrl = string.Empty
            },
            new EvacuationCenterItem
            {
                Name = "Concepcion Integrated School ES",
                Barangay = "Concepcion Uno",
                Classification = "Dual-Purpose Major",
                Address = "Concepcion Uno, Marikina City",
                Latitude = 14.649954,
                Longitude = 121.101893,
                Capacity = 1800,
                CurrentEvacuees = 0,
                Status = "Standby",
                HeadOfficer = "MCDRRMO Campus Lead",
                Contact = "0915-999-0011",
                Facilities = new List<string> { "Dual-Purpose Fields", "Medical Hub", "Generator", "Clean Water" },
                FacilityImageUrl = string.Empty
            },
            new EvacuationCenterItem
            {
                Name = "Fortune Elem. School Fields",
                Barangay = "Fortune",
                Classification = "Dual-Purpose Minor",
                Address = "Fortune, Marikina City",
                Latitude = 14.655000,
                Longitude = 121.115000,
                Capacity = 700,
                CurrentEvacuees = 0,
                Status = "Standby",
                HeadOfficer = "Officer Sandra Lopez",
                Contact = "0918-222-5566",
                Facilities = new List<string> { "Open Grounds", "Restrooms" },
                FacilityImageUrl = string.Empty
            },
            new EvacuationCenterItem
            {
                Name = "San Roque High School",
                Barangay = "San Roque",
                Classification = "Earthquake-Safe Minor",
                Address = "Nicanor Roxas St., San Roque, 1801 Marikina City",
                Latitude = 14.622798,
                Longitude = 121.097105,
                Capacity = 500,
                CurrentEvacuees = 0,
                Status = "Standby",
                HeadOfficer = "Capt. Danilo Reyes",
                Contact = "0920-111-8899",
                Facilities = new List<string> { "Clean Water", "Restrooms", "Open Courtyard" },
                FacilityImageUrl = string.Empty
            }
        };
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
        if (IsOfflineData)
        {
            OfflineStatusTitle = "Offline Mode — Locally Saved Data";
            if (LastSuccessfulUpdate.HasValue)
            {
                var localTime = LastSuccessfulUpdate.Value.ToLocalTime();
                OfflineStatusDescription = $"Last updated: {FormatTimestamp(localTime)}";
                ShelterSectionSubtitle = $"Offline (Saved {FormatTimestamp(localTime)})";
            }
            else
            {
                OfflineStatusDescription = "Viewing saved emergency shelter data";
                ShelterSectionSubtitle = "Nearest shelter first • Offline Mode";
            }
        }
        else
        {
            OfflineStatusTitle = string.Empty;
            if (LastSuccessfulUpdate.HasValue)
            {
                var localTime = LastSuccessfulUpdate.Value.ToLocalTime();
                OfflineStatusDescription = $"Live Data • Updated {FormatTimestamp(localTime)}";
                ShelterSectionSubtitle = $"Updated {FormatTimestamp(localTime)} • Nearest first";
            }
            else
            {
                OfflineStatusDescription = "Live Data • Connected";
                ShelterSectionSubtitle = "Nearest shelter is listed first";
            }
        }
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

    private static EvacuationCenterItem CreateItemFromCached(CachedEvacuationCenterItem cached, Location userLoc)
    {
        var item = new EvacuationCenterItem
        {
            Id = cached.Id,
            Name = cached.Name,
            Barangay = cached.Barangay,
            Classification = string.IsNullOrWhiteSpace(cached.Classification) ? "Flood-Safe Major" : cached.Classification,
            Address = cached.Address,
            VerifiedBy = string.IsNullOrWhiteSpace(cached.VerifiedBy) ? "Marikina LGU" : cached.VerifiedBy,
            Latitude = cached.Latitude,
            Longitude = cached.Longitude,
            Capacity = cached.Capacity > 0 ? cached.Capacity : 500,
            CurrentEvacuees = cached.CurrentEvacuees,
            Status = string.IsNullOrWhiteSpace(cached.Status) ? "Standby" : cached.Status,
            HeadOfficer = string.IsNullOrWhiteSpace(cached.HeadOfficer) ? "Unassigned" : cached.HeadOfficer,
            Contact = string.IsNullOrWhiteSpace(cached.Contact) ? "N/A" : cached.Contact,
            Facilities = cached.Facilities ?? new List<string>(),
            FacilityImageUrl = cached.FacilityImageUrl ?? string.Empty,
            MapImageSource = string.IsNullOrWhiteSpace(cached.MapImageSource)
                ? $"https://staticmap.openstreetmap.de/staticmap.php?center={cached.Latitude},{cached.Longitude}&zoom=16&size=600x300&markers={cached.Latitude},{cached.Longitude},red-pushpin"
                : cached.MapImageSource
        };

        item.LoadBookmarkState();

        if (item.Latitude != 0 && item.Longitude != 0)
        {
            var centerLoc = new Location(item.Latitude, item.Longitude);
            double distKm = Location.CalculateDistance(userLoc, centerLoc, DistanceUnits.Kilometers);
            item.DistanceKm = distKm;
            item.Distance = distKm < 1.0 ? $"{Math.Round(distKm * 1000)} meters away" : $"{distKm:F1} km away";
            int walkMins = (int)Math.Max(1, Math.Round(distKm * 13.75));
            item.DetailedDistanceString = $"{(int)Math.Round(distKm * 1000)} meters ({walkMins} mins walk)";
        }

        return item;
    }

    private static CachedEvacuationCenterItem CreateCachedFromItem(EvacuationCenterItem item)
    {
        return new CachedEvacuationCenterItem
        {
            Id = item.Id,
            Name = item.Name,
            Barangay = item.Barangay,
            Classification = item.Classification,
            Address = item.Address,
            VerifiedBy = item.VerifiedBy,
            FacilityImageUrl = item.FacilityImageUrl,
            MapImageSource = item.MapImageSource,
            Latitude = item.Latitude,
            Longitude = item.Longitude,
            Capacity = item.Capacity,
            CurrentEvacuees = item.CurrentEvacuees,
            Status = item.Status,
            HeadOfficer = item.HeadOfficer,
            Contact = item.Contact,
            Facilities = item.Facilities?.ToList() ?? new List<string>()
        };
    }

    private void RecalculateDistances(double userLat, double userLng)
    {
        var userLoc = new Location(userLat, userLng);
        foreach (var center in _masterCentersList)
        {
            if (center.Latitude != 0 && center.Longitude != 0)
            {
                var centerLoc = new Location(center.Latitude, center.Longitude);
                double distKm = Location.CalculateDistance(userLoc, centerLoc, DistanceUnits.Kilometers);
                center.DistanceKm = distKm;
                center.Distance = distKm < 1.0 ? $"{Math.Round(distKm * 1000)} meters away" : $"{distKm:F1} km away";
                int walkMins = (int)Math.Max(1, Math.Round(distKm * 13.75));
                center.DetailedDistanceString = $"{(int)Math.Round(distKm * 1000)} meters ({walkMins} mins walk)";
            }
        }
        ApplyFilterAndSorting();
    }

    private void LoadData()
    {
        var cached = CacheService.LoadEvacuationCenters();
        if (cached != null && cached.Centers != null && cached.Centers.Count > 0)
        {
            var userLoc = new Location(_userLat, _userLng);
            _masterCentersList.Clear();
            foreach (var c in cached.Centers)
            {
                _masterCentersList.Add(CreateItemFromCached(c, userLoc));
            }

            if (cached.Hotlines != null && cached.Hotlines.Count > 0)
            {
                Hotlines.Clear();
                foreach (var h in cached.Hotlines)
                {
                    Hotlines.Add(new EmergencyHotlineItem
                    {
                        Name = h.Name,
                        Number = h.Number,
                        Type = h.Type,
                        BadgeText = h.BadgeText
                    });
                }
            }
            else
            {
                LoadDefaultHotlines();
            }

            LastSuccessfulUpdate = cached.LastUpdatedUtc;
            IsOfflineData = !IsConnectedToInternet();
            UpdateOfflineStatusInfo();
            ApplyFilterAndSorting();
        }
        else
        {
            LoadDefaultHotlines();
            _masterCentersList.Clear();
            _masterCentersList.AddRange(GetOfficialMarikinaCenters());
            LastSuccessfulUpdate = CacheService.GetLastUpdatedTime();
            IsOfflineData = !IsConnectedToInternet();
            UpdateOfflineStatusInfo();
            ApplyFilterAndSorting();
        }
    }

    public void OnAppearing()
    {
        if (!_isConnectivitySubscribed)
        {
            try
            {
                Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
                _isConnectivitySubscribed = true;
            }
            catch { }
        }

        if (IsConnectedToInternet())
        {
            _ = RefreshDataAsync();
        }
        else
        {
            IsOfflineData = true;
            UpdateOfflineStatusInfo();
        }
    }

    public void OnDisappearing()
    {
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
                IsOfflineData = true;
                UpdateOfflineStatusInfo();
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
        if (!IsConnectedToInternet())
        {
            IsOfflineData = true;
            UpdateOfflineStatusInfo();
            RecalculateDistances(_userLat, _userLng);
            return;
        }

        await Task.WhenAll(
            FetchHotlinesFromSupabaseAsync(),
            FetchEvacuationCentersFromSupabaseAsync(_userLat, _userLng)
        );
    }

    public async Task FetchEvacuationCentersFromSupabaseAsync(double userLat = 14.6612, double userLng = 121.0963)
    {
        _userLat = userLat;
        _userLng = userLng;

        if (!IsConnectedToInternet())
        {
            IsOfflineData = true;
            UpdateOfflineStatusInfo();
            RecalculateDistances(_userLat, _userLng);
            return;
        }

        try
        {
            var client = await SupabaseService.Instance.GetClientAsync();
            if (client != null)
            {
                var response = await client.From<SupabaseEvacuationCenter>()
                    .Order("name", Supabase.Postgrest.Constants.Ordering.Ascending)
                    .Get();

                if (response?.Models != null && response.Models.Count > 0)
                {
#if ANDROID
                    Android.Util.Log.Info(MldLogTag, $"Fetched {response.Models.Count} evacuation centers from Supabase.");
#endif
                    var userLoc = new Location(userLat, userLng);
                    var updatedList = new List<EvacuationCenterItem>();

                    foreach (var model in response.Models)
                    {
                        double.TryParse(model.Latitude, out var lat);
                        double.TryParse(model.Longitude, out var lng);

                        if (lat == 0 && lng == 0)
                        {
                            lat = 14.6502;
                            lng = 121.0944;
                        }

                        var facilitiesList = ParseFacilities(model.Facilities);
                        string resolvedImage = (model.ImageUrl ?? string.Empty).Trim();

                        var item = new EvacuationCenterItem
                        {
                            Id = model.Id ?? string.Empty,
                            Name = model.Name ?? string.Empty,
                            Barangay = model.Barangay ?? string.Empty,
                            Classification = string.IsNullOrWhiteSpace(model.Classification) ? "Flood-Safe Major" : model.Classification,
                            Address = string.IsNullOrWhiteSpace(model.Barangay) ? "Marikina City" : $"{model.Barangay}, Marikina City",
                            VerifiedBy = "Marikina LGU",
                            Latitude = lat,
                            Longitude = lng,
                            Capacity = model.Capacity > 0 ? model.Capacity : 500,
                            CurrentEvacuees = model.CurrentEvacuees,
                            Status = string.IsNullOrWhiteSpace(model.Status) ? "Standby" : model.Status,
                            HeadOfficer = string.IsNullOrWhiteSpace(model.HeadOfficer) ? "Unassigned" : model.HeadOfficer,
                            Contact = string.IsNullOrWhiteSpace(model.Contact) ? "N/A" : model.Contact,
                            Facilities = facilitiesList,
                            FacilityImageUrl = resolvedImage,
                            MapImageSource = $"https://staticmap.openstreetmap.de/staticmap.php?center={lat},{lng}&zoom=16&size=600x300&markers={lat},{lng},red-pushpin"
                        };

                        item.LoadBookmarkState();

                        if (lat != 0 && lng != 0)
                        {
                            var centerLoc = new Location(lat, lng);
                            double distKm = Location.CalculateDistance(userLoc, centerLoc, DistanceUnits.Kilometers);
                            item.DistanceKm = distKm;
                            item.Distance = distKm < 1.0 ? $"{Math.Round(distKm * 1000)} meters away" : $"{distKm:F1} km away";
                            int walkMins = (int)Math.Max(1, Math.Round(distKm * 13.75));
                            item.DetailedDistanceString = $"{(int)Math.Round(distKm * 1000)} meters ({walkMins} mins walk)";
                        }

                        updatedList.Add(item);
                    }

                    // Save latest successfully loaded data to local storage (Requirements 1, 3, 4)
                    var now = DateTimeOffset.UtcNow;
                    var cachedItems = updatedList.Select(CreateCachedFromItem).ToList();
                    var cachedHotlines = Hotlines.Select(h => new CachedEmergencyHotlineItem
                    {
                        Name = h.Name,
                        Number = h.Number,
                        Type = h.Type,
                        BadgeText = h.BadgeText
                    }).ToList();

                    CacheService.SaveEvacuationCenters(cachedItems, cachedHotlines, now);

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _masterCentersList.Clear();
                        _masterCentersList.AddRange(updatedList);
                        LastSuccessfulUpdate = now;
                        IsOfflineData = false;
                        UpdateOfflineStatusInfo();
                        ApplyFilterAndSorting();
                    });
                }
            }
        }
        catch (Exception ex)
        {
#if ANDROID
            Android.Util.Log.Error(MldLogTag, $"Failed to fetch live evacuation centers from Supabase: {ex.Message}");
#else
            System.Diagnostics.Debug.WriteLine($"Failed to fetch live evacuation centers from Supabase: {ex.Message}");
#endif
            // If online request fails, do NOT overwrite existing saved data with empty/incomplete results (Requirement 4)
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsOfflineData = true;
                UpdateOfflineStatusInfo();
                if (_masterCentersList.Count == 0)
                {
                    LoadData();
                }
            });
        }
    }

    private static List<string> ParseFacilities(object? rawFacilities)
    {
        if (rawFacilities == null) return new List<string> { "Clean Water", "Restrooms" };

        string str = rawFacilities.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(str)) return new List<string> { "Clean Water", "Restrooms" };

        try
        {
            if (str.Trim().StartsWith("["))
            {
                var list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(str);
                if (list != null && list.Count > 0) return list;
            }
        }
        catch { }

        return str.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                  .Select(s => s.Trim())
                  .Where(s => !string.IsNullOrWhiteSpace(s))
                  .ToList();
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
                    MainThread.BeginInvokeOnMainThread(() =>
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
        try
        {
            var location = await Geolocation.Default.GetLastKnownLocationAsync();
            if (location != null)
            {
                _userLat = location.Latitude;
                _userLng = location.Longitude;
                RecalculateDistances(_userLat, _userLng);
                _ = FetchEvacuationCentersFromSupabaseAsync(_userLat, _userLng);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var freshLoc = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Low, TimeSpan.FromSeconds(1)));
                    if (freshLoc != null)
                    {
                        _userLat = freshLoc.Latitude;
                        _userLng = freshLoc.Longitude;
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            RecalculateDistances(_userLat, _userLng);
                            _ = FetchEvacuationCentersFromSupabaseAsync(_userLat, _userLng);
                        });
                    }
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GPS location error: {ex.Message}");
        }
    }

    private void ApplyFilterAndSorting()
    {
        IEnumerable<EvacuationCenterItem> filtered = _masterCentersList;

        string activeFilter = !string.IsNullOrWhiteSpace(SelectedCategoryDropdown) ? SelectedCategoryDropdown : SelectedFilter;

        if (string.Equals(activeFilter, "Within 5 km", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(activeFilter, "Within 5 km radius", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(c => c.DistanceKm <= 5.0);
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

        // ALWAYS sort by distance ascending so the VERY FIRST ITEM is the NEAREST evacuation center!
        var sortedList = filtered.OrderBy(c => c.DistanceKm).ToList();

        // Mark first element as Nearest Shelter
        for (int i = 0; i < sortedList.Count; i++)
        {
            sortedList[i].IsNearestShelter = (i == 0);
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

    private async Task StartNavigationToCenterAsync(EvacuationCenterItem center, bool closeDetailsPopup)
    {
        if (IsStartingNavigation) return;
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

            bool opened = await CameraNavigationLauncher.OpenAsync(
                center.Name,
                center.Latitude,
                center.Longitude);

            if (!opened && Shell.Current is not null)
            {
                await Shell.Current.DisplayAlert(
                    "AR Navigation",
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
                    "AR Navigation",
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
    private async Task NavigateToMapAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("//Map");
        }
    }
}
