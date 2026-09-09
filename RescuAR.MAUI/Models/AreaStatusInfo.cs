using System;

namespace RescuAR.App.Models;

public class AreaStatusInfo
{
    public FloodRiskLevel RiskLevel { get; set; }
    public int ActiveAdvisoriesCount { get; set; }
    public double AlertRangeKm { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
