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

    public async Task<List<CommunityReport>> GetReportsAsync(string searchQuery = "", string filterOption = "Newest first")
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
                Category = "Flood Warning",
                PostedBy = "Captain Santos",
                CreatedAt = DateTime.UtcNow.AddMinutes(-25),
                Status = "Pending"
            },
            new CommunityReport
            {
                Id = "rep-2",
                Title = "Fallen Tree Blocking Entrance to Evacuation Center",
                Description = "Large acacia branch down near H. Bautista Elem. Gate 2. Local LGU clearing operations underway.",
                Address = "H. Bautista Elementary School, Concepcion Uno, Marikina City",
                Category = "Road Hazard",
                PostedBy = "Maria Cruz",
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                Status = "Approved"
            }
        };
    }
}