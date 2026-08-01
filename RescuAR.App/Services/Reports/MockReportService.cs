using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RescuAR.App.Models;

namespace RescuAR.App.Services.Reports
{
    public interface IReportService
    {
        Task<IEnumerable<CommunityReport>> GetReportsAsync();
        Task<CommunityReport?> GetReportByIdAsync(string id);
        Task AddReportAsync(CommunityReport report);
        Task AddCommentAsync(string reportId, Comment comment);
    }

    public class MockReportService : IReportService
    {
        private readonly List<CommunityReport> _reports;

        public MockReportService()
        {
            _reports = new List<CommunityReport>
            {
                new CommunityReport
                {
                    Id = "1",
                    Title = "Someone help us!",
                    Description = "A tree fell at J.P. Rizal Street near Malaya St. and now the street is blocked. We need help removing it as it is our only way out!",
                    ImageUrl = "https://images.unsplash.com/photo-1596484346857-e9a0f0230286?q=80&w=800&auto=format&fit=crop", // Placeholder image for fallen tree
                    LocationAddress = "J.P. Rizal St. cor. Malaya St., Malanday, Marikina City",
                    DistanceInMeters = 320,
                    PostedBy = "Aubrey T.",
                    PostedAt = new DateTime(2026, 6, 6, 14, 23, 0), // June 6, 2026 • 2:23 PM
                    AllowComments = true,
                    Comments = new List<Comment>
                    {
                        new Comment
                        {
                            AuthorName = "Gerimiah Josh Palma",
                            Content = "We have notified the local barangay. Rescue team is on the way.",
                            PostedAt = new DateTime(2026, 6, 6, 14, 30, 0)
                        }
                    }
                },
                new CommunityReport
                {
                    Id = "2",
                    Title = "Roads are flooded at Malaya Street!",
                    Description = "Be careful coming to this area. The entire road is submerged and is impassable!",
                    ImageUrl = "https://images.unsplash.com/photo-1542385151-efd9000785a0?q=80&w=800&auto=format&fit=crop", // Placeholder image for flood
                    LocationAddress = "Malaya St., Malanday, Marikina City",
                    DistanceInMeters = 150,
                    PostedBy = "Gerimiah Josh Palma",
                    PostedAt = new DateTime(2026, 6, 4, 16, 12, 0), // June 4, 2026 • 4:12 PM
                    AllowComments = true,
                    Comments = new List<Comment>
                    {
                        new Comment
                        {
                            AuthorName = "Juan Dela Cruz",
                            Content = "Thanks for the heads up! Stay safe everyone.",
                            PostedAt = new DateTime(2026, 6, 4, 16, 20, 0)
                        },
                        new Comment
                        {
                            AuthorName = "Maria Clara",
                            Content = "Is the water level rising fast?",
                            PostedAt = new DateTime(2026, 6, 4, 16, 25, 0)
                        }
                    }
                }
            };
        }

        public async Task<IEnumerable<CommunityReport>> GetReportsAsync()
        {
            await Task.Delay(500); // Simulate network latency
            return _reports.OrderByDescending(r => r.PostedAt).ToList();
        }

        public async Task<CommunityReport?> GetReportByIdAsync(string id)
        {
            await Task.Delay(300); // Simulate network latency
            return _reports.FirstOrDefault(r => r.Id == id);
        }

        public async Task AddReportAsync(CommunityReport report)
        {
            await Task.Delay(500); // Simulate network latency
            report.Id = Guid.NewGuid().ToString();
            report.PostedAt = DateTime.Now;
            _reports.Insert(0, report);
        }

        public async Task AddCommentAsync(string reportId, Comment comment)
        {
            await Task.Delay(300); // Simulate network latency
            var report = _reports.FirstOrDefault(r => r.Id == reportId);
            if (report != null && report.AllowComments)
            {
                comment.Id = Guid.NewGuid().ToString();
                comment.PostedAt = DateTime.Now;
                report.Comments.Add(comment);
            }
        }
    }
}
