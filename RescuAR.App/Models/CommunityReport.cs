using System;
using System.Collections.Generic;

namespace RescuAR.App.Models
{
    public class CommunityReport
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public int DistanceMeters { get; set; }
        public string PostedBy { get; set; } = string.Empty;
        public DateTime PostedAt { get; set; }
        public string HazardType { get; set; } = string.Empty;
        public List<ReportComment> Comments { get; set; } = new();

        public string DistanceDisplay =>
            DistanceMeters >= 1000
                ? $"{DistanceMeters / 1000.0:F1} km away"
                : $"{DistanceMeters} meters away";

        public string PostedAtDisplay =>
            PostedAt.ToString("MMMM d, yyyy - h:mm tt");
    }
}
