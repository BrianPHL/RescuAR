using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using RescuAR.App.Models;

namespace RescuAR.App.Services.Reports
{
    public class CommunityReportService
    {
        public ObservableCollection<CommunityReport> Reports { get; } = new();

        public CommunityReportService()
        {
            SeedInitialReports();
        }

        private void SeedInitialReports()
        {
            if (Reports.Count == 0)
            {
                Reports.Add(new CommunityReport
                {
                    Id = "rep-1",
                    Title = "Waist-deep Flood Water along J.P. Rizal St.",
                    Description = "Flood waters reaching waist level near Malanday market area. Passable only to heavy rescue trucks.",
                    Address = "J.P. Rizal St. cor. Malaya St., Malanday, Marikina City",
                    Category = "Flood Warning",
                    DistanceText = "150 meters away",
                    PostedBy = "Captain Santos",
                    PostedAt = DateTime.Now.AddMinutes(-25),
                    LikeCount = 14,
                    AllowComments = true
                });

                Reports.Add(new CommunityReport
                {
                    Id = "rep-2",
                    Title = "Fallen Tree Blocking Entrance to Evacuation Center",
                    Description = "Large acacia branch down near H. Bautista Elem. Gate 2. Local LGU clearing operations underway.",
                    Address = "H. Bautista Elementary School, Concepcion Uno, Marikina City",
                    Category = "Road Hazard",
                    DistanceText = "620 meters away",
                    PostedBy = "Maria Cruz",
                    PostedAt = DateTime.Now.AddHours(-1),
                    LikeCount = 8,
                    AllowComments = true
                });
            }
        }

        public Task<List<CommunityReport>> GetReportsAsync(string searchQuery = "", string filterOption = "Newest first")
        {
            IEnumerable<CommunityReport> query = Reports;

            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var q = searchQuery.Trim().ToLowerInvariant();
                query = query.Where(r => 
                    r.Title.ToLowerInvariant().Contains(q) || 
                    r.Description.ToLowerInvariant().Contains(q) ||
                    r.Address.ToLowerInvariant().Contains(q) ||
                    r.PostedBy.ToLowerInvariant().Contains(q) ||
                    r.Category.ToLowerInvariant().Contains(q));
            }

            switch (filterOption)
            {
                case "Oldest first":
                    query = query.OrderBy(r => r.PostedAt);
                    break;
                case "Nearest to me":
                    query = query.OrderBy(r => ParseDistance(r.DistanceText));
                    break;
                case "Newest first":
                default:
                    query = query.OrderByDescending(r => r.PostedAt);
                    break;
            }

            return Task.FromResult(query.ToList());
        }

        private double ParseDistance(string distanceText)
        {
            if (string.IsNullOrWhiteSpace(distanceText)) return 999999;
            var parts = distanceText.Split(' ');
            if (parts.Length > 0 && double.TryParse(parts[0], out double val))
            {
                if (distanceText.Contains("km")) return val * 1000;
                return val;
            }
            return 999999;
        }

        public Task AddReportAsync(CommunityReport report)
        {
            if (string.IsNullOrWhiteSpace(report.Id))
            {
                report.Id = Guid.NewGuid().ToString();
            }
            Reports.Insert(0, report);
            return Task.CompletedTask;
        }

        public Task AddCommentAsync(string reportId, string content, string authorName)
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
            }
            return Task.CompletedTask;
        }

        public Task ToggleLikeAsync(string reportId)
        {
            var report = Reports.FirstOrDefault(r => r.Id == reportId);
            if (report != null)
            {
                if (report.IsLikedByCurrentUser)
                {
                    report.IsLikedByCurrentUser = false;
                    report.LikeCount = Math.Max(0, report.LikeCount - 1);
                }
                else
                {
                    report.IsLikedByCurrentUser = true;
                    report.LikeCount++;
                }
            }
            return Task.CompletedTask;
        }
    }
}
