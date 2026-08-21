using System.Collections.Generic;
using System.Threading.Tasks;

namespace RescuAR.App.Services.Dashboard;

public class DisasterInfoData
{
    public string Title { get; set; } = "Flood Advisory";
    public string Description { get; set; } = "Water level in Marikina River has reached Alert Level 2";
    public string ActionText { get; set; } = "Disaster Updates";
    public string ModuleRoute { get; set; } = "//Reports/AdvisoryFeed";
    public string ModuleName { get; set; } = "Disaster Updates / Advisory Feed";
}

public class PreparednessData
{
    public int PercentReady { get; set; } = 60;
    public int PreparedItems { get; set; } = 6;
    public int TotalItems { get; set; } = 10;
    public string ActionText { get; set; } = "Preparation Progress";
    public string ModuleRoute { get; set; } = "//Prepare/Checklist";
    public string ModuleName { get; set; } = "Preparation Progress / Checklist";
}

public class EvacuationData
{
    public string CenterName { get; set; } = "Malanday Elementary School";
    public double DistanceMeters { get; set; } = 877;
    public string ActionText { get; set; } = "Nearest Evacuation Center";
    public string ModuleRoute { get; set; } = "//Prepare/EvacuationCenterInfo";
    public string ModuleName { get; set; } = "Evacuation Center Info";
}

public class SafetyCircleGroupItem
{
    public string Name { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public bool IsAlert { get; set; }
}

public class SafetyCircleOverviewData
{
    public List<SafetyCircleGroupItem> Groups { get; set; } = new();
    public string ActionText { get; set; } = "Safety Circle";
    public string ModuleRoute { get; set; } = "//Map/SafetyCircle";
    public string ModuleName { get; set; } = "Safety Circle Module";
}

public class CommunityReportItem
{
    public string Title { get; set; } = string.Empty;
    public string DistanceText { get; set; } = string.Empty;
}

public class CommunityReportsOverviewData
{
    public List<CommunityReportItem> Reports { get; set; } = new();
    public string ActionText { get; set; } = "Recent Reports Nearby";
    public string ModuleRoute { get; set; } = "//Reports/CommunityPosting";
    public string ModuleName { get; set; } = "Community Reports Module";
}

public class PASSData
{
    public string Title { get; set; } = "Preparation Assessment";
    public int ScorePercentage { get; set; } = 100;
    public string Description { get; set; } = "Evaluate your overall preparedness for emergencies and evacuation.";
    public string ButtonText { get; set; } = "Take Assessment";
    public string ModuleRoute { get; set; } = "//Prepare/PASS";
    public string ModuleName { get; set; } = "Preparation Assessment (PASS)";
}

public interface IDashboardDataService
{
    Task<DisasterInfoData> GetDisasterInfoAsync();
    Task<PreparednessData> GetPreparednessDataAsync();
    Task<EvacuationData> GetEvacuationDataAsync();
    Task<SafetyCircleOverviewData> GetSafetyCircleDataAsync();
    Task<CommunityReportsOverviewData> GetCommunityReportsDataAsync();
    Task<PASSData> GetPASSDataAsync();
}

public class DashboardDataService : IDashboardDataService
{
    public static IDashboardDataService Instance { get; set; } = new DashboardDataService();

    public Task<DisasterInfoData> GetDisasterInfoAsync()
    {
        return Task.FromResult(new DisasterInfoData());
    }

    public Task<PreparednessData> GetPreparednessDataAsync()
    {
        return Task.FromResult(new PreparednessData());
    }

    public Task<EvacuationData> GetEvacuationDataAsync()
    {
        return Task.FromResult(new EvacuationData());
    }

    public Task<SafetyCircleOverviewData> GetSafetyCircleDataAsync()
    {
        return Task.FromResult(new SafetyCircleOverviewData
        {
            Groups = new List<SafetyCircleGroupItem>
            {
                new SafetyCircleGroupItem { Name = "Second Fam Circle", StatusText = "1 member status unknown", IsAlert = true },
                new SafetyCircleGroupItem { Name = "Friends Circle", StatusText = "All members are safe", IsAlert = false }
            }
        });
    }

    public Task<CommunityReportsOverviewData> GetCommunityReportsDataAsync()
    {
        return Task.FromResult(new CommunityReportsOverviewData
        {
            Reports = new List<CommunityReportItem>
            {
                new CommunityReportItem { Title = "Roads are flooded nearby", DistanceText = "150 meters away" },
                new CommunityReportItem { Title = "Someone help us!", DistanceText = "600 meters away" }
            }
        });
    }

    public Task<PASSData> GetPASSDataAsync()
    {
        return Task.FromResult(new PASSData());
    }
}
