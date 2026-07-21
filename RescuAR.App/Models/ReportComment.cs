using System;

namespace RescuAR.App.Models
{
    public class ReportComment
    {
        public int Id { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public string AuthorInitials => AuthorName.Length > 0
            ? string.Concat(AuthorName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w[0].ToString().ToUpper()))
            : "?";
        public string Text { get; set; } = string.Empty;
        public DateTime PostedAt { get; set; }
        public bool LikedByUser { get; set; }

        public string PostedAtDisplay => PostedAt.ToString("MMM d, h:mm tt");
    }
}
