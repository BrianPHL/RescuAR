using RescuAR.App.Views.Profile;

namespace RescuAR.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register routes for Profile sub-pages
        Routing.RegisterRoute(nameof(HelpCenterPage), typeof(HelpCenterPage));
        Routing.RegisterRoute(nameof(PrivacyPolicyPage), typeof(PrivacyPolicyPage));
        Routing.RegisterRoute(nameof(TermsConditionsPage), typeof(TermsConditionsPage));
        Routing.RegisterRoute(nameof(SystemInformationPage), typeof(SystemInformationPage));
        Routing.RegisterRoute(nameof(Views.Summary.SummaryPage), typeof(Views.Summary.SummaryPage));
        Routing.RegisterRoute(nameof(Views.Summary.ActivityHistoryPage), typeof(Views.Summary.ActivityHistoryPage));
        Routing.RegisterRoute(nameof(Views.Summary.NearbySafeLocationsPage), typeof(Views.Summary.NearbySafeLocationsPage));
        Routing.RegisterRoute(nameof(Views.Summary.RecommendedActionPage), typeof(Views.Summary.RecommendedActionPage));
        Routing.RegisterRoute(nameof(Views.Summary.SituationAsessmentPage), typeof(Views.Summary.SituationAsessmentPage));
        Routing.RegisterRoute(nameof(Views.Summary.AreaStatusPage), typeof(Views.Summary.AreaStatusPage));
    }
}
