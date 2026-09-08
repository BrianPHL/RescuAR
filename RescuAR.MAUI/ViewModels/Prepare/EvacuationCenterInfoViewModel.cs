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

public partial class EvacuationCenterItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Distance { get; set; } = string.Empty;
    public double DistanceKm { get; set; }
    public string DetailedDistanceString { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string VerifiedBy { get; set; } = string.Empty;
    public string FacilityImageUrl { get; set; } = string.Empty;
    public string MapImageSource { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BookmarkFill))]
    [NotifyPropertyChangedFor(nameof(BookmarkStroke))]
    private bool _isBookmarked;

    public string BookmarkFill => IsBookmarked ? "#007E8A" : "Transparent";
    public string BookmarkStroke => IsBookmarked ? "#007E8A" : "#0F172A";

    public bool HasFacilityImage => !string.IsNullOrWhiteSpace(FacilityImageUrl);

    public string Status { get; set; } = "Open";
    public string StatusPillBg => "#DCFCE7";
    public string StatusPillText => "#16A34A";
    public string StatusLabel => "Open / Operational";

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

public partial class EvacuationCenterInfoViewModel : ObservableObject
{
    private const string MldLogTag = "RescuAR-MLD";

    public ObservableCollection<EmergencyHotlineItem> Hotlines { get; } = new();
    public ObservableCollection<EvacuationCenterItem> EvacuationCenters { get; } = new();

    [ObservableProperty]
    private string _selectedFilter = "Nearest";

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
        LoadData();
        _ = FetchHotlinesFromSupabaseAsync();
        _ = FilterEvacuationCentersByGpsAsync();
    }

    private void LoadData()
    {
        Hotlines.Clear();
        // Fallback default emergency hotlines
        Hotlines.Add(new EmergencyHotlineItem { Name = "Marikina Rescue 161", Number = "(02) 161", Type = "24/7 Emergency Medical & Rescue", BadgeText = "EMS" });
        Hotlines.Add(new EmergencyHotlineItem { Name = "Marikina PNP Central", Number = "(02) 8405-0091", Type = "Police Emergency Hotline", BadgeText = "PNP" });
        Hotlines.Add(new EmergencyHotlineItem { Name = "Marikina BFP Fire Dept", Number = "(02) 8646-0427", Type = "Fire & Rescue Brigade", BadgeText = "BFP" });
        Hotlines.Add(new EmergencyHotlineItem { Name = "Red Cross Marikina", Number = "(02) 8681-3442", Type = "Disaster Relief & Blood Bank", BadgeText = "PRC" });

        // Load all available candidate centers
        PopulateMasterCenters(14.6612, 121.0963);
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
                PopulateMasterCenters(location.Latitude, location.Longitude);
            }

            // Perform high accuracy GPS fix in background without delaying page navigation
            _ = Task.Run(async () =>
            {
                try
                {
                    var freshLoc = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Low, TimeSpan.FromSeconds(1)));
                    if (freshLoc != null)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            PopulateMasterCenters(freshLoc.Latitude, freshLoc.Longitude);
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

    private void PopulateMasterCenters(double userLat, double userLng)
    {
        var masterList = new List<EvacuationCenterItem>
        {
            new EvacuationCenterItem 
            { 
                Name = "Malanday Elementary School", 
                Address = "48 Visayas St., Malanday, 1805 Marikina City, Philippines", 
                VerifiedBy = "Marikina LGU",
                Latitude = 14.6612,
                Longitude = 121.0963,
                FacilityImageUrl = "https://pbs.twimg.com/media/Emm23rQVQAAbUe3?format=jpg&name=large",
                MapImageSource = "https://staticmap.openstreetmap.de/staticmap.php?center=14.6612,121.0963&zoom=16&size=600x300&markers=14.6612,121.0963,red-pushpin"
            },
            new EvacuationCenterItem 
            { 
                Name = "San Roque High School", 
                Address = "Abad Santos Street, San Roque, 1801 Marikina City, Philippines", 
                VerifiedBy = "Marikina LGU",
                Latitude = 14.6258,
                Longitude = 121.1042,
                FacilityImageUrl = "https://www.airesingegneria.it/site/assets/files/1208/metro-manila-edifici.jpg",
                MapImageSource = "https://staticmap.openstreetmap.de/staticmap.php?center=14.6258,121.1042&zoom=16&size=600x300&markers=14.6258,121.1042,red-pushpin"
            },
            new EvacuationCenterItem 
            { 
                Name = "Concepcion Uno Covered Court", 
                Address = "J.P. Rizal St., Concepcion Uno, 1807 Marikina City, Philippines", 
                VerifiedBy = "Red Cross PH Verified",
                Latitude = 14.6521,
                Longitude = 121.1084,
                FacilityImageUrl = "https://upload.wikimedia.org/wikipedia/commons/thumb/d/d5/Barangay_Concepcion_Uno%2C_Marikina_City_%28Rizal%2C_Metro_Manila%3B_2023-08-07%29_E911a_22.jpg/3840px-Barangay_Concepcion_Uno%2C_Marikina_City_%28Rizal%2C_Metro_Manila%3B_2023-08-07%29_E911a_22.jpg",
                MapImageSource = "https://staticmap.openstreetmap.de/staticmap.php?center=14.6521,121.1084&zoom=16&size=600x300&markers=14.6521,121.1084,red-pushpin"
            },
            new EvacuationCenterItem 
            { 
                Name = "Marikina Elementary School", 
                Address = "W.C. Paz St., Sta. Elena, 1800 Marikina City, Philippines", 
                VerifiedBy = "Marikina LGU",
                Latitude = 14.6335,
                Longitude = 121.0968,
                FacilityImageUrl = "https://blogger.googleusercontent.com/img/b/R29vZ2xl/AVvXsEgfm1_L35DOsZzEP6Op7KXLa44OxSrijBZ3zuIF4bczTgTQvA4c2GWNhzxlmy1UqFaZz47_IyXrAWuM6zZv8CDTR7ZwVITldWURKjINOxGi94kvfhRuN5mXYWT3geYrG3KJmemaYDL7hKc/w1200-h630-p-k-no-nu/2018-02-25_05.54.17_1%255B1%255D.jpg",
                MapImageSource = "https://staticmap.openstreetmap.de/staticmap.php?center=14.6335,121.0968&zoom=16&size=600x300&markers=14.6335,121.0968,red-pushpin"
            }
        };

        var userLoc = new Location(userLat, userLng);

        foreach (var center in masterList)
        {
            center.LoadBookmarkState();

            var centerLoc = new Location(center.Latitude, center.Longitude);
            double distKm = Location.CalculateDistance(userLoc, centerLoc, DistanceUnits.Kilometers);
            center.DistanceKm = distKm;
            center.Distance = distKm < 1.0 ? $"{Math.Round(distKm * 1000)} meters away" : $"{distKm:F1} km away";
            
            int walkMins = (int)Math.Max(1, Math.Round(distKm * 13.75));
            center.DetailedDistanceString = $"{(int)Math.Round(distKm * 1000)} meters ({walkMins} mins walk)";
        }

        // Filter strictly for nearby evacuation centers (within 2.5 km of user's GPS location) and sort by distance
        var nearbyCenters = masterList
            .Where(c => c.DistanceKm <= 2.5)
            .OrderBy(c => c.DistanceKm)
            .ToList();

        // Fallback: If no center is within 2.5 km, show top 2 closest centers
        if (nearbyCenters.Count == 0)
        {
            nearbyCenters = masterList.OrderBy(c => c.DistanceKm).Take(2).ToList();
        }

        EvacuationCenters.Clear();
        foreach (var item in nearbyCenters)
        {
            EvacuationCenters.Add(item);
        }
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
            if (Application.Current?.Windows.Count > 0 && Application.Current.Windows[0].Page?.Navigation != null)
            {
                await Application.Current.Windows[0].Page.Navigation.PushAsync(new Views.Prepare.PASSPage());
            }
        }
    }

    [RelayCommand]
    private async Task MakePhoneCall(string number)
    {
        if (string.IsNullOrWhiteSpace(number)) return;

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
        // A shelter-card action supplies the current center as a command
        // parameter. In that case this is real evacuation guidance and the
        // destination must be published before Camera opens.
        if (center is not null)
        {
            await StartNavigationToCenterAsync(center, closeDetailsPopup: false);
            return;
        }

        // The standalone AR Route Practice tool intentionally has no shelter
        // destination, so preserve its existing Camera-only behavior.
        if (Shell.Current is not null)
        {
            await Shell.Current.GoToAsync("//Camera");
        }
    }

    [RelayCommand]
    private async Task StartARNavigationAsync()
    {
        var center = SelectedCenter;

        if (center is null)
        {
            return;
        }

        await StartNavigationToCenterAsync(center, closeDetailsPopup: true);
    }

    private async Task StartNavigationToCenterAsync(
        EvacuationCenterItem center,
        bool closeDetailsPopup)
    {
        if (IsStartingNavigation)
        {
            return;
        }

        IsStartingNavigation = true;

#if ANDROID
        Log.Debug(
            MldLogTag,
            "Evacuation-center AR navigation selected: " +
            $"name='{center.Name}', " +
            $"lat={center.Latitude:F7}, " +
            $"lon={center.Longitude:F7}");
#endif

        try
        {
            // Opening the Camera tab directly is not sufficient. The Camera
            // page only requests MLD routing after a verified destination has
            // been published through NavigationDestinationBridge, which the
            // established launcher performs for us.
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
            Log.Error(
                MldLogTag,
                $"Starting AR navigation failed: {exception}");
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
