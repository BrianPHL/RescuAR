using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace RescuAR.App.ViewModels.Prepare;

public partial class FloodHistoryViewModel : ObservableObject
{
    [RelayCommand]
    private async Task BackAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("..");
        }
    }

    [RelayCommand]
    private async Task StartLearningAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("Prepare/Assessment");
        }
    }

    [RelayCommand]
    private async Task ViewCategoryAsync(string categoryName)
    {
        if (Shell.Current == null) return;

        switch (categoryName)
        {
            case "Historical Photos":
                await Shell.Current.GoToAsync("Prepare/HistoricalPhotos");
                break;

            case "Documentary Videos":
                await Shell.Current.GoToAsync("Prepare/DocumentaryVideos");
                break;

            case "News Archive":
                string newsChoice = await Shell.Current.DisplayActionSheet(
                    "📰 Official News & Research Archive",
                    "Close",
                    null,
                    "Philippine News Agency: Ondoy & Ulysses Analysis",
                    "DOST PJS: Marikina Flood Hazard Models",
                    "PIDS SERP-P: Barangay Tumana EWS Study",
                    "Marikina City Official Portal");

                if (newsChoice == "Philippine News Agency: Ondoy & Ulysses Analysis")
                {
                    await Launcher.Default.OpenAsync(new Uri("https://www.pna.gov.ph/articles/1123381"));
                }
                else if (newsChoice == "DOST PJS: Marikina Flood Hazard Models")
                {
                    await Launcher.Default.OpenAsync(new Uri("https://philjournalsci.dost.gov.ph/"));
                }
                else if (newsChoice == "Marikina City Official Portal")
                {
                    await Launcher.Default.OpenAsync(new Uri("https://www.marikina.gov.ph/our-city"));
                }
                break;

            case "Flood Timeline":
                await Shell.Current.GoToAsync("Prepare/FloodTimeline");
                break;
        }
    }

    [RelayCommand]
    private async Task ViewEventDetailAsync(string eventYear)
    {
        if (Shell.Current == null) return;

        switch (eventYear)
        {
            case "1992":
                await Shell.Current.DisplayAlert(
                    "1992 Marikina Flood",
                    "Source: DOST Philippine Journal of Science (Vol. 147 No. 3)\n\n" +
                    "One of the earliest benchmark flood events recorded in modern Marikina hydrology history. " +
                    "The water levels prompted DOST and DPWH to establish initial flood control barriers and water level telemetry stations.",
                    "Close");
                break;

            case "2009":
                await Shell.Current.DisplayAlert(
                    "Typhoon Ondoy (Ketsana) Details",
                    "Source: DOST-PAGASA Pasig-Marikina River FFWS\n\n" +
                    "Peak Water Level: 23.0 meters (Record High)\n" +
                    "24-Hour Rainfall: 455 mm (exceeded 1-month average rainfall)\n\n" +
                    "The most devastating flood in Marikina's recent history, submerging over 80% of the city.",
                    "Close");
                break;

            case "2020":
                await Shell.Current.DisplayAlert(
                    "Typhoon Ulysses (Vamco) Details",
                    "Source: Philippine News Agency (PNA)\n\n" +
                    "Peak Water Level: 22.0 meters (Alarm Level 3 Evacuation)\n\n" +
                    "Affected over 40,000 residents across Barangays Tumana, Malanday, and Nangka.",
                    "Close");
                break;

            case "Recent":
                await Shell.Current.DisplayAlert(
                    "Typhoon Carina (Gaemi) – July 24, 2024",
                    "Source: Marikina City Government & PAGASA\n\n" +
                    "Peak Water Level: 20.7 meters (3rd Alarm Forced Evacuation)\n\n" +
                    "Severe monsoon rains caused rapid river rise. Timely early warning protocols ensured safe, pre-emptive evacuations across all vulnerable sectors.",
                    "Close");
                break;
        }
    }
}
