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
using Microsoft.Maui.Storage;
using RescuAR.App.Models;
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

public class EvacuationCenterCacheDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Barangay { get; set; } = string.Empty;
    public string Classification { get; set; } = "Flood-Safe Major";
    public string Address { get; set; } = string.Empty;
    public string VerifiedBy { get; set; } = "Marikina LGU";
    public string FacilityImageUrl { get; set; } = string.Empty;
    public string MapImageSource { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int Capacity { get; set; } = 500;
    public int CurrentEvacuees { get; set; } = 0;
    public string HeadOfficer { get; set; } = "Unassigned";
    public string Contact { get; set; } = "N/A";
    public List<string> Facilities { get; set; } = new();
    public string Status { get; set; } = "Standby";
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
    private const string EvacuationCentersCacheKey = "CachedEvacuationCenters_v1";

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
    private EvacuationCenterItem? _selectedCenter;

    [ObservableProperty]
    private bool _isStartingNavigation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PassScoreText))]
    private int _passScore = Preferences.Default.Get("PASS_Score", 72);

    public string PassScoreText => $"{PassScore}% prepared";

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

    private void LoadData()
    {
        Hotlines.Clear();
        Hotlines.Add(new EmergencyHotlineItem { Name = "Marikina Rescue 161", Number = "(02) 161", Type = "24/7 Emergency Medical & Rescue", BadgeText = "EMS" });
        Hotlines.Add(new EmergencyHotlineItem { Name = "Marikina PNP Central", Number = "(02) 8405-0091", Type = "Police Emergency Hotline", BadgeText = "PNP" });
        Hotlines.Add(new EmergencyHotlineItem { Name = "Marikina BFP Fire Dept", Number = "(02) 8646-0427", Type = "Fire & Rescue Brigade", BadgeText = "BFP" });
        Hotlines.Add(new EmergencyHotlineItem { Name = "Red Cross Marikina", Number = "(02) 8681-3442", Type = "Disaster Relief & Blood Bank", BadgeText = "PRC" });

        _masterCentersList.Clear();
        var cachedList = LoadCentersFromLocalCache();
        if (cachedList.Count > 0)
        {
            _masterCentersList.AddRange(cachedList);
            ApplyFilterAndSorting();
        }
    }

    private void SaveCentersToLocalCache(List<EvacuationCenterCacheDto> cacheItems)
    {
        try
        {
            if (cacheItems != null && cacheItems.Count > 0)
            {
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(cacheItems);
                Preferences.Default.Set(EvacuationCentersCacheKey, json);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save evacuation centers to local cache: {ex.Message}");
        }
    }

    private List<EvacuationCenterItem> LoadCentersFromLocalCache()
    {
        var list = new List<EvacuationCenterItem>();
        try
        {
            string json = Preferences.Default.Get(EvacuationCentersCacheKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var cacheDtos = Newtonsoft.Json.JsonConvert.DeserializeObject<List<EvacuationCenterCacheDto>>(json);
                if (cacheDtos != null && cacheDtos.Count > 0)
                {
                    var userLoc = new Location(_userLat, _userLng);
                    foreach (var dto in cacheDtos)
                    {
                        var item = new EvacuationCenterItem
                        {
                            Id = dto.Id,
                            Name = dto.Name,
                            Barangay = dto.Barangay,
                            Classification = string.IsNullOrWhiteSpace(dto.Classification) ? "Flood-Safe Major" : dto.Classification,
                            Address = string.IsNullOrWhiteSpace(dto.Address) ? (string.IsNullOrWhiteSpace(dto.Barangay) ? "Marikina City" : $"{dto.Barangay}, Marikina City") : dto.Address,
                            VerifiedBy = string.IsNullOrWhiteSpace(dto.VerifiedBy) ? "Marikina LGU" : dto.VerifiedBy,
                            FacilityImageUrl = dto.FacilityImageUrl,
                            MapImageSource = string.IsNullOrWhiteSpace(dto.MapImageSource) ? $"https://staticmap.openstreetmap.de/staticmap.php?center={dto.Latitude},{dto.Longitude}&zoom=16&size=600x300&markers={dto.Latitude},{dto.Longitude},red-pushpin" : dto.MapImageSource,
                            Latitude = dto.Latitude,
                            Longitude = dto.Longitude,
                            Capacity = dto.Capacity > 0 ? dto.Capacity : 500,
                            CurrentEvacuees = dto.CurrentEvacuees,
                            Status = string.IsNullOrWhiteSpace(dto.Status) ? "Standby" : dto.Status,
                            HeadOfficer = string.IsNullOrWhiteSpace(dto.HeadOfficer) ? "Unassigned" : dto.HeadOfficer,
                            Contact = string.IsNullOrWhiteSpace(dto.Contact) ? "N/A" : dto.Contact,
                            Facilities = dto.Facilities ?? new List<string>()
                        };

                        item.LoadBookmarkState();
                        UpdateItemDistance(item, userLoc);
                        list.Add(item);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load evacuation centers from local cache: {ex.Message}");
        }
        return list;
    }

    private static void UpdateItemDistance(EvacuationCenterItem item, Location userLoc)
    {
        var centerLoc = new Location(item.Latitude, item.Longitude);
        double distKm = Location.CalculateDistance(userLoc, centerLoc, DistanceUnits.Kilometers);
        item.DistanceKm = distKm;
        item.Distance = distKm < 1.0 ? $"{Math.Round(distKm * 1000)} meters away" : $"{distKm:F1} km away";
        int walkMins = (int)Math.Max(1, Math.Round(distKm * 13.75));
        item.DetailedDistanceString = $"{(int)Math.Round(distKm * 1000)} meters ({walkMins} mins walk)";
    }

    private void UpdateMasterListDistances()
    {
        var userLoc = new Location(_userLat, _userLng);
        foreach (var item in _masterCentersList)
        {
            UpdateItemDistance(item, userLoc);
        }
        ApplyFilterAndSorting();
    }

    public async Task FetchEvacuationCentersFromSupabaseAsync(double userLat = 14.6612, double userLng = 121.0963)
    {
        _userLat = userLat;
        _userLng = userLng;

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
                    var newList = new List<EvacuationCenterItem>();
                    var cacheList = new List<EvacuationCenterCacheDto>();

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
                        string addressStr = string.IsNullOrWhiteSpace(model.Barangay) ? "Marikina City" : $"{model.Barangay}, Marikina City";

                        var item = new EvacuationCenterItem
                        {
                            Id = model.Id ?? string.Empty,
                            Name = model.Name ?? string.Empty,
                            Barangay = model.Barangay ?? string.Empty,
                            Classification = string.IsNullOrWhiteSpace(model.Classification) ? "Flood-Safe Major" : model.Classification,
                            Address = addressStr,
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
                        UpdateItemDistance(item, userLoc);
                        newList.Add(item);

                        cacheList.Add(new EvacuationCenterCacheDto
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
                            HeadOfficer = item.HeadOfficer,
                            Contact = item.Contact,
                            Facilities = item.Facilities,
                            Status = item.Status
                        });
                    }

                    // Save latest successful online fetch to persistent local cache
                    SaveCentersToLocalCache(cacheList);

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _masterCentersList.Clear();
                        _masterCentersList.AddRange(newList);
                        ApplyFilterAndSorting();
                    });
                    return;
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
        }

        // Offline Fallback: Load last successful evacuation centers from local cache if online fetch fails
        var offlineCachedList = LoadCentersFromLocalCache();
        if (offlineCachedList.Count > 0)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _masterCentersList.Clear();
                _masterCentersList.AddRange(offlineCachedList);
                ApplyFilterAndSorting();
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
                UpdateMasterListDistances();
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
                            UpdateMasterListDistances();
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
        SelectedCenter = null;
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
