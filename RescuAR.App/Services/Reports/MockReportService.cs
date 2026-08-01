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
        Task<CommunityReport?> GetReportByIdAsync(int id);
        Task AddReportAsync(CommunityReport report);
        Task AddCommentAsync(int reportId, ReportComment comment);
    }

    public class MockReportService : IReportService
    {
        private readonly List<CommunityReport> _reports;
        private int _nextId = 3;

        public MockReportService()
        {
            _reports = new List<CommunityReport>
            {
                new CommunityReport
                {
                    Id = 1,
                    Title = "Someone help us!",
                    Description = "A tree fell at J.P. Rizal Street near Malaya St. and now the street is blocked. We need help removing it as it is our only way out!",
                    ImageUrl = "https://images.unsplash.com/photo-1596484346857-e9a0f0230286?q=80&w=800&auto=format&fit=crop",
                    Address = "J.P. Rizal St. cor. Malaya St., Malanday, Marikina City",
                    DistanceMeters = 320,
                    PostedBy = "Aubrey T.",
                    PostedAt = new DateTime(2026, 6, 6, 14, 23, 0),
                    Comments = new List<ReportComment>
                    {
                        new ReportComment
                        {
                            Id = 1,
                            AuthorName = "Gerimiah Josh Palma",
                            Text = "We have notified the local barangay. Rescue team is on the way.",
                            PostedAt = new DateTime(2026, 6, 6, 14, 30, 0)
                        }
                    }
                },
                new CommunityReport
                {
                    Id = 2,
                    Title = "Roads are flooded at Malaya Street!",
                    Description = "Be careful coming to this area. The entire road is submerged and is impassable!",
                    ImageUrl = "https://images.unsplash.com/photo-1542385151-efd9000785a0?q=80&w=800&auto=format&fit=crop",
                    Address = "Malaya St., Malanday, Marikina City",
                    DistanceMeters = 150,
                    PostedBy = "Gerimiah Josh Palma",
                    PostedAt = new DateTime(2026, 6, 4, 16, 12, 0),
                    Comments = new List<ReportComment>
                    {
                        new ReportComment
                        {
                            Id = 2,
                            AuthorName = "Juan Dela Cruz",
                            Text = "Thanks for the heads up! Stay safe everyone.",
                            PostedAt = new DateTime(2026, 6, 4, 16, 20, 0)
                        },
                        new ReportComment
                        {
                            Id = 3,
                            AuthorName = "Maria Clara",
                            Text = "Is the water level rising fast?",
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

        public async Task<CommunityReport?> GetReportByIdAsync(int id)
        {
            await Task.Delay(300); // Simulate network latency
            return _reports.FirstOrDefault(r => r.Id == id);
        }

        public async Task AddReportAsync(CommunityReport report)
        {
            await Task.Delay(500); // Simulate network latency
            report.Id = _nextId++;
            report.PostedAt = DateTime.Now;
            _reports.Insert(0, report);
        }

        public async Task AddCommentAsync(int reportId, ReportComment comment)
        {
            await Task.Delay(300); // Simulate network latency
            var report = _reports.FirstOrDefault(r => r.Id == reportId);
            if (report != null)
            {
                comment.Id = report.Comments.Count + 1;
                comment.PostedAt = DateTime.Now;
                report.Comments.Add(comment);
            }
        }
    }
}
