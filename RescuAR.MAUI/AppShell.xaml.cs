namespace RescuAR.MAUI;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        RegisterRoutes();
    }

    private static void RegisterRoutes()
    {
        // Prepare
        Routing.RegisterRoute(
            "Prepare/Checklist",
            typeof(RescuAR.App.Views.Prepare.ChecklistPage));

        Routing.RegisterRoute(
            "Prepare/PASS",
            typeof(RescuAR.App.Views.Prepare.PASSPage));

        Routing.RegisterRoute(
            "Prepare/Assessment",
            typeof(RescuAR.App.Views.Prepare.AssessmentPage));

        Routing.RegisterRoute(
            "Prepare/EvacuationCenterInfo",
            typeof(RescuAR.App.Views.Prepare.EvacuationCenterInfoPage));

        Routing.RegisterRoute(
            "Prepare/HotlineDirectory",
            typeof(RescuAR.App.Views.Prepare.HotlineDirectoryPage));

        // Reports
        Routing.RegisterRoute(
            "AdvisoryFeedPage",
            typeof(RescuAR.App.Views.Reports.AdvisoryFeedPage));

        Routing.RegisterRoute(
            "Reports/AdvisoryFeed",
            typeof(RescuAR.App.Views.Reports.AdvisoryFeedPage));

        Routing.RegisterRoute(
            "Reports/CommunityPosting",
            typeof(RescuAR.App.ViewModels.Reports.CommunityPostingPage));

        Routing.RegisterRoute(
            "ReportDetails",
            typeof(RescuAR.App.Views.Reports.ReportDetailsPage));

        // Profile
        Routing.RegisterRoute(
            "ProfilePage",
            typeof(RescuAR.App.Views.Profile.ProfilePage));

        // Summary
        Routing.RegisterRoute(
            "SummaryPage",
            typeof(RescuAR.App.Views.Summary.SummaryPage));

        Routing.RegisterRoute(
            "AreaStatusSummaryPage",
            typeof(RescuAR.App.ViewModels.Summary.AreaStatusPage));

        // Batch 4: flood preparedness content
        Routing.RegisterRoute(
            "Prepare/FloodHistory",
            typeof(RescuAR.App.Views.Prepare.FloodHistoryPage));

        Routing.RegisterRoute(
            "FloodHistoryPage",
            typeof(RescuAR.App.Views.Prepare.FloodHistoryPage));

        Routing.RegisterRoute(
            "Prepare/HistoricalPhotos",
            typeof(RescuAR.App.Views.Prepare.HistoricalPhotosPage));

        Routing.RegisterRoute(
            "Prepare/DocumentaryVideos",
            typeof(RescuAR.App.Views.Prepare.DocumentaryVideosPage));

        Routing.RegisterRoute(
            "Prepare/FloodTimeline",
            typeof(RescuAR.App.Views.Prepare.FloodTimelinePage));
    }
}
