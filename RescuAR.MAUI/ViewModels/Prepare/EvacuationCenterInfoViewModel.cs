#if ANDROID
using Android.Util;
#endif

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RescuAR.MAUI.Services.Navigation;

namespace RescuAR.App.ViewModels.Prepare;

public class EmergencyHotlineItem
{
    public string Name { get; set; } =
        string.Empty;

    public string Number { get; set; } =
        string.Empty;

    public string Type { get; set; } =
        string.Empty;

    public string BadgeText { get; set; } =
        "EMS";

    public string IconEmoji { get; set; } =
        "🚑";

    public string IconBg { get; set; } =
        "#FEE2E2";
}

public partial class EvacuationCenterItem : ObservableObject
{
    public string Name { get; set; } =
        string.Empty;

    public string Distance { get; set; } =
        string.Empty;

    public string Address { get; set; } =
        string.Empty;

    public string VerifiedBy { get; set; } =
        string.Empty;

    public string MapImageSource { get; set; } =
        string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CapacityText))]
    [NotifyPropertyChangedFor(nameof(ProgressValue))]
    [NotifyPropertyChangedFor(nameof(StatusPillBg))]
    [NotifyPropertyChangedFor(nameof(StatusPillText))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private int currentOccupancy;

    public int MaxCapacity { get; set; } =
        500;

    public string Status { get; set; } =
        "Open";

    public string CapacityText =>
        $"{CurrentOccupancy} / {MaxCapacity} evacuees";

    public double ProgressValue =>
        MaxCapacity <=
            0
            ? 0.0
            : Math.Clamp(
                (double)CurrentOccupancy /
                MaxCapacity,
                0.0,
                1.0);

    public string StatusPillBg =>
        ProgressValue switch
        {
            >= 0.9 => "#FEE2E2",
            >= 0.7 => "#FEF3C7",
            _ => "#DCFCE7"
        };

    public string StatusPillText =>
        ProgressValue switch
        {
            >= 0.9 => "#DC2626",
            >= 0.7 => "#D97706",
            _ => "#16A34A"
        };

    public string StatusLabel =>
        ProgressValue switch
        {
            >= 0.9 => "Near Full",
            >= 0.7 => "Moderate",
            _ => "Open / Space Available"
        };
}

public partial class EvacuationCenterInfoViewModel : ObservableObject
{
    private const string MldLogTag =
        "RescuAR-MLD";

    public ObservableCollection<EmergencyHotlineItem> Hotlines { get; } =
        new();

    public ObservableCollection<EvacuationCenterItem> EvacuationCenters { get; } =
        new();

    [ObservableProperty]
    private string selectedFilter =
        "Nearest";

    [ObservableProperty]
    private bool isStartingNavigation;

    public EvacuationCenterInfoViewModel()
    {
        LoadData();
    }

    private void LoadData()
    {
        Hotlines.Clear();

        Hotlines.Add(
            new EmergencyHotlineItem
            {
                Name =
                    "Marikina Rescue 161",
                Number =
                    "(02) 161",
                Type =
                    "24/7 Emergency Medical & Rescue",
                BadgeText =
                    "EMS",
                IconEmoji =
                    "🚑",
                IconBg =
                    "#FEE2E2"
            });

        Hotlines.Add(
            new EmergencyHotlineItem
            {
                Name =
                    "Marikina PNP Central",
                Number =
                    "(02) 8405-0091",
                Type =
                    "Police Emergency Hotline",
                BadgeText =
                    "PNP",
                IconEmoji =
                    "🚓",
                IconBg =
                    "#DBEAFE"
            });

        Hotlines.Add(
            new EmergencyHotlineItem
            {
                Name =
                    "Marikina BFP Fire Dept",
                Number =
                    "(02) 8646-0427",
                Type =
                    "Fire & Rescue Brigade",
                BadgeText =
                    "BFP",
                IconEmoji =
                    "🚒",
                IconBg =
                    "#FFEDD5"
            });

        Hotlines.Add(
            new EmergencyHotlineItem
            {
                Name =
                    "Red Cross Marikina",
                Number =
                    "(02) 8681-3442",
                Type =
                    "Disaster Relief & Blood Bank",
                BadgeText =
                    "PRC",
                IconEmoji =
                    "🏥",
                IconBg =
                    "#FFE4E6"
            });

        EvacuationCenters.Clear();

        EvacuationCenters.Add(
            new EvacuationCenterItem
            {
                Name =
                    "Malanday Elementary School",
                Distance =
                    "877 meters away",
                Address =
                    "48 Visayas St., Malanday\nMarikina City 1805",
                VerifiedBy =
                    "Marikina LGU",
                CurrentOccupancy =
                    210,
                MaxCapacity =
                    500,
                Latitude =
                    14.6612,
                Longitude =
                    121.0963,
                MapImageSource =
                    "https://staticmap.openstreetmap.de/staticmap.php?center=14.6612,121.0963&zoom=16&size=600x300&markers=14.6612,121.0963,red-pushpin"
            });

        EvacuationCenters.Add(
            new EvacuationCenterItem
            {
                Name =
                    "San Roque High School Evacuation Facility",
                Distance =
                    "1.2 km away",
                Address =
                    "Abad Santos St., San Roque\nMarikina City 1801",
                VerifiedBy =
                    "Marikina LGU",
                CurrentOccupancy =
                    380,
                MaxCapacity =
                    450,
                Latitude =
                    14.6258,
                Longitude =
                    121.1042,
                MapImageSource =
                    "https://staticmap.openstreetmap.de/staticmap.php?center=14.6258,121.1042&zoom=16&size=600x300&markers=14.6258,121.1042,red-pushpin"
            });

        EvacuationCenters.Add(
            new EvacuationCenterItem
            {
                Name =
                    "Concepcion Uno Covered Court",
                Distance =
                    "2.4 km away",
                Address =
                    "J.P. Rizal St., Concepcion Uno\nMarikina City 1807",
                VerifiedBy =
                    "Red Cross PH Verified",
                CurrentOccupancy =
                    120,
                MaxCapacity =
                    300,
                Latitude =
                    14.6521,
                Longitude =
                    121.1084,
                MapImageSource =
                    "https://staticmap.openstreetmap.de/staticmap.php?center=14.6521,121.1084&zoom=16&size=600x300&markers=14.6521,121.1084,red-pushpin"
            });
    }

    [RelayCommand]
    private async Task StartArNavigationAsync(
        EvacuationCenterItem center)
    {
        if (center is null ||
            IsStartingNavigation)
        {
            return;
        }

        IsStartingNavigation =
            true;

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
            bool opened =
                await CameraNavigationLauncher.OpenAsync(
                    center.Name,
                    center.Latitude,
                    center.Longitude);

            if (!opened &&
                Shell.Current is not null)
            {
                await Shell.Current.DisplayAlert(
                    "AR Navigation",
                    "This evacuation center could not be used as a navigation destination. Check Logcat for RescuAR-MLD.",
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
            IsStartingNavigation =
                false;
        }
    }

    [RelayCommand]
    private async Task DialNumberAsync(
        string number)
    {
        if (Shell.Current is not null)
        {
            await Shell.Current.DisplayAlert(
                "Emergency Hotline",
                $"Initiating call to {number}...",
                "Call Now");
        }
    }

    [RelayCommand]
    private async Task CheckInEvacueeAsync(
        EvacuationCenterItem center)
    {
        if (center is null)
        {
            return;
        }

        if (center.CurrentOccupancy <
            center.MaxCapacity)
        {
            center.CurrentOccupancy +=
                1;

            if (Shell.Current is not null)
            {
                await Shell.Current.DisplayAlert(
                    "Real-Time Check-In",
                    $"You have successfully checked in at {center.Name}. Real-time capacity updated!",
                    "OK");
            }
        }
        else if (Shell.Current is not null)
        {
            await Shell.Current.DisplayAlert(
                "Center Full",
                $"{center.Name} has reached max capacity! Please check nearby centers.",
                "OK");
        }
    }

    [RelayCommand]
    private async Task ViewMoreDetailsAsync(
        EvacuationCenterItem center)
    {
        if (center is null)
        {
            return;
        }

        if (Shell.Current is not null)
        {
            await Shell.Current.GoToAsync(
                "//Map");
        }
    }

    [RelayCommand]
    private async Task NavigateToMapAsync()
    {
        if (Shell.Current is not null)
        {
            await Shell.Current.GoToAsync(
                "//Map");
        }
    }
}
