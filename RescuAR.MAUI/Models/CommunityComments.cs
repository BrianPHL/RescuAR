using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace RescuAR.App.Models
{
    public partial class CommunityComment : ObservableObject
    {
        [ObservableProperty]
        private string id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private string reportId = string.Empty;

        [ObservableProperty]
        private string userId = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayAuthorName))]
        [NotifyPropertyChangedFor(nameof(AuthorInitial))]
        private string authorName = string.Empty;

        [ObservableProperty]
        private string authorAvatar = "https://i.pravatar.cc/100?u=user";

        [ObservableProperty]
        private string content = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PostedAtText))]
        private DateTime postedAt = DateTime.Now;

        public string PostedAtText => PostedAt.ToString("MMM d, yyyy • h:mm tt");

        [JsonIgnore]
        public string DisplayAuthorName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(AuthorName) &&
                    !string.Equals(AuthorName.Trim(), "User", StringComparison.OrdinalIgnoreCase) &&
                    !Guid.TryParse(AuthorName.Trim(), out _))
                {
                    return AuthorName.Trim();
                }
                return "Resident";
            }
        }

        [JsonIgnore]
        public string AuthorInitial
        {
            get
            {
                string name = DisplayAuthorName;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name.Trim()[0].ToString().ToUpper();
                }
                return "R";
            }
        }
    }
}
