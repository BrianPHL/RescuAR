using RescuAR.App.ViewModels.Dashboard;

namespace RescuAR.App.Views.Dashboard;

public partial class AreaStatusOverviewPage : ContentView
{
    public AreaStatusOverviewPage()
    {
        InitializeComponent();
        BindingContext = new AreaStatusOverviewViewModel();
    }
}
