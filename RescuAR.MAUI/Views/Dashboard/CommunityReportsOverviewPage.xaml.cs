using RescuAR.App.ViewModels.Dashboard;

namespace RescuAR.App.Views.Dashboard;

public partial class CommunityReportsOverviewPage : ContentView
{
    public CommunityReportsOverviewPage()
    {
        InitializeComponent();
        BindingContext = new CommunityReportsOverviewViewModel();
    }
}
