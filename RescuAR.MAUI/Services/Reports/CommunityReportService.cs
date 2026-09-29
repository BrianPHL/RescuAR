using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using RescuAR.App.Models;
using RescuAR.Services;

namespace RescuAR.App.Services.Reports;

public class CommunityReportService
{
    public ObservableCollection<CommunityReport> Reports { get; } = new();

    private async Task<Supabase.Client?> GetClientAsync()
    {
        return await SupabaseService.Instance.GetClientAsync();
    }

    /// <summary>
    /// Remote-only report fetch used by route-safety monitoring. Unlike the
    /// public feed method, this returns null when Supabase cannot be reached
    /// and never substitutes local sample rows. This prevents a fallback UI
    /// record from being interpreted as a verified live road hazard.
    /// </summary>
    public async Task<List<CommunityReport>?> TryGetRemoteReportsAsync()
    {
        var client = await GetClientAsync();

        if (client is null)
        {
            return null;
        }

        try
        {
            var response = await client.From<CommunityReport>()
                .Order("created_at", Supabase.Postgrest.Constants.Ordering.Descending)
                .Get();

            return response.Models?.ToList() ??
                new List<CommunityReport>();
        }
        catch (Exception ex)
        {
#if ANDROID
            Android.Util.Log.Warn(
                "RescuAR-HazardReroute",
                $"Remote community-hazard fetch unavailable: {ex.Message}");
#else
            System.Diagnostics.Debug.WriteLine(
                $"Remote community-hazard fetch unavailable: {ex.Message}");
#endif
            return null;
        }
    }

    public async Task<List<CommunityReport>> GetReportsAsync(string searchQuery = "", string filterOption = "Newest first", double? userLat = null, double? userLng = null)
    {
        var client = await GetClientAsync();
        if (client != null)
        {
            try
            {
                var response = await client.From<CommunityReport>()
                    .Order("created_at", Supabase.Postgrest.Constants.Ordering.Descending)
                    .Get();

                if (response.Models != null && response.Models.Count > 0)
                {
                    // Only display Approved or Resolved reports on public community feed
                    IEnumerable<CommunityReport> list = response.Models.Where(r => 
                        !string.IsNullOrWhiteSpace(r.Status) && 
                        (r.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase) || 
                         r.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase)));

                    // Calculate vicinity distance if user location is available
                    foreach (var report in list)
                    {
                        CalculateReportDistance(report, userLat, userLng);
                    }

                    if (!string.IsNullOrWhiteSpace(searchQuery))
                    {
                        var q = searchQuery.Trim().ToLowerInvariant();
                        list = list.Where(r =>
                            (r.Title != null && r.Title.ToLowerInvariant().Contains(q)) ||
                            (r.Description != null && r.Description.ToLowerInvariant().Contains(q)) ||
                            (r.Address != null && r.Address.ToLowerInvariant().Contains(q)) ||
                            (r.PostedBy != null && r.PostedBy.ToLowerInvariant().Contains(q)) ||
                            (r.Category != null && r.Category.ToLowerInvariant().Contains(q)));
                    }

                    switch (filterOption)
                    {
                        case "Nearest to me":
                            list = list.OrderBy(r => r.DistanceKm).ThenByDescending(r => r.CreatedAt);
                            break;
                        case "Oldest first":
                            list = list.OrderBy(r => r.CreatedAt);
                            break;
                        case "Newest first":
                        default:
                            list = list.OrderByDescending(r => r.CreatedAt);
                            break;
                    }

                    var fetchedList = list.ToList();

                    // Apply local liked status & sync to Reports cache
                    var likedReportIds = Microsoft.Maui.Storage.Preferences.Default.Get("LikedReportIds", "");
                    var likedSet = new HashSet<string>(likedReportIds.Split(',', StringSplitOptions.RemoveEmptyEntries));

                    Reports.Clear();
                    foreach (var item in fetchedList)
                    {
                        item.IsLikedByCurrentUser = likedSet.Contains(item.Id);
                        Reports.Add(item);
                    }

                    return fetchedList;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Supabase GetReportsAsync Error: {ex.Message}");
            }
        }

        var fallbackList = GetFallbackReports().Where(r => 
            !string.IsNullOrWhiteSpace(r.Status) && 
            (r.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase) || 
             r.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase))).ToList();

        foreach (var report in fallbackList)
        {
            CalculateReportDistance(report, userLat, userLng);
        }

        switch (filterOption)
        {
            case "Nearest to me":
                fallbackList = fallbackList.OrderBy(r => r.DistanceKm).ThenByDescending(r => r.CreatedAt).ToList();
                break;
            case "Oldest first":
                fallbackList = fallbackList.OrderBy(r => r.CreatedAt).ToList();
                break;
            case "Newest first":
            default:
                fallbackList = fallbackList.OrderByDescending(r => r.CreatedAt).ToList();
                break;
        }

        var fallbackLikedReportIds = Microsoft.Maui.Storage.Preferences.Default.Get("LikedReportIds", "");
        var fallbackLikedSet = new HashSet<string>(fallbackLikedReportIds.Split(',', StringSplitOptions.RemoveEmptyEntries));

        Reports.Clear();
        foreach (var item in fallbackList)
        {
            item.IsLikedByCurrentUser = fallbackLikedSet.Contains(item.Id);
            Reports.Add(item);
        }

        return fallbackList;
    }

    private void CalculateReportDistance(CommunityReport report, double? userLat, double? userLng)
    {
        if (report == null) return;

        // Fallback default coordinates if report lacks coordinates
        if (report.Latitude == 0 && report.Longitude == 0)
        {
            report.Latitude = 14.6585;
            report.Longitude = 121.0955;
        }

        double uLat = userLat ?? 14.6585;
        double uLng = userLng ?? 121.0955;

        try
        {
            double distKm = Microsoft.Maui.Devices.Sensors.Location.CalculateDistance(
                uLat, uLng,
                report.Latitude, report.Longitude,
                Microsoft.Maui.Devices.Sensors.DistanceUnits.Kilometers);

            report.DistanceKm = distKm;

            if (distKm < 1.0)
            {
                int meters = (int)Math.Round(distKm * 1000);
                report.DistanceText = meters <= 50 ? "Within 50 meters" : $"{meters} meters away";
            }
            else
            {
                report.DistanceText = $"{distKm:F1} km away";
            }
        }
        catch
        {
            report.DistanceKm = 0.5;
            report.DistanceText = "350 meters away";
        }
    }

    public async Task AddReportAsync(CommunityReport report)
    {
        if (string.IsNullOrWhiteSpace(report.Id))
        {
            report.Id = Guid.NewGuid().ToString();
        }

        // Force Pending status requiring Admin moderation approval before appearing on feed
        report.Status = "Pending";

        var client = await GetClientAsync();
        if (client != null)
        {
            try
            {
                await client.From<CommunityReport>().Insert(report);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Supabase AddReportAsync Error: {ex.Message}");
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    if (Shell.Current != null)
                    {
                        await Shell.Current.DisplayAlert("Supabase Error", $"Could not send report to web: {ex.Message}", "OK");
                    }
                });
            }
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.DisplayAlert("Supabase Error", "Supabase Client could not be initialized.", "OK");
                }
            });
        }
    }

    public async Task AddCommentAsync(string reportId, string content, string authorName)
    {
        var report = Reports.FirstOrDefault(r => r.Id == reportId);
        if (report != null && report.AllowComments)
        {
            var comment = new CommunityComment
            {
                ReportId = reportId,
                AuthorName = string.IsNullOrWhiteSpace(authorName) ? "User" : authorName,
                Content = content,
                PostedAt = DateTime.Now
            };
            report.Comments.Add(comment);
            report.NotifyCommentsChanged();

            var client = await GetClientAsync();
            if (client != null)
            {
                try
                {
                    await client.From<CommunityReport>().Update(report);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Supabase AddComment Error: {ex.Message}");
                }
            }
        }
    }

    public async Task ToggleLikeAsync(string reportId)
    {
        var report = Reports.FirstOrDefault(r => r.Id == reportId);
        if (report != null)
        {
            var likedReportIds = Microsoft.Maui.Storage.Preferences.Default.Get("LikedReportIds", "");
            var likedSet = new HashSet<string>(likedReportIds.Split(',', StringSplitOptions.RemoveEmptyEntries));

            if (report.IsLikedByCurrentUser)
            {
                report.IsLikedByCurrentUser = false;
                report.LikeCount = Math.Max(0, report.LikeCount - 1);
                likedSet.Remove(report.Id);
            }
            else
            {
                report.IsLikedByCurrentUser = true;
                report.LikeCount++;
                likedSet.Add(report.Id);
            }

            Microsoft.Maui.Storage.Preferences.Default.Set("LikedReportIds", string.Join(",", likedSet));

            var client = await GetClientAsync();
            if (client != null)
            {
                try
                {
                    await client.From<CommunityReport>().Update(report);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Supabase ToggleLike Error: {ex.Message}");
                }
            }
        }
    }

    private List<CommunityReport> GetFallbackReports()
    {
        return new List<CommunityReport>
        {
            new CommunityReport
            {
                Id = "rep-1",
                Title = "Waist-deep Flood Water along J.P. Rizal St.",
                Description = "Flood waters reaching waist level near Malanday market area. Passable only to heavy rescue trucks.",
                Address = "J.P. Rizal St. cor. Malaya St., Malanday, Marikina City",
                Latitude = 14.6590,
                Longitude = 121.0960,
                Category = "Flood Warning",
                PostedBy = "Captain Santos",
                CreatedAt = DateTime.UtcNow.AddMinutes(-25),
                Status = "Approved"
            },
            new CommunityReport
            {
                Id = "rep-2",
                Title = "Fallen Tree Blocking Entrance to Evacuation Center",
                Description = "Large acacia branch down near H. Bautista Elem. Gate 2. Local LGU clearing operations underway.",
                Address = "H. Bautista Elementary School, Concepcion Uno, Marikina City",
                Latitude = 14.6540,
                Longitude = 121.1010,
                Category = "Road Hazard",
                PostedBy = "Maria Cruz",
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                Status = "Approved"
            },
            new CommunityReport
            {
                Id = "rep-3",
                Title = "Power Outage & Exposed High-Voltage Line",
                Description = "Downed electrical post after strong wind burst. Area cordoned off by emergency responders.",
                Address = "Nangka Elementary School, Nangka, Marikina City",
                Latitude = 14.6670,
                Longitude = 121.1050,
                Category = "Power Outage",
                PostedBy = "BFP Marikina",
                CreatedAt = DateTime.UtcNow.AddHours(-3),
                Status = "Approved"
            }
        };
    }
}
