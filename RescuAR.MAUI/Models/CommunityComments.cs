using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RescuAR.App.Models
{
    public partial class CommunityComment : ObservableObject
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ReportId { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public string AuthorAvatar { get; set; } = "https://i.pravatar.cc/100?u=user";
        public string Content { get; set; } = string.Empty;
        public DateTime PostedAt { get; set; } = DateTime.Now;

        public string PostedAtText => PostedAt.ToString("MMM d, yyyy • h:mm tt");
    }
}
