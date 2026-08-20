using System;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace RescuAR.App.Models;

[Table("advisories")]
public class DisasterAdvisory : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("message")]
    public string? Message { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("action_plan")]
    public string? ActionPlan { get; set; }

    [Column("recommended_action")]
    public string? RecommendedAction { get; set; }

    [Column("category")]
    public string Category { get; set; } = "General";

    [Column("severity")]
    public string? Severity { get; set; }

    [Column("water_level")]
    public double WaterLevel { get; set; }

    [Column("alert_level")]
    public string? AlertLevel { get; set; }

    [Column("affected_area")]
    public string? AffectedArea { get; set; }

    [Column("affected_areas")]
    public string? AffectedAreas { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Robust UI Display Getters
    public string DisplayAlertLevel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Severity)) return Severity;
            if (!string.IsNullOrWhiteSpace(AlertLevel)) return AlertLevel;
            return "Standby";
        }
    }

    public string DisplayMessage
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Message)) return Message;
            if (!string.IsNullOrWhiteSpace(Description)) return Description;
            return "No additional description provided.";
        }
    }

    public string DisplayActionPlan
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ActionPlan)) return ActionPlan;
            if (!string.IsNullOrWhiteSpace(RecommendedAction)) return RecommendedAction;
            return string.Empty;
        }
    }

    public string DisplayAffectedArea
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AffectedAreas)) return AffectedAreas;
            if (!string.IsNullOrWhiteSpace(AffectedArea)) return AffectedArea;
            return "Marikina City";
        }
    }

    public bool HasActionPlan => !string.IsNullOrWhiteSpace(DisplayActionPlan);

    public bool ShowWaterLevel => WaterLevel > 0;

    public string WaterLevelText => $"{WaterLevel:F1} meters";

    public string CreatedAtText => CreatedAt.Kind == DateTimeKind.Utc 
        ? CreatedAt.ToLocalTime().ToString("MMM dd, yyyy • hh:mm tt")
        : CreatedAt.ToString("MMM dd, yyyy • hh:mm tt");

    public string TimeAgoText
    {
        get
        {
            var localTime = CreatedAt.Kind == DateTimeKind.Utc ? CreatedAt.ToLocalTime() : CreatedAt;
            var diff = DateTime.Now - localTime;

            if (diff.TotalSeconds < 60)
                return "Just now";
            if (diff.TotalMinutes < 60)
                return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24)
                return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}d ago";

            return localTime.ToString("MMM dd • h:mm tt");
        }
    }

    public string AlertBadgeBg => DisplayAlertLevel.ToLower() switch
    {
        "critical" or "level 3" or "evacuate" or "high" or "high severity" => "#FEE2E2",
        "warning" or "level 2" or "alarm" or "medium" or "moderate" => "#FEF3C7",
        "standby" or "level 1" or "alert" or "low" => "#E0F2FE",
        _ => "#DCFCE7"
    };

    public string AlertBadgeText => DisplayAlertLevel.ToLower() switch
    {
        "critical" or "level 3" or "evacuate" or "high" or "high severity" => "#DC2626",
        "warning" or "level 2" or "alarm" or "medium" or "moderate" => "#D97706",
        "standby" or "level 1" or "alert" or "low" => "#0369A1",
        _ => "#16A34A"
    };

    public string AlertIconEmoji => DisplayAlertLevel.ToLower() switch
    {
        "critical" or "level 3" or "evacuate" or "high" or "high severity" => "🚨",
        "warning" or "level 2" or "alarm" or "medium" or "moderate" => "⚠️",
        "standby" or "level 1" or "alert" or "low" => "🌊",
        _ => "ℹ️"
    };
}
