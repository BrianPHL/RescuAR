using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using RescuAR.App.Services.Translation;

namespace RescuAR.App.Models;

[Table("advisories")]
public class DisasterAdvisory : BaseModel, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

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

    [Column("escalation_actions")]
    public string? EscalationActions { get; set; }

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

    [JsonIgnore]
    public bool IsTagalog { get; set; }

    public void SetLanguage(bool isTagalog)
    {
        IsTagalog = isTagalog;
        OnPropertyChanged(nameof(IsTagalog));
        OnPropertyChanged(nameof(DisplayAlertLevelText));
        OnPropertyChanged(nameof(DisplayTitleText));
        OnPropertyChanged(nameof(DisplayMessageText));
        OnPropertyChanged(nameof(DisplayActionPlanText));
        OnPropertyChanged(nameof(DisplayEscalationActionsText));
        OnPropertyChanged(nameof(DisplayAffectedAreaText));
        OnPropertyChanged(nameof(HeaderTagText));
        OnPropertyChanged(nameof(ActionPlanHeaderTagText));
        OnPropertyChanged(nameof(AffectedAreaHeaderTagText));
        OnPropertyChanged(nameof(LanguageButtonLabel));
        OnPropertyChanged(nameof(CloseButtonText));
        OnPropertyChanged(nameof(DetailsButtonText));
    }

    public void ToggleLanguage()
    {
        SetLanguage(!IsTagalog);
    }

    // Robust UI Display Getters decorated with JsonIgnore
    [JsonIgnore]
    public string DisplayAlertLevel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Severity)) return Severity.Trim();
            if (!string.IsNullOrWhiteSpace(AlertLevel)) return AlertLevel.Trim();
            return "Standby";
        }
    }

    [JsonIgnore]
    public string DisplayMessage
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Message)) return Message;
            if (!string.IsNullOrWhiteSpace(Description)) return Description;
            return "No additional description provided.";
        }
    }

    [JsonIgnore]
    public string DisplayActionPlan
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ActionPlan)) return ActionPlan;
            if (!string.IsNullOrWhiteSpace(RecommendedAction)) return RecommendedAction;
            return string.Empty;
        }
    }

    [JsonIgnore]
    public string DisplayAffectedArea
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AffectedAreas)) return AffectedAreas;
            if (!string.IsNullOrWhiteSpace(AffectedArea)) return AffectedArea;
            return "Marikina City";
        }
    }

    [JsonIgnore]
    public string DisplayAlertLevelText => IsTagalog ? AdvisoryTranslationService.TranslateAlertLevel(DisplayAlertLevel) : DisplayAlertLevel;

    [JsonIgnore]
    public string DisplayTitleText => IsTagalog ? AdvisoryTranslationService.TranslateText(Title) : Title;

    [JsonIgnore]
    public string DisplayMessageText => IsTagalog ? AdvisoryTranslationService.TranslateText(DisplayMessage) : DisplayMessage;

    [JsonIgnore]
    public string DisplayEscalationActions
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(EscalationActions)) return EscalationActions;
            return string.Empty;
        }
    }

    [JsonIgnore]
    public string DisplayActionPlanText => IsTagalog ? AdvisoryTranslationService.TranslateText(DisplayActionPlan) : DisplayActionPlan;

    [JsonIgnore]
    public string DisplayEscalationActionsText => IsTagalog ? AdvisoryTranslationService.TranslateText(DisplayEscalationActions) : DisplayEscalationActions;

    [JsonIgnore]
    public string DisplayAffectedAreaText => IsTagalog ? AdvisoryTranslationService.TranslateText(DisplayAffectedArea) : DisplayAffectedArea;

    [JsonIgnore]
    public string HeaderTagText => IsTagalog ? "BAGONG ADVISORY SA SAKUNA" : "NEW EMERGENCY ADVISORY";

    [JsonIgnore]
    public string ActionPlanHeaderTagText => IsTagalog ? "MGA REKOMENDADONG HAKBANGIN" : "RECOMMENDED ACTION PLAN";

    [JsonIgnore]
    public string AffectedAreaHeaderTagText => IsTagalog ? "MGA APEKTADONG LUGAR" : "AFFECTED AREA SECTORS";

    [JsonIgnore]
    public string LanguageButtonLabel => IsTagalog ? "🌐 Tagalog" : "🌐 English";

    [JsonIgnore]
    public string CloseButtonText => IsTagalog ? "Isara" : "Close";

    [JsonIgnore]
    public string DetailsButtonText => IsTagalog ? "Buong Detalye" : "See Full Details";

    [JsonIgnore]
    public bool HasActionPlan => !string.IsNullOrWhiteSpace(DisplayActionPlan);

    [JsonIgnore]
    public bool ShowWaterLevel => WaterLevel > 0;

    [JsonIgnore]
    public string WaterLevelText => $"{WaterLevel:F1} meters";

    [JsonIgnore]
    public string CreatedAtText => CreatedAt.Kind == DateTimeKind.Utc 
        ? CreatedAt.ToLocalTime().ToString("MMM dd, yyyy • hh:mm tt")
        : CreatedAt.ToString("MMM dd, yyyy • hh:mm tt");

    [JsonIgnore]
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

    [JsonIgnore]
    public string AlertBadgeBg => DisplayAlertLevel.ToLower() switch
    {
        "critical" or "level 3" or "evacuate" or "high" or "high severity" => "#FEE2E2",
        "warning" or "level 2" or "alarm" or "medium" or "moderate" => "#FEF3C7",
        "standby" or "level 1" or "alert" or "low" => "#E0F2FE",
        _ => "#DCFCE7"
    };

    [JsonIgnore]
    public string AlertBadgeText => DisplayAlertLevel.ToLower() switch
    {
        "critical" or "level 3" or "evacuate" or "high" or "high severity" => "#DC2626",
        "warning" or "level 2" or "alarm" or "medium" or "moderate" => "#D97706",
        "standby" or "level 1" or "alert" or "low" => "#0369A1",
        _ => "#16A34A"
    };

    [JsonIgnore]
    public string AlertIconEmoji => DisplayAlertLevel.ToLower() switch
    {
        "critical" or "level 3" or "evacuate" or "high" or "high severity" => "🚨",
        "warning" or "level 2" or "alarm" or "medium" or "moderate" => "⚠️",
        "standby" or "level 1" or "alert" or "low" => "🌊",
        _ => "ℹ️"
    };

    [JsonIgnore]
    public string CategoryIconData => DisplayAlertLevel.ToLower() switch
    {
        "critical" or "level 3" or "evacuate" or "high" or "high severity" => "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M11,7H13V13H11V7M11,15H13V17H11V15Z",
        "warning" or "level 2" or "alarm" or "medium" or "moderate" => "M12,2L1,21H23L12,2M12,6L19.8,20H4.2L12,6M11,10V14H13V10H11M11,16V18H13V16H11Z",
        _ => "M12,3.25C12,3.25 6,10 6,14A6,6 0 0,0 12,20A6,6 0 0,0 18,14C18,10 12,3.25 12,3.25Z"
    };
}
