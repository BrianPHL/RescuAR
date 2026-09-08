using System;
using System.Collections.ObjectModel;
using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace RescuAR.App.Models;

[Table("community_reports")]
public class CommunityReport : BaseModel
{
    [PrimaryKey("id", true)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("description")]
    public string Description { get; set; } = string.Empty;

    [Column("address")]
    public string Address { get; set; } = string.Empty;

    [Column("latitude")]
    public double Latitude { get; set; }

    [Column("longitude")]
    public double Longitude { get; set; }

    [Column("posted_by")]
    public string PostedBy { get; set; } = "Aubrey T.";

    [Column("category")]
    public string Category { get; set; } = "Flood Warning";

    [Column("severity")]
    public string Severity { get; set; } = "Medium";

    [Column("status")]
    public string Status { get; set; } = "Pending";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("media_url")]
    public string MediaUrl { get; set; } = string.Empty;

    [JsonIgnore]
    public string MediaType { get; set; } = "Image";

    [JsonIgnore]
    public bool HasMedia
    {
        get => !string.IsNullOrWhiteSpace(MediaUrl);
        set { }
    }

    [JsonIgnore]
    public bool AllowComments { get; set; } = true;

    [Column("like_count")]
    public int LikeCount { get; set; }

    [JsonIgnore]
    public string DistanceText { get; set; } = "320 meters away";

    [JsonIgnore]
    public bool IsLikedByCurrentUser { get; set; }

    [JsonIgnore]
    public ObservableCollection<CommunityComment> Comments { get; set; } = new();

    [Column("comments_json")]
    public string CommentsJson
    {
        get => JsonConvert.SerializeObject(Comments);
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                try
                {
                    var deserialized = JsonConvert.DeserializeObject<ObservableCollection<CommunityComment>>(value);
                    if (deserialized != null)
                    {
                        Comments = deserialized;
                        return;
                    }
                }
                catch
                {
                    // Fallback on error
                }
            }
            Comments = new ObservableCollection<CommunityComment>();
        }
    }

    [JsonIgnore]
    public int CommentCount => Comments.Count;

    [JsonIgnore]
    public DateTime PostedAt => CreatedAt.Kind == DateTimeKind.Utc ? CreatedAt.ToLocalTime() : CreatedAt;

    [JsonIgnore]
    public string PostedAtText => PostedAt.ToString("MMM d, yyyy • h:mm tt");

    [JsonIgnore]
    public string AuthorInitials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(PostedBy)) return "AT";
            var parts = PostedBy.Trim().Split(' ');
            if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpper();
            return $"{parts[0][0]}{parts[parts.Length - 1][0]}".ToUpper();
        }
    }

    [JsonIgnore]
    public string CategoryBadgeColor => Category switch
    {
        "Flood Warning" => "#EFF6FF",
        "Rescue Request" => "#FEF2F2",
        "Road Hazard" => "#FFFBEB",
        "Power Outage" => "#F3E8FF",
        _ => "#F1F5F9"
    };

    [JsonIgnore]
    public string CategoryTextColor => Category switch
    {
        "Flood Warning" => "#1E40AF",
        "Rescue Request" => "#DC2626",
        "Road Hazard" => "#B45309",
        "Power Outage" => "#6B21A8",
        _ => "#475569"
    };

    [JsonIgnore]
    public string CategoryIcon => Category switch
    {
        "Flood Warning" => "M12,3.25C12,3.25 6,10 6,14A6,6 0 0,0 12,20A6,6 0 0,0 18,14C18,10 12,3.25 12,3.25Z",
        "Rescue Request" => "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 12,2M11,7H13V13H11V7M11,15H13V17H11V15Z",
        "Road Hazard" => "M12,2L1,21H23L12,2M12,6L19.8,20H4.2L12,6M11,10V14H13V10H11M11,16V18H13V16H11Z",
        "Power Outage" => "M7,2H17A1,1 0 0,1 18,3V6A3,3 0 0,1 15,9V21A1,1 0 0,1 14,22H10A1,1 0 0,1 9,21V9A3,3 0 0,1 6,6V3A1,1 0 0,1 7,2Z",
        _ => "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 12,22A10,10 0 0,0 12,2Z"
    };

    public void NotifyCommentsChanged()
    {
    }
}